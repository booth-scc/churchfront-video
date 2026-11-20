using System.Runtime.InteropServices;

namespace CueVideo.Ndi;

/// <summary>One NDI sender visible on the network.</summary>
public sealed record NdiSourceInfo(string Name, string Url)
{
    /// <summary>NDI advertises senders as "MACHINE NAME (Source Name)".</summary>
    public string ShortName
    {
        get
        {
            var open = Name.LastIndexOf('(');
            var close = Name.LastIndexOf(')');
            return open >= 0 && close > open ? Name[(open + 1)..close] : Name;
        }
    }
}

/// <summary>Wraps NDIlib_find: the list of senders currently on the network.</summary>
public sealed class NdiFinder : IDisposable
{
    private readonly object _gate = new();
    private readonly IntPtr _finder;
    private readonly NdiRuntime.FindWaitForSourcesFn _wait;
    private readonly NdiRuntime.FindGetCurrentSourcesFn _sources;
    private readonly NdiRuntime.FindDestroyFn _destroy;
    private bool _disposed;

    private NdiFinder(IntPtr finder)
    {
        _finder = finder;
        _wait = NdiRuntime.Get<NdiRuntime.FindWaitForSourcesFn>("NDIlib_find_wait_for_sources");
        _sources = NdiRuntime.Get<NdiRuntime.FindGetCurrentSourcesFn>("NDIlib_find_get_current_sources");
        _destroy = NdiRuntime.Get<NdiRuntime.FindDestroyFn>("NDIlib_find_destroy");
    }

    public static NdiFinder Create(bool showLocalSources = false)
    {
        if (!NdiRuntime.Initialize())
            throw new NdiUnavailableException(NdiRuntime.Error ?? "NDI runtime is not available.");

        var create = new FindCreate { ShowLocalSources = showLocalSources };
        var finder = NdiRuntime.Get<NdiRuntime.FindCreateV2Fn>("NDIlib_find_create_v2")(ref create);
        if (finder == IntPtr.Zero) throw new NdiUnavailableException("NDIlib_find_create_v2 failed.");

        return new NdiFinder(finder);
    }

    /// <summary>Sources visible now; waits up to <paramref name="wait"/> for a change first.</summary>
    public IReadOnlyList<NdiSourceInfo> GetSources(TimeSpan? wait = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (wait is { } timeout && timeout > TimeSpan.Zero)
            {
                _wait(_finder, (uint)timeout.TotalMilliseconds);
            }

            var pointer = _sources(_finder, out var count);
            if (pointer == IntPtr.Zero || count == 0) return [];

            var size = Marshal.SizeOf<NdiSource>();
            var result = new List<NdiSourceInfo>((int)count);

            for (var i = 0; i < count; i++)
            {
                var source = Marshal.PtrToStructure<NdiSource>(IntPtr.Add(pointer, i * size));
                var name = Marshal.PtrToStringUTF8(source.Name);
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(new NdiSourceInfo(name, Marshal.PtrToStringUTF8(source.Url) ?? ""));
            }

            return result;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _destroy(_finder);
        }
    }
}
