namespace CueVideo.Ndi;

/// <summary>
/// What a feed has to say while it has no picture: an NDI failure, or the senders it can see
/// while it waits for the wanted one.
/// </summary>
public sealed record NdiFeedStatus(string? Error, IReadOnlyList<string> SeenSources, bool Stopped = false);

/// <summary>
/// One receiver per source, shared by every subscription showing that source. A feed runs
/// only while it has subscribers: the first subscription opens the receiver and the last one
/// closes it after a short grace, so a rebuild can hand the same feed to its new views.
/// </summary>
public sealed class NdiHub : IDisposable
{
    private static readonly TimeSpan StopGrace = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly Dictionary<string, NdiFeed> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<INdiFinder> _finders;
    private readonly Func<string, INdiReceiver> _receivers;
    private bool _disposed;

    public NdiHub()
        : this(
            () => NdiFinder.Create(showLocalSources: true),
            sourceName => NdiReceiver.Open(sourceName, lowestBandwidth: true))
    {
    }

    /// <summary>Builds a hub around the NDI clients; tests pass fakes here.</summary>
    public NdiHub(Func<INdiFinder> finders, Func<string, INdiReceiver> receivers)
    {
        _finders = finders;
        _receivers = receivers;
    }

    /// <summary>
    /// Adds a subscriber and returns the handle that releases it. The frame handler runs on
    /// the feed's thread and the frame is only valid during the call.
    /// </summary>
    public IDisposable Subscribe(string sourceName, NdiFrameHandler onFrame, Action<NdiFeedStatus>? onStatus = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(onFrame);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_feeds.TryGetValue(sourceName, out var feed))
            {
                feed = new NdiFeed(sourceName, _finders, _receivers, FeedStopped);
                _feeds[sourceName] = feed;
            }

