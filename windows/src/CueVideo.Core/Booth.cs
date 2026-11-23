using CueVideo.Atem;
using CueVideo.Visca;

namespace CueVideo.Core;

/// <summary>
/// What the whole app shares, ported from the Mac's Booth: the setup file, the switcher's
/// last state, and the camera links. The window renders <see cref="Render"/> and sends
/// Operator and Engineer commands back through here; it never subscribes to the switcher,
/// loads or watches the setup file, or opens a camera itself.
/// </summary>
public sealed class Booth : IDisposable
{
    private readonly object _gate = new();
    private readonly ConfigStore _store;
    private readonly ISwitcher _switcher;
    private readonly Action<Action>? _post;

    private AtemState _state;
    private AtemLink _link;
    private ExposureReadout _exposure = ExposureReadout.Unknown;
    private int _exposureInput = -1;
    private int _exposureGeneration;
    private int _appliedVersion;
    private bool _disposed;

    /// <param name="post">
    /// Runs setup-change work on the UI thread. The store watches config.json from a timer
    /// thread; without this, a hand edit would swap camera links and reopen the switcher off
    /// the thread that owns them. Tests pass null and change the setup inline.
    /// </param>
    public Booth(ConfigStore store, ISwitcher switcher, Action<Action>? post = null)
    {
        _store = store;
        _switcher = switcher;
        _post = post;
        _state = switcher.State;
        _link = switcher.Link;
        Cameras = new CameraLinks(() => Config);
        _appliedVersion = store.Version;

        _switcher.LinkChanged += OnLinkChanged;
        _switcher.StateChanged += OnStateChanged;
        _store.Changed += OnConfigChanged;
    }

    public CueConfig Config => _store.Config;

    /// <summary>Why the setup file could not be read or written, if it could not.</summary>
    public string? ConfigError => _store.FileError;

    /// <summary>True in a demo or self-test run, which changes the screen but never the file.</summary>
    public bool ReadOnly => _store.ReadOnly;

    /// <summary>Where the setup file lives, for the Engineer screen.</summary>
    public string ConfigPath => _store.Path;

    /// <summary>The one link per input, opened when a camera command needs it.</summary>
    public CameraLinks Cameras { get; }

    /// <summary>The M/E the operator runs, as the switcher numbers it on the wire.</summary>
    private int Me => SwitcherAddress.MeIndex(Config.Me);

    private AtemState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Raised with the snapshot the screen should render after any switcher change.</summary>
    public event Action<BoothSnapshot>? Rendered;

    /// <summary>Raised after the setup changed, so the window can rebuild what is built from it.</summary>
    public event Action? ConfigChanged;

    /// <summary>The source row: configured sources, or the cameras when none are named.</summary>
    public IReadOnlyList<VolunteerSource> Sources =>
        Config.Sources.Count > 0
            ? Config.Sources
            : Config.Cameras
                .Where(camera => camera.AtemInput is not null)
                .Select(camera => new VolunteerSource { Input = camera.AtemInput!.Value, Label = camera.Name })
                .ToList();

    /// <summary>The NDI feed mapped to an input, if one is configured.</summary>
    public string? PictureFor(int input) =>
        Config.Pictures.TryGetValue(input.ToString(), out var picture) ? picture : null;

    /// <summary>The NDI feed for an input, from the picture map or its source row entry.</summary>
    public string? NdiFor(int input)
    {
        var picture = PictureFor(input);
        return string.IsNullOrWhiteSpace(picture)
            ? Sources.FirstOrDefault(source => source.Input == input)?.Ndi
            : picture;
    }

    /// <summary>
    /// Loads the setup, points the switcher at the configured host (or the
    /// <c>CUEVIDEO_ATEM</c> override) and starts watching the file for hand edits. The
    /// window calls this once when it opens.
    /// </summary>
    public void Start()
    {
        _store.Load();
        ApplyConfig(announce: false);
    }

    // ---- Engineer intents ----

    /// <summary>Maps an NDI feed to an input, or clears it when the feed is blank.</summary>
    public void SetPicture(int input, string? ndi) => _store.Update(config =>
    {
        var key = input.ToString();
        if (string.IsNullOrWhiteSpace(ndi)) config.Pictures.Remove(key);
        else config.Pictures[key] = ndi;
    });

