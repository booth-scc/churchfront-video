using System.Text.Json;

namespace CueVideo.Core;

/// <summary>
/// Keeps config.json and the running app in step: a hand edit to the file is picked up within
/// a second, an edit made in the app is written back, and a copy of the last setup that parsed
/// cleanly sits beside it so a typo never leaves the app with nothing. A demo or self-test run
/// (any <c>CUEVIDEO_</c> environment setting) reads the file but never writes it.
/// </summary>
public sealed class ConfigStore : IDisposable
{
    /// <summary>How often a running app looks for a hand edit; half a second, as the Mac polls.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>An empty setup never replaces the last good copy, as on the Mac.</summary>
    private static readonly string EmptyConfigJson = JsonSerializer.Serialize(new CueConfig(), Options);

    private readonly object _gate = new();
    private Timer? _timer;
    private (DateTime WrittenUtc, long Length)? _lastSignature;
    private bool _preserveFileOnSave;
    private volatile bool _disposed;
    private volatile CueConfig _config = new();
    private volatile string? _fileError;
    private int _version;

    public ConfigStore(string? directory = null, bool? readOnly = null)
    {
        directory ??= Environment.GetEnvironmentVariable("CUEVIDEO_CONFIG_DIR") is { Length: > 0 } custom
            ? custom
            : null;

        Directory = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CueVideo");
        Path = System.IO.Path.Combine(Directory, "config.json");
        LastGoodPath = System.IO.Path.Combine(Directory, "config.last-good.json");
        ReadOnly = readOnly ?? IsSelfTestRun(Environment.GetEnvironmentVariables().Keys.Cast<string>());
    }

    public string Directory { get; }
    public string Path { get; }
    public string LastGoodPath { get; }

    /// <summary>True for demo and self-test runs, which must never write over a setup.</summary>
    public bool ReadOnly { get; }

    public CueConfig Config => _config;
    public string? FileError => _fileError;
    public bool UsedLastGood { get; private set; }
    public bool CreatedDefaults { get; private set; }

