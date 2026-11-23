using CueVideo.Atem;

namespace CueVideo.Core;

/// <summary>
/// What the whole app shares, ported from the Mac's Booth: the setup file and the
/// switcher's last state. The window renders <see cref="Render"/> and sends Operator
/// commands back through here; it never subscribes to the switcher itself.
/// </summary>
public sealed class Booth : IDisposable
{
    private readonly object _gate = new();
    private readonly ISwitcher _switcher;

    private AtemState _state;
    private AtemLink _link;
    private bool _disposed;

    public Booth(ConfigStore store, ISwitcher switcher)
    {
        Store = store;
        _switcher = switcher;
        _state = switcher.State;
        _link = switcher.Link;

        _switcher.LinkChanged += OnLinkChanged;
        _switcher.StateChanged += OnStateChanged;
    }

    public ConfigStore Store { get; }
    public CueConfig Config => Store.Config;
    public string Host => _switcher.Host;

    /// <summary>The M/E the operator runs, as the switcher numbers it on the wire.</summary>
    private int Me => SwitcherAddress.MeIndex(Config.Me);

    private AtemState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Raised with the snapshot the screen should render after any switcher change.</summary>
    public event Action<BoothSnapshot>? Rendered;

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

    public void Load() => Store.Load();

    public void Connect(string host) => _switcher.Connect(host);

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
            Status: BoothStatus.From(_link, _switcher.Host, Store.FileError),
            InTransition: _state.InTransition.GetValueOrDefault(me),
            FadeSeconds: frames > 0 ? frames / 30.0 : 1.0,
            Black: _state.FtbBlack.GetValueOrDefault(me));
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
        _switcher.Dispose();
    }
}