    public void SetCameraName(Guid id, string name) => EditCamera(id, camera => camera.Name = name);

    public void SetCameraHost(Guid id, string host) => EditCamera(id, camera => camera.Host = host);

    public void SetCameraPort(Guid id, int? port) => EditCamera(id, camera => camera.Port = port);

    public void SetCameraInput(Guid id, int? input) => EditCamera(id, camera => camera.AtemInput = input);

    public void SetCameraFirstPreset(Guid id, int firstPreset) => EditCamera(id, camera => camera.FirstPreset = firstPreset);

    public void SetSwitcherHost(string host) => _store.Update(config => config.AtemHost = host);

    private void EditCamera(Guid id, Action<PtzCameraConfig> change) => _store.Update(config =>
    {
        if (config.Cameras.FirstOrDefault(camera => camera.Id == id) is { } camera) change(camera);
    });

    /// <summary>The one place the Operator screen's switcher state is built.</summary>
    public BoothSnapshot Render()
    {
        lock (_gate) return RenderLocked();
    }

    // ---- Operator commands ----

    public void SetPreview(int input) => _switcher.SetPreview(input, Me);
    public void Cut() => _switcher.Cut(Me);
    public void Auto() => _switcher.Auto(Me);
    public void FadeToBlack() => _switcher.FadeToBlack(Me);

    public void Toggle(KeyButtonConfig button)
    {
        if (button.Toggle(State) is not { } toggle) return;

        if (toggle.Target == KeyTarget.Downstream)
        {
            _switcher.SetDskOnAir(toggle.KeyIndex, toggle.On);
        }
        else
        {
            _switcher.SetKeyOnAir(toggle.KeyIndex, toggle.On, toggle.Me);
        }
    }

    // ---- Camera ----

    /// <summary>
    /// The Booth-owned link for the camera on Next, or null when that camera cannot be
    /// driven. Commands act on the one connection every camera control shares.
    /// </summary>
    public ViscaIpLink? LinkOnNext()
    {
        int input;
        lock (_gate) input = PreviewLocked();
        return Cameras.LinkFor(input);
    }

    /// <summary>Reads the camera on Next and shares the formatted rows through <see cref="Render"/>.</summary>
    public async Task RefreshExposureAsync()
    {
        int input, generation;

        lock (_gate)
        {
            input = PreviewLocked();
            generation = ++_exposureGeneration;
        }

        var link = Cameras.LinkFor(input);
        if (link is null)
        {
            ShowExposure(input, ExposureReadout.Unknown, generation);
            return;
        }

        var exposure = await link.ReadExposureAsync(TimeSpan.FromMilliseconds(1500));

        // A camera that went quiet keeps its last rows rather than blinking back to dashes.
        if (exposure is null) return;

        ShowExposure(input, ExposureReadout.From(exposure), generation);
    }

    /// <summary>Puts the camera on Next into auto exposure, then reads it back.</summary>
    public async Task AutoExposureAsync()
    {
        if (LinkOnNext() is not { } link) return;

        await link.AutoExposureAsync();
        await RefreshExposureAsync();
    }

    /// <summary>
    /// Recalls one button of a camera's shot row: a saved place shot moves the camera to its
    /// counts, a saved exposure shot sets its exposure, and a preset button recalls the preset
    /// stored on the camera.
    /// </summary>
    public async Task RecallShotAsync(int input, ShotButton button)
    {
        if (Cameras.LinkFor(input) is not { } link) return;

        if (button.Preset is { } preset)
        {
            link.Recall(preset);
            return;
        }

        if (button.Shot is not { } shot) return;

        if (shot.IsExposure)
        {
            if (shot.ExposureValues is { } exposure)
            {
                await link.SetExposureAsync(exposure);

                int preview;
                lock (_gate) preview = PreviewLocked();
                if (preview == input) await RefreshExposureAsync();
            }

            return;
        }

        if (shot.Position is { } position) link.GoTo(position);
    }

    /// <summary>
    /// Forgets the camera links and the exposure rows after a setup change. The caller
    /// renders again once it has rebuilt anything that depends on the config.
    /// </summary>
    private void ReloadCameras()
    {
        if (_disposed) return;

        Cameras.Close();

        lock (_gate)
        {
            _exposure = ExposureReadout.Unknown;
            _exposureInput = -1;
            _exposureGeneration++;
        }
    }