    /// <summary>Counts applied changes, so a watcher can apply each change once.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Raised after config.json changed on disk, after this store changed the setup through
    /// <see cref="Update"/>, or when a hand edit made the file unreadable or readable again.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// A run that names its own folder, switcher or feeds with a <c>CUEVIDEO_</c> setting is a
    /// demo or self-test; it must never write over the real setup.
    /// </summary>
    public static bool IsSelfTestRun(IEnumerable<string> environmentNames) =>
        environmentNames.Any(name => name.StartsWith("CUEVIDEO_", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the setup and starts watching the file for hand edits.</summary>
    public void Load()
    {
        lock (_gate)
        {
            _fileError = null;
            UsedLastGood = false;
            CreatedDefaults = false;
            _preserveFileOnSave = false;
            _lastSignature = Signature();

            ReadSetup();
        }

        StartPolling();
    }

    /// <summary>Writes the setup beside a copy of the last good one. False in a demo run.</summary>
    public bool Save()
    {
        if (ReadOnly) return false;

        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                // A hand-written file that did not load may hold work that is not in the app.
                // Keep it under another name first; if that fails, leave it alone.
                if (_preserveFileOnSave && File.Exists(Path)) CopyUnreadableAside();

                var json = Serialize(Config);
                var temporary = Path + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, Path, true);

                _preserveFileOnSave = false;
                _lastSignature = Signature();
                _fileError = null;
                KeepLastGood(Config, json);
                return true;
            }
            catch (Exception ex)
            {
                _fileError = $"config.json could not be written: {ex.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Changes the setup from the app: keeps the change in memory even in a demo run, writes
    /// the file when it can, and raises <see cref="Changed"/> once when anything changed.
    /// </summary>
    public bool Update(Action<CueConfig> change)
    {
        bool changed;

        lock (_gate)
        {
            var before = Serialize(Config);
            change(Config);
            changed = !string.Equals(Serialize(Config), before, StringComparison.Ordinal);

            if (changed)
            {
                Save();
                Interlocked.Increment(ref _version);
            }
        }

        if (changed) Changed?.Invoke();
        return changed;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void ReadSetup()
    {
        if (File.Exists(Path))
        {
            string? json = null;

            try
            {
                json = File.ReadAllText(Path);
            }
            catch (Exception ex)
            {
                _fileError = $"config.json could not be read: {ex.Message}";
                _preserveFileOnSave = true;
            }

            if (json is not null)
            {
                try
                {
                    _config = Parse(json);
                    _preserveFileOnSave = false;
                    KeepLastGood(_config, json);
                    return;
                }
                catch (Exception ex)
                {
                    _fileError = $"config.json could not be parsed: {ex.Message}";
                    _preserveFileOnSave = true;
                }
            }
        }

        if (File.Exists(LastGoodPath))
        {
            try
            {
                _config = Parse(File.ReadAllText(LastGoodPath));
                UsedLastGood = true;
                return;
            }
            catch (Exception ex)
            {
                _fileError = $"{_fileError} Last good copy also failed: {ex.Message}";
            }
        }

        _config = CueConfig.BoothDefaults();
        CreatedDefaults = true;

        // A file that exists but would not load stays exactly as written; the next save
        // copies it aside first.
        if (!_preserveFileOnSave) Save();
    }

    private void StartPolling()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer ??= new Timer(_ => Poll(), null, PollInterval, PollInterval);
        }
    }

    private void Poll()
    {
        if (_disposed) return;

        var signature = Signature();

        int version;
        lock (_gate)
        {
            if (signature == _lastSignature) return;
            version = _version;
        }

        string json;
        try
        {
            json = File.ReadAllText(Path);
        }
        catch (Exception ex)
        {
            // The file may be locked for a moment while an editor or sync client writes it.
            // Keep the last setup, say so, and look again next tick.
            var error = $"config.json could not be read: {ex.Message}";
            var changed = false;

            lock (_gate)
            {
                if (error != _fileError)
                {
                    _fileError = error;
                    changed = true;
                }
            }

            if (changed) Changed?.Invoke();     // the status line now says what is wrong
            return;
        }

        CueConfig parsed;
        try
        {
            parsed = Parse(json);
        }
        catch (Exception ex)
        {
            // The file is readable but will not parse. Keep a copy before any save replaces it.
            var error = $"config.json could not be parsed: {ex.Message}";
            var changed = false;

            lock (_gate)
            {
                _lastSignature = signature;
                _preserveFileOnSave = true;
                if (error != _fileError)
                {
                    _fileError = error;
                    changed = true;
                }
            }

            if (changed) Changed?.Invoke();
            return;
        }

        var raised = false;

        lock (_gate)
        {
            // An in-app edit landed while the file was being read: keep it, and let the next
            // poll look at the file again.
            if (_version != version) return;

            _lastSignature = signature;

            // The file is readable again, even if its setup matches what is already loaded.
            raised = _fileError is not null;
            _preserveFileOnSave = false;
            _fileError = null;

            if (!string.Equals(Serialize(parsed), Serialize(Config), StringComparison.Ordinal))
            {
                _config = parsed;
                Interlocked.Increment(ref _version);
                raised = true;
            }

            KeepLastGood(parsed, json);
        }

        if (raised) Changed?.Invoke();
    }

    private (DateTime WrittenUtc, long Length)? Signature()
    {
        try
        {
            var file = new FileInfo(Path);
            return file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Keeps a hand-written file that did not parse, so a save cannot destroy it.</summary>
    private void CopyUnreadableAside()
    {
        var aside = System.IO.Path.Combine(Directory, $"config.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(Path, aside);
    }

    private void KeepLastGood(CueConfig config, string json)
    {
        // A demo run never writes, and an empty setup never replaces a real one.
        if (ReadOnly || Serialize(config) == EmptyConfigJson) return;

        try
        {
            if (File.Exists(LastGoodPath) && File.ReadAllText(LastGoodPath) == json) return;
            File.WriteAllText(LastGoodPath, json);
        }
        catch (Exception)
        {
            // A missing safety copy is not worth failing the load over.
        }
    }

    private static string Serialize(CueConfig config) => JsonSerializer.Serialize(config, Options);

    private static CueConfig Parse(string json) =>
        JsonSerializer.Deserialize<CueConfig>(json, Options) ?? throw new InvalidDataException("empty config");
}
