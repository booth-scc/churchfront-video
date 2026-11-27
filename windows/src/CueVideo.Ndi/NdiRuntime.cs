using System.Runtime.InteropServices;

namespace CueVideo.Ndi;

/// <summary>Thrown when the NDI runtime is missing or fails to initialise.</summary>
public sealed class NdiUnavailableException(string message) : Exception(message);

public static class NdiRuntime
{
    private const string DllName = "Processing.NDI.Lib.x64.dll";

    private static readonly object Gate = new();
    private static IntPtr _library;
    private static bool _attempted;

    public static bool IsAvailable { get; private set; }
    public static string? RuntimePath { get; private set; }
    public static string? Error { get; private set; }

    /// <summary>Loads the NDI runtime and calls NDIlib_initialize. Idempotent.</summary>
    public static bool Initialize()
    {
        lock (Gate)
        {
            if (_attempted) return IsAvailable;
            _attempted = true;

            foreach (var candidate in Candidates())
            {
                if (!File.Exists(candidate)) continue;

                try
                {
                    _library = NativeLibrary.Load(candidate);
                }
                catch (Exception ex)
                {
                    Error = $"{candidate}: {ex.Message}";
                    continue;
                }

                RuntimePath = candidate;

                try
                {
                    var initialize = Get<InitializeFn>("NDIlib_initialize");
                    if (!initialize())
                    {
                        Error = $"{candidate}: NDIlib_initialize returned false";
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Error = $"{candidate}: {ex.Message}";
                    continue;
                }

                IsAvailable = true;
                return true;
            }

            Error ??= "NDI runtime not found. Install NDI 6 Tools.";
            return false;
        }
    }

    internal static T Get<T>(string export) where T : Delegate
    {
        if (_library == IntPtr.Zero)
            throw new NdiUnavailableException(Error ?? "NDI runtime is not loaded.");

        var address = NativeLibrary.GetExport(_library, export);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (var variable in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5", "NDI_RUNTIME_DIR" })
        {
            var directory = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(directory)) yield return Path.Combine(directory, DllName);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "NDI", "NDI 6 Tools", "Runtime", DllName);
        yield return Path.Combine(programFiles, "NDI", "NDI 5 Tools", "Runtime", DllName);

        yield return Path.Combine(AppContext.BaseDirectory, DllName);
    }

    // ---- Delegates (cdecl, as documented in Processing.NDI.Lib.h) ----

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool InitializeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr FindCreateV2Fn(ref FindCreate desc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool FindWaitForSourcesFn(IntPtr finder, uint timeoutMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr FindGetCurrentSourcesFn(IntPtr finder, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FindDestroyFn(IntPtr finder);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr RecvCreateV3Fn(ref RecvCreateV3 desc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int RecvCaptureV2Fn(IntPtr recv, ref VideoFrameV2 video, IntPtr audio, IntPtr metadata, uint timeoutMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RecvFreeVideoV2Fn(IntPtr recv, ref VideoFrameV2 video);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RecvDestroyFn(IntPtr recv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr SendCreateFn(ref SendCreate desc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SendVideoV2Fn(IntPtr sender, ref VideoFrameV2 video);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SendGetNoConnectionsFn(IntPtr sender, uint timeoutMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SendDestroyFn(IntPtr sender);
}

/// <summary>NDI struct layouts, matching Processing.NDI.structs.h (v5 and v6).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NdiSource
{
    public IntPtr Name;
    public IntPtr Url;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FindCreate
{
    [MarshalAs(UnmanagedType.I1)] public bool ShowLocalSources;
    public IntPtr Groups;
    public IntPtr ExtraIps;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RecvCreateV3
{
    public NdiSource Source;
    public int ColorFormat;
    public int Bandwidth;
    [MarshalAs(UnmanagedType.I1)] public bool AllowVideoFields;
    public IntPtr RecvName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SendCreate
{
    public IntPtr Name;
    public IntPtr Groups;
    [MarshalAs(UnmanagedType.I1)] public bool ClockVideo;
    [MarshalAs(UnmanagedType.I1)] public bool ClockAudio;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VideoFrameV2
{
    public int Xres;
    public int Yres;
    public int FourCc;
    public int FrameRateN;
    public int FrameRateD;
    public float Aspect;
    public int FrameFormatType;
    public long Timecode;
    public IntPtr Data;
    public int LineStride;
    public IntPtr Metadata;
    public long Timestamp;
}
