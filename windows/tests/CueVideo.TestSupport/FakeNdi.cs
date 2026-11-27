using CueVideo.Ndi;

namespace CueVideo.TestSupport;

/// <summary>
/// An NDI network that reports fixed sources and hands out receivers which emit a small
/// frame on every capture. Lets tests watch how many receivers a hub opens.
/// </summary>
public sealed class FakeNdi
{
    private static readonly byte[] Frame = new byte[16];

    private readonly object _gate = new();
    private readonly List<FakeNdiReceiver> _receivers = [];
    private readonly List<string> _openedNames = [];

    public FakeNdi(params string[] sources) =>
        Sources = sources.Select(name => new NdiSourceInfo(name, "")).ToArray();

    public IReadOnlyList<NdiSourceInfo> Sources { get; }

    public int OpenReceiverCount
    {
        get
        {
            lock (_gate) return _receivers.Count(receiver => !receiver.IsDisposed);
        }
    }

    public IReadOnlyList<FakeNdiReceiver> Receivers
    {
        get
        {
            lock (_gate) return _receivers.ToArray();
        }
    }

    /// <summary>The full advertised name each receiver was opened with, in order.</summary>
    public IReadOnlyList<string> OpenedNames
    {
        get
        {
            lock (_gate) return _openedNames.ToArray();
        }
    }

    public NdiHub CreateHub() => new(CreateFinder, OpenReceiver);

    public INdiFinder CreateFinder() => new FakeNdiFinder(() => Sources);

    public INdiReceiver OpenReceiver(string sourceName)
    {
        var receiver = new FakeNdiReceiver(Frame);
        lock (_gate)
        {
            _openedNames.Add(sourceName);
            _receivers.Add(receiver);
        }

        return receiver;
    }

    private sealed class FakeNdiFinder(Func<IReadOnlyList<NdiSourceInfo>> sources) : INdiFinder
    {
        public IReadOnlyList<NdiSourceInfo> GetSources(TimeSpan? wait = null) => sources();

        public void Dispose() { }
    }
}

/// <summary>An NDI receiver that shows a 2x2 picture forever and records its own disposal.</summary>
public sealed class FakeNdiReceiver(byte[] pixels) : INdiReceiver
{
    private int _captures;
    private int _disposed;

    public int Captures => Volatile.Read(ref _captures);

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public bool TryCapture(int timeoutMs, NdiFrameHandler handler)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        Interlocked.Increment(ref _captures);

        var frame = new NdiFrameView
        {
            Width = 2,
            Height = 2,
            Stride = 8,
            FourCc = "BGRA",
            Aspect = 1,
            FrameRateN = 30,
            FrameRateD = 1,
            Timestamp = 0,
            Pixels = pixels,
        };

        handler(in frame);
        Thread.Sleep(5);
        return true;
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