            var subscriber = feed.AddSubscriber(onFrame, onStatus);
            return new Subscription(this, feed, subscriber);
        }
    }

    private void Release(NdiFeed feed, NdiFeed.Subscriber subscriber)
    {
        lock (_gate)
        {
            feed.RemoveSubscriber(subscriber, StopGrace);
        }
    }

    /// <summary>A feed's thread has left; drop it, or resume it for a late subscriber.</summary>
    private void FeedStopped(NdiFeed feed)
    {
        lock (_gate)
        {
            if (_disposed) return;

            if (feed.HasSubscribers)
            {
                feed.Resume();
                return;
            }

            if (_feeds.TryGetValue(feed.SourceName, out var current) && ReferenceEquals(current, feed))
            {
                _feeds.Remove(feed.SourceName);
            }
        }
    }

    public void Dispose()
    {
        NdiFeed[] feeds;

        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            feeds = _feeds.Values.ToArray();
            _feeds.Clear();
        }

        // Signal every feed before waiting, so they stop together rather than one after another.
        foreach (var feed in feeds) feed.Stop();
        foreach (var feed in feeds) feed.Stop(wait: true);
    }

    private sealed class Subscription(NdiHub hub, NdiFeed feed, NdiFeed.Subscriber subscriber) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            hub.Release(feed, subscriber);
        }
    }

    /// <summary>One source's discovery, receiver and subscribers, alive only while retained.</summary>
    private sealed class NdiFeed
    {
        private readonly object _gate = new();
        private readonly Func<INdiFinder> _finders;
        private readonly Func<string, INdiReceiver> _receivers;
        private readonly Action<NdiFeed> _stopped;
        private static readonly TimeSpan FrameDrought = TimeSpan.FromSeconds(5);
        private readonly List<Subscriber> _subscribers = [];
        private Subscriber[] _snapshot = [];
        private Thread? _thread;
        private long _stopAtTicks = long.MaxValue;

        public NdiFeed(
            string sourceName,
            Func<INdiFinder> finders,
            Func<string, INdiReceiver> receivers,
            Action<NdiFeed> stopped)
        {
            SourceName = sourceName;
            _finders = finders;
            _receivers = receivers;
            _stopped = stopped;
        }

        public string SourceName { get; }

        public bool HasSubscribers
        {
            get
            {
                lock (_gate) return _subscribers.Count > 0;
            }
        }

        public Subscriber AddSubscriber(NdiFrameHandler onFrame, Action<NdiFeedStatus>? onStatus)
        {
            lock (_gate)
            {
                var subscriber = new Subscriber(onFrame, onStatus);
                _subscribers.Add(subscriber);
                Volatile.Write(ref _snapshot, _subscribers.ToArray());
                Volatile.Write(ref _stopAtTicks, long.MaxValue);

                if (_thread is not { IsAlive: true }) StartThreadLocked();

                return subscriber;
            }
        }

        public void RemoveSubscriber(Subscriber subscriber, TimeSpan grace)
        {
            lock (_gate)
            {
                if (!_subscribers.Remove(subscriber)) return;

                Volatile.Write(ref _snapshot, _subscribers.ToArray());

                if (_subscribers.Count > 0) return;

                // Set the stop deadline in the same lock that adds a subscriber, so a quick
                // re-subscribe either cancels it or is counted before it is set.
                Volatile.Write(ref _stopAtTicks, DateTime.UtcNow.Add(grace).Ticks);
            }
        }

        /// <summary>Stops now; the thread disposes the receiver as it leaves.</summary>
        public void Stop(bool wait = false)
        {
            Thread? thread;

            lock (_gate)
            {
                Volatile.Write(ref _stopAtTicks, 0);
                thread = _thread;
            }

            if (!wait || thread is null || thread == Thread.CurrentThread) return;
            thread.Join(TimeSpan.FromSeconds(1));
        }

        public void Resume()
        {
            lock (_gate)
            {
                Volatile.Write(ref _stopAtTicks, long.MaxValue);
                if (_thread is not { IsAlive: true }) StartThreadLocked();
            }
        }

        private void StartThreadLocked()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = $"ndi:{SourceName}" };
            _thread.Start();
        }

        private bool KeepRunning => Volatile.Read(ref _stopAtTicks) > DateTime.UtcNow.Ticks;

        private void Run()
        {
            while (KeepRunning)
            {
                try
                {
                    RunFeed();
                }
                catch (Exception ex)
                {
                    Report(ex);
                    if (!Wait(2000)) break;
                }
            }

            lock (_gate)
            {
                if (ReferenceEquals(_thread, Thread.CurrentThread)) _thread = null;
            }

            _stopped(this);
        }

        private void RunFeed()
        {
            INdiFinder finder;

            try
            {
                finder = _finders();
            }
            catch (Exception ex)
            {
                Report(ex);
                Wait(2000);
                return;
            }

            using (finder)
            {
                while (KeepRunning)
                {
                    var source = WaitForSource(finder);
                    if (source is null) continue;

                    INdiReceiver receiver;

                    try
                    {
                        receiver = _receivers(source.Name);
                    }
                    catch (Exception ex)
                    {
                        Report(ex);
                        if (!Wait(500)) return;
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
        private NdiSourceInfo? WaitForSource(INdiFinder finder)
        {
            IReadOnlyList<NdiSourceInfo> sources;

            try
            {
                sources = finder.GetSources(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Report(ex);
                Wait(500);
                return null;
            }

            var match = sources.FirstOrDefault(source =>
                    source.Name.Equals(SourceName, StringComparison.OrdinalIgnoreCase))
                ?? sources.FirstOrDefault(source =>
                    source.ShortName.Equals(SourceName, StringComparison.OrdinalIgnoreCase))
                ?? sources.FirstOrDefault(source =>
                    source.Name.Contains(SourceName, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var seen = sources.Select(source => source.ShortName).ToArray();
                Report(new NdiFeedStatus(null, seen));
            }

            return match;
        }

        private void Pump(INdiReceiver receiver)
        {
            var lastFrame = DateTime.UtcNow;
            var gotFrame = false;

            while (KeepRunning && DateTime.UtcNow - lastFrame < FrameDrought)
            {
                bool captured;

                try
                {
                    captured = receiver.TryCapture(200, Deliver);
                }
                catch (Exception ex)
                {
                    Report(ex);
                    return;
                }

                if (captured)
                {
                    lastFrame = DateTime.UtcNow;
                    gotFrame = true;
                }
            }

            // The last picture is still on screen; say it stopped before trying again.
            if (gotFrame && KeepRunning) Report(new NdiFeedStatus(null, [], Stopped: true));
        }

        private void Deliver(in NdiFrameView frame)
        {
            foreach (var subscriber in Volatile.Read(ref _snapshot))
            {
                try
                {
                    subscriber.OnFrame(in frame);
                }
                catch (Exception)
                {
                    // One subscription must not stop the picture for the others.
                }
            }
        }

        private void Report(NdiFeedStatus status)
        {
            foreach (var subscriber in Volatile.Read(ref _snapshot))
            {
                try
                {
                    subscriber.OnStatus?.Invoke(status);
                }
                catch (Exception)
                {
                    // A lost UI must not stop the feed for the other subscribers.
                }
            }
        }

        private void Report(Exception exception) => Report(new NdiFeedStatus(exception.Message, []));

        /// <summary>Sleeps in short steps so a stop does not wait out the whole delay.</summary>
        private bool Wait(int milliseconds)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);

            while (KeepRunning && DateTime.UtcNow < until) Thread.Sleep(50);

            return KeepRunning;
        }

        public sealed record Subscriber(NdiFrameHandler OnFrame, Action<NdiFeedStatus>? OnStatus);
    }
}
