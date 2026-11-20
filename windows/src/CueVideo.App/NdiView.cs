using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CueVideo.Ndi;

namespace CueVideo.App;

/// <summary>
/// Shows one NDI sender. Frames are copied into a WriteableBitmap on the UI thread;
/// older frames are dropped rather than queued, so the picture stays live.
/// </summary>
public sealed class NdiView : Grid, IDisposable
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status;
    private readonly string _sourceName;
    private readonly bool _lowestBandwidth;

    private WriteableBitmap? _bitmap;
    private Thread? _thread;
    private volatile bool _running = true;
    private int _pending;
    private bool _gotFrame;

    public NdiView(string sourceName, string emptyText, bool lowestBandwidth = true)
    {
        _sourceName = sourceName;
        _lowestBandwidth = lowestBandwidth;

        _status = new TextBlock
        {
            Text = emptyText,
            FontSize = 13,
            LetterSpacing = 2,
            Foreground = new SolidColorBrush(Color.Parse("#3C4150")),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        Children.Add(_image);
        Children.Add(_status);
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = $"ndi:{_sourceName}" };
        _thread.Start();
    }

    private void Loop()
    {
        NdiFinder finder;
        try
        {
            finder = NdiFinder.Create(showLocalSources: true);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => _status.Text = $"NDI: {ex.Message}");
            return;
        }

        using (finder)
        {
            while (_running)
            {
                var source = WaitForSource(finder);
                if (source is null) continue;

                NdiReceiver? receiver = null;
                try
                {
                    receiver = NdiReceiver.Open(source.Name, _lowestBandwidth);
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => _status.Text = $"NDI: {ex.Message}");
                    Thread.Sleep(500);
                    continue;
                }

                using (receiver)
                {
                    Pump(receiver);
                }
            }
        }
    }

    /// <summary>NDI opens a receiver by the full advertised name, not the short one.</summary>
    private NdiSourceInfo? WaitForSource(NdiFinder finder)
    {
        var sources = finder.GetSources(TimeSpan.FromSeconds(1));

        var match = sources.FirstOrDefault(s =>
            s.Name.Equals(_sourceName, StringComparison.OrdinalIgnoreCase) ||
            s.ShortName.Equals(_sourceName, StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains(_sourceName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_gotFrame)
                {
                    var visible = string.Join(", ", sources.Take(3).Select(s => s.ShortName));
                    _status.Text = sources.Count == 0
                        ? $"WAITING FOR NDI\n{_sourceName}"
                        : $"WAITING FOR NDI\n{_sourceName}\nseen: {visible}";
                }
            });
        }

        return match;
    }

    private void Pump(NdiReceiver receiver)
    {
        var lastFrame = DateTime.UtcNow;

        while (_running && (DateTime.UtcNow - lastFrame).TotalSeconds < 5)
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            {
                Thread.Sleep(5);
                continue;
            }

            var captured = receiver.TryCapture(200, (in NdiFrameView frame) => Copy(frame));
            if (captured) lastFrame = DateTime.UtcNow;
            else Interlocked.Exchange(ref _pending, 0);
        }
    }

    private void Copy(in NdiFrameView frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var stride = frame.Stride;
        var rowBytes = width * 4;

        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(rowBytes * height);
        for (var y = 0; y < height; y++)
        {
            frame.Pixels.Slice(y * stride, rowBytes).CopyTo(buffer.AsSpan(y * rowBytes, rowBytes));
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Blit(buffer, width, height);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                _status.IsVisible = false;
                Interlocked.Exchange(ref _pending, 0);
            }
        });
    }

    private void Blit(byte[] buffer, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _image.Source = _bitmap;
        }

        using var locked = _bitmap.Lock();
        var rowBytes = width * 4;

        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(buffer, y * rowBytes, locked.Address + y * locked.RowBytes, rowBytes);
        }

        _gotFrame = true;
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(500); } catch (Exception) { /* shutting down */ }
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
