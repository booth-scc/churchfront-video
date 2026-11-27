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
/// Shows one NDI sender from the shared feed hub. Frames are copied into a WriteableBitmap on
/// the UI thread; older frames are dropped rather than queued, so the picture stays live.
/// The view retains the feed while it is showing and releases it when it closes.
/// </summary>
public sealed class NdiView : Grid, IDisposable
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status;
    private readonly NdiHub _hub;
    private readonly string _sourceName;
    private readonly string _waitingText;

    private WriteableBitmap? _bitmap;
    private IDisposable? _subscription;
    private volatile bool _closed;
    private int _pending;

    public NdiView(NdiHub hub, string sourceName, string emptyText)
    {
        _hub = hub;
        _sourceName = sourceName;
        _waitingText = string.IsNullOrWhiteSpace(emptyText)
            ? $"WAITING FOR NDI\n{sourceName}"
            : emptyText;

        _status = new TextBlock
        {
            Text = _waitingText,
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
        ObjectDisposedException.ThrowIf(_closed, this);
        _subscription ??= _hub.Subscribe(_sourceName, OnFrame, OnStatus);
    }

    private void OnFrame(in NdiFrameView frame)
    {
        if (_closed || Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;

        try
        {
            Copy(in frame);
        }
        catch (Exception)
        {
            // Let the next frame try again rather than freezing this tile for good.
            Interlocked.Exchange(ref _pending, 0);
            throw;
        }
    }

    private void OnStatus(NdiFeedStatus status)
    {
        if (_closed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;

            if (status.Error is { } error)
            {
                _status.Text = $"NDI: {error}";
            }
            else if (status.Stopped)
            {
                _status.Text = $"PICTURE STOPPED\n{_sourceName}";
            }
            else if (status.SeenSources.Count == 0)
            {
                _status.Text = _waitingText;
            }
            else
            {
                _status.Text = $"{_waitingText}\nseen: {string.Join(", ", status.SeenSources.Take(3))}";
            }

            _status.IsVisible = true;
        });
    }

    private void Copy(in NdiFrameView frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var stride = frame.Stride;
        var rowBytes = width * 4;

        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(rowBytes * height);
        var posted = false;

        try
        {
            for (var y = 0; y < height; y++)
            {
                frame.Pixels.Slice(y * stride, rowBytes).CopyTo(buffer.AsSpan(y * rowBytes, rowBytes));
            }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_closed) return;
                    Blit(buffer, width, height);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                    if (!_closed)
                    {
                        _status.IsVisible = false;
                        Interlocked.Exchange(ref _pending, 0);
                    }
                }
            });

            posted = true;
        }
        finally
        {
            if (!posted) System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
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
    }

    public void Dispose()
    {
        _closed = true;
        _subscription?.Dispose();
        _subscription = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
