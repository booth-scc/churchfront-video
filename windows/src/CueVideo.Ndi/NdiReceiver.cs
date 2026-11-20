using System.Runtime.InteropServices;
using System.Text;

namespace CueVideo.Ndi;

/// <summary>
/// A received frame. Valid only inside the handler passed to
/// <see cref="NdiReceiver.TryCapture"/>; the SDK recycles the buffer after it returns.
/// </summary>
public readonly ref struct NdiFrameView
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    public required string FourCc { get; init; }
    public required float Aspect { get; init; }
    public required int FrameRateN { get; init; }
    public required int FrameRateD { get; init; }
    public required long Timestamp { get; init; }

    /// <summary>Pixels, BGRX or BGRA depending on <see cref="FourCc"/>.</summary>
    public required ReadOnlySpan<byte> Pixels { get; init; }

    public bool HasAlpha => FourCc is "BGRA" or "RGBA";
}

public delegate void NdiFrameHandler(in NdiFrameView frame);

/// <summary>Wraps NDIlib_recv for one sender.</summary>
public sealed class NdiReceiver : IDisposable
{
    private const int FrameTypeVideo = 1;
    private const int FrameTypeError = 4;

    private readonly IntPtr _receiver;
    private readonly NdiRuntime.RecvCaptureV2Fn _capture;
    private readonly NdiRuntime.RecvFreeVideoV2Fn _freeVideo;
    private readonly NdiRuntime.RecvDestroyFn _destroy;
    private bool _disposed;

    private NdiReceiver(IntPtr receiver, string sourceName)
    {
        _receiver = receiver;
        SourceName = sourceName;
        _capture = NdiRuntime.Get<NdiRuntime.RecvCaptureV2Fn>("NDIlib_recv_capture_v2");
        _freeVideo = NdiRuntime.Get<NdiRuntime.RecvFreeVideoV2Fn>("NDIlib_recv_free_video_v2");
        _destroy = NdiRuntime.Get<NdiRuntime.RecvDestroyFn>("NDIlib_recv_destroy");
    }

    public string SourceName { get; }

    /// <summary>
    /// Opens a receiver. <paramref name="lowestBandwidth"/> asks the sender for its
    /// proxy stream (about 640 wide), which is what the camera tiles use.
    /// </summary>
    public static NdiReceiver Open(string sourceName, bool lowestBandwidth = true, string receiverName = "CueVideo")
    {
        if (!NdiRuntime.Initialize())
            throw new NdiUnavailableException(NdiRuntime.Error ?? "NDI runtime is not available.");

        var namePointer = Marshal.StringToCoTaskMemUTF8(sourceName);
        var receiverNamePointer = Marshal.StringToCoTaskMemUTF8(receiverName);

        try
        {
            var create = new RecvCreateV3
            {
                Source = new NdiSource { Name = namePointer, Url = IntPtr.Zero },
                ColorFormat = 0, // BGRX, or BGRA when the sender has alpha
                Bandwidth = lowestBandwidth ? 0 : 100,
                AllowVideoFields = false,
                RecvName = receiverNamePointer,
            };

            var receiver = NdiRuntime.Get<NdiRuntime.RecvCreateV3Fn>("NDIlib_recv_create_v3")(ref create);
            if (receiver == IntPtr.Zero) throw new NdiUnavailableException($"Could not open receiver for '{sourceName}'.");

            return new NdiReceiver(receiver, sourceName);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePointer);
            Marshal.FreeCoTaskMem(receiverNamePointer);
        }
    }

    /// <summary>Waits up to <paramref name="timeoutMs"/> for a video frame, then hands it over.</summary>
    public unsafe bool TryCapture(int timeoutMs, NdiFrameHandler handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var frame = default(VideoFrameV2);
        var type = _capture(_receiver, ref frame, IntPtr.Zero, IntPtr.Zero, (uint)Math.Max(0, timeoutMs));

        if (type == FrameTypeError) return false;
        if (type != FrameTypeVideo || frame.Data == IntPtr.Zero || frame.Xres <= 0 || frame.Yres <= 0) return false;

        try
        {
            var stride = frame.LineStride > 0 ? frame.LineStride : frame.Xres * 4;
            var view = new NdiFrameView
            {
                Width = frame.Xres,
                Height = frame.Yres,
                Stride = stride,
                FourCc = FourCcToString(frame.FourCc),
                Aspect = frame.Aspect,
                FrameRateN = frame.FrameRateN,
                FrameRateD = frame.FrameRateD,
                Timestamp = frame.Timestamp,
                Pixels = new ReadOnlySpan<byte>(frame.Data.ToPointer(), stride * frame.Yres),
            };

            handler(in view);
            return true;
        }
        finally
        {
            _freeVideo(_receiver, ref frame);
        }
    }

    private static string FourCcToString(int value)
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)(value & 0xFF);
        chars[1] = (char)((value >> 8) & 0xFF);
        chars[2] = (char)((value >> 16) & 0xFF);
        chars[3] = (char)((value >> 24) & 0xFF);
        return new string(chars);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _destroy(_receiver);
    }
}

/// <summary>Optional NDI sender; used by tests and diagnostics, not by the operator UI.</summary>
public sealed class NdiSender : IDisposable
{
    private readonly IntPtr _sender;
    private readonly NdiRuntime.SendVideoV2Fn _send;
    private readonly NdiRuntime.SendDestroyFn _destroy;
    private bool _disposed;

    private NdiSender(IntPtr sender)
    {
        _sender = sender;
        _send = NdiRuntime.Get<NdiRuntime.SendVideoV2Fn>("NDIlib_send_send_video_v2");
        _destroy = NdiRuntime.Get<NdiRuntime.SendDestroyFn>("NDIlib_send_destroy");
    }

    public static NdiSender Create(string name)
    {
        if (!NdiRuntime.Initialize())
            throw new NdiUnavailableException(NdiRuntime.Error ?? "NDI runtime is not available.");

        var namePointer = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            var create = new SendCreate { Name = namePointer, ClockVideo = true, ClockAudio = false };
            var sender = NdiRuntime.Get<NdiRuntime.SendCreateFn>("NDIlib_send_create")(ref create);
            if (sender == IntPtr.Zero) throw new NdiUnavailableException($"Could not create NDI sender '{name}'.");
            return new NdiSender(sender);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePointer);
        }
    }

    /// <summary>Sends one BGRX frame; the buffer must stay alive for the duration of the call.</summary>
    public void Send(ReadOnlySpan<byte> bgrx, int width, int height, int stride, int frameRateN = 30000, int frameRateD = 1001)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        unsafe
        {
            fixed (byte* data = bgrx)
            {
                var frame = new VideoFrameV2
                {
                    Xres = width,
                    Yres = height,
                    FourCc = MakeFourCc('B', 'G', 'R', 'X'),
                    FrameRateN = frameRateN,
                    FrameRateD = frameRateD,
                    Aspect = (float)width / height,
                    FrameFormatType = 1, // progressive
                    Timecode = long.MaxValue, // synthesize
                    Data = (IntPtr)data,
                    LineStride = stride,
                    Timestamp = 0,
                };

                _send(_sender, ref frame);
            }
        }
    }

    private static int MakeFourCc(char a, char b, char c, char d) =>
        a | (b << 8) | (c << 16) | (d << 24);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _destroy(_sender);
    }
}