    /// <summary>
    /// Applies whatever the setup now says: camera links are dropped (an address or protocol
    /// may have changed) and the switcher follows the configured host.
    /// </summary>
    private void ApplyConfig(bool announce)
    {
        ReloadCameras();
        ConnectConfiguredHost();
        if (announce) ConfigChanged?.Invoke();
    }

    private void ConnectConfiguredHost()
    {
        var host = Environment.GetEnvironmentVariable("CUEVIDEO_ATEM");
        if (string.IsNullOrWhiteSpace(host)) host = Config.AtemHost;
        host = host.Trim();

        if (host.Length > 0 && !string.Equals(host, _switcher.Host, StringComparison.OrdinalIgnoreCase))
        {
            _switcher.Connect(host);
        }
    }

    /// <summary>
    /// The one path for setup changes, whether the Engineer screen changed something or the
    /// file was edited by hand. The store raises this from its poll thread, so the work moves
    /// to the UI thread before it drops camera links or reopens the switcher.
    /// </summary>
    private void OnConfigChanged()
    {
        if (_post is null) HandleConfigChanged();
        else _post(HandleConfigChanged);
    }

    private void HandleConfigChanged()
    {
        if (_disposed) return;

        if (_store.Version != _appliedVersion)
        {
            _appliedVersion = _store.Version;
            ApplyConfig(announce: true);
        }

        Rendered?.Invoke(Render());
    }

    private int PreviewLocked() => _state.Preview.GetValueOrDefault(Me);

    private void ShowExposure(int input, ExposureReadout readout, int generation)
    {
        lock (_gate)
        {
            // A newer camera took over while this read was in flight.
            if (_disposed || generation != _exposureGeneration) return;

            _exposureInput = input;
            _exposure = readout;
        }

        Rendered?.Invoke(Render());
    }

    private void OnLinkChanged(AtemLink link)
    {
        lock (_gate) _link = link;
        Rendered?.Invoke(Render());
    }

    private void OnStateChanged(AtemState state)
    {
        lock (_gate) _state = state;
        Rendered?.Invoke(Render());
    }

    private BoothSnapshot RenderLocked()
    {
        var me = Me;
        var program = _state.Program.GetValueOrDefault(me);
        var preview = _state.Preview.GetValueOrDefault(me);
        var frames = _state.MixRate.GetValueOrDefault(me);

        return new BoothSnapshot(
            ProgramInput: program,
            PreviewInput: preview,
            ProgramLabel: _state.Name(program),
            PreviewLabel: _state.Name(preview),
            Tally: BuildTallyLocked(),
            Status: BoothStatus.From(_link, _switcher.Host, _store.FileError),
            InTransition: _state.InTransition.GetValueOrDefault(me),
            FadeSeconds: frames > 0 ? frames / 30.0 : 1.0,
            Black: _state.FtbBlack.GetValueOrDefault(me),
            CameraOnNext: Config.CameraForInput(preview),
            Exposure: _exposureInput == preview ? _exposure : ExposureReadout.Unknown);
    }

    private IReadOnlyDictionary<int, SourceTally> BuildTallyLocked()
    {
        var inputs = new HashSet<int>(_state.Tally.Keys);
        inputs.UnionWith(_state.Program.Values);
        inputs.UnionWith(_state.Preview.Values);
        foreach (var source in Sources) inputs.Add(source.Input);

        var tally = new Dictionary<int, SourceTally>(inputs.Count);
        foreach (var input in inputs)
        {
            var bits = _state.Tally.TryGetValue(input, out var bit)
                ? bit
                : (byte)((_state.Program.Values.Contains(input) ? 1 : 0) | (_state.Preview.Values.Contains(input) ? 2 : 0));

            tally[input] = new SourceTally(Live: (bits & 1) != 0, Next: (bits & 2) != 0);
        }

        return tally;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _switcher.LinkChanged -= OnLinkChanged;
        _switcher.StateChanged -= OnStateChanged;
        _store.Changed -= OnConfigChanged;
        _store.Dispose();
        Cameras.Dispose();
        _switcher.Dispose();
    }
}
