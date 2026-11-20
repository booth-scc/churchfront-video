using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CueVideo.Atem;
using CueVideo.Core;
using CueVideo.Ndi;
using CueVideo.Visca;

namespace CueVideo.App;

public partial class MainWindow : Window
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#2FA84F"));
    private static readonly IBrush ConnectingBrush = new SolidColorBrush(Color.Parse("#D79A2B"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#E0362C"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#8A90A0"));

    private static readonly IBrush SourceIdle = new SolidColorBrush(Color.Parse("#1C1F27"));
    private static readonly IBrush TallyProgram = new SolidColorBrush(Color.Parse("#E0362C"));
    private static readonly IBrush TallyPreview = new SolidColorBrush(Color.Parse("#2FA84F"));
    private static readonly IBrush TallyKeyOn = new SolidColorBrush(Color.Parse("#D79A2B"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#F2F4F8"));

    private readonly ConfigStore _config = new();
    private readonly Dictionary<int, Button> _sourceButtons = [];
    private readonly Dictionary<KeyButtonConfig, Button> _keyButtons = [];
    private readonly Dictionary<int, Button> _presetButtons = [];
    private readonly Dictionary<int, ViscaIpLink> _links = [];

    private AtemClient? _atem;
    private NdiFinder? _finder;
    private CancellationTokenSource? _ndiPoll;
    private NdiView? _liveView;
    private NdiView? _nextView;
    private string? _nextNdiName;
    private int _nextInput = 1;
    private bool _initialised;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _config.Load();

        var host = Environment.GetEnvironmentVariable("CUEVIDEO_ATEM");
        if (string.IsNullOrWhiteSpace(host)) host = _config.Config.AtemHost;

        BuildSourceRow();
        BuildKeyRow();
        WireTransport();
        WireCameraPanel();

        _atem = new AtemClient();
        _atem.LinkChanged += link => Dispatcher.UIThread.Post(() => ShowLink(link));
        _atem.StateChanged += state => Dispatcher.UIThread.Post(() => ShowState(state));
        _atem.Connect(host);

        AttachLive();
        StartNdiPolling();

        FooterStatus.Text = _config.FileError ?? $"Connecting to ATEM at {host}…";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ndiPoll?.Cancel();
        _ndiPoll?.Dispose();
        _ndiPoll = null;

        _finder?.Dispose();
        _finder = null;

        _liveView?.Dispose();
        _nextView?.Dispose();
        _liveView = null;
        _nextView = null;

        foreach (var link in _links.Values) link.Dispose();
        _links.Clear();

        _atem?.Dispose();
        _atem = null;
    }

    // ---- Switcher ----

    private void BuildSourceRow()
    {
        SourceRow.Children.Clear();
        _sourceButtons.Clear();

        var sources = _config.Config.Sources.Count > 0
            ? _config.Config.Sources
            : _config.Config.Cameras
                .Where(c => c.AtemInput is not null)
                .Select(c => new VolunteerSource { Input = c.AtemInput!.Value, Label = c.Name })
                .ToList();

        foreach (var source in sources)
        {
            var button = new Button
            {
                Content = string.IsNullOrWhiteSpace(source.Label) ? source.Input.ToString() : source.Label,
                Tag = source.Input,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = Ink,
                Background = SourceIdle,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 14),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };

            var input = source.Input;
            button.Click += (_, _) => _atem?.SetPreview(input);

            _sourceButtons[input] = button;
            SourceRow.Children.Add(button);
        }
    }

    private void BuildKeyRow()
    {
        KeyRow.Children.Clear();
        _keyButtons.Clear();

        foreach (var key in _config.Config.Keys)
        {
            var button = new Button
            {
                Content = key.Label,
                Tag = key,
                Margin = new Thickness(6, 0, 0, 0),
                Height = 56,
                MinWidth = 110,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = Ink,
                Background = SourceIdle,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };

            button.Click += (_, _) => ToggleKey(key);

            _keyButtons[key] = button;
            KeyRow.Children.Add(button);
        }
    }

    private void ToggleKey(KeyButtonConfig key)
    {
        var state = _atem?.State;

        if (key.Dsk is { } dsk)
        {
            var on = state?.DskOnAir.GetValueOrDefault(dsk) ?? false;
            _atem?.SetDskOnAir(dsk, !on);
            return;
        }

        var me = (key.Me ?? 1) - 1;
        var keyer = key.Keyer ?? 0;
        var keyOn = state?.KeyOnAir.TryGetValue(me, out var keys) == true && keys.GetValueOrDefault(keyer);
        _atem?.SetKeyOnAir(keyer, !keyOn, me);
    }

    private void WireTransport()
    {
        var me = _config.Config.Me - 1;

        CutButton.Click += (_, _) => _atem?.Cut(me);
        AutoButton.Click += (_, _) => _atem?.Auto(me);
        BlackButton.Click += (_, _) => _atem?.FadeToBlack(me);
    }

    // ---- Cameras ----

    private void WireCameraPanel()
    {
        _presetButtons[1] = Preset1Button;
        _presetButtons[2] = Preset2Button;
        _presetButtons[3] = Preset3Button;
        _presetButtons[4] = Preset4Button;
        _presetButtons[5] = Preset5Button;
        _presetButtons[6] = Preset6Button;
        _presetButtons[7] = Preset7Button;
        _presetButtons[8] = Preset8Button;
        _presetButtons[9] = Preset9Button;

        foreach (var (number, button) in _presetButtons)
        {
            var preset = number;
            button.Click += (_, _) => Link()?.Recall(preset);
        }

        Hold(PanUpLeft, () => Link()?.Drive(-1, 1), () => Link()?.StopMove());
        Hold(PanUp, () => Link()?.Drive(0, 1), () => Link()?.StopMove());
        Hold(PanUpRight, () => Link()?.Drive(1, 1), () => Link()?.StopMove());
        Hold(PanLeft, () => Link()?.Drive(-1, 0), () => Link()?.StopMove());
        Hold(PanRight, () => Link()?.Drive(1, 0), () => Link()?.StopMove());
        Hold(PanDownLeft, () => Link()?.Drive(-1, -1), () => Link()?.StopMove());
        Hold(PanDown, () => Link()?.Drive(0, -1), () => Link()?.StopMove());
        Hold(PanDownRight, () => Link()?.Drive(1, -1), () => Link()?.StopMove());

        PanHome.Click += (_, _) => Link()?.Home();
        ZoomInButton.Click += (_, _) => Link()?.Zoom(0.6);
        ZoomOutButton.Click += (_, _) => Link()?.Zoom(-0.6);
        Hold(FocusNearButton, () => Link()?.FocusNear(), () => Link()?.StopFocus());
        Hold(FocusFarButton, () => Link()?.FocusFar(), () => Link()?.StopFocus());
        FocusAutoButton.Click += (_, _) => Link()?.AutoFocus(true);
    }

    private static void Hold(Button button, Action start, Action stop)
    {
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => start(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => stop(), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => stop();
    }

    private ViscaIpLink? Link()
    {
        var camera = _config.Config.CameraForInput(_nextInput);
        if (camera is null || string.IsNullOrWhiteSpace(camera.Host)) return null;

        if (_links.TryGetValue(_nextInput, out var existing)) return existing;

        var link = new ViscaIpLink(camera.Host, camera.EffectivePort, camera.FirstPreset);
        link.Start();
        _links[_nextInput] = link;
        return link;
    }

    private void UpdateCameraPanel()
    {
        var camera = _config.Config.CameraForInput(_nextInput);
        var state = _atem?.State;

        CameraNextName.Text = camera is null
            ? $"{state?.Name(_nextInput) ?? "—"} · no camera"
            : $"{camera.Name} · {state?.Name(_nextInput) ?? "—"}";

        foreach (var (number, button) in _presetButtons)
        {
            button.Content = camera is null
                ? number switch { 8 => "Full Stage", 9 => "Sermon", _ => number.ToString() }
                : camera.PresetName(number) is { Length: > 0 } name && name != number.ToString()
                    ? name
                    : number switch { 8 => "Full Stage", 9 => "Sermon", _ => number.ToString() };
        }
    }

    // ---- Pictures ----

    private void AttachLive()
    {
        var source = Environment.GetEnvironmentVariable("CUEVIDEO_NDI_LIVE");
        if (string.IsNullOrWhiteSpace(source)) return;

        _liveView = new NdiView(source, $"WAITING FOR PROGRAM NDI\n{source}");
        ProgramHost.Child = _liveView;
        _liveView.Start();
    }

    private void UpdateNextTile()
    {
        var source = Environment.GetEnvironmentVariable("CUEVIDEO_NDI_NEXT");

        if (string.IsNullOrWhiteSpace(source))
        {
            source = _config.Config.Pictures.TryGetValue(_nextInput.ToString(), out var picture) ? picture : null;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = _config.Config.Sources.FirstOrDefault(s => s.Input == _nextInput)?.Ndi;
        }

        if (string.Equals(source, _nextNdiName, StringComparison.OrdinalIgnoreCase)) return;

        _nextNdiName = source;
        _nextView?.Dispose();
        _nextView = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            NextHost.Child = null;
            return;
        }

        _nextView = new NdiView(source, $"WAITING FOR NEXT NDI\n{source}");
        NextHost.Child = _nextView;
        _nextView.Start();
    }

    private void StartNdiPolling()
    {
        if (!NdiRuntime.Initialize())
        {
            NdiChipText.Text = "NDI UNAVAILABLE";
            return;
        }

        try
        {
            _finder = NdiFinder.Create(showLocalSources: true);
        }
        catch (Exception ex)
        {
            NdiChipText.Text = $"NDI: {ex.Message}";
            return;
        }

        _ndiPoll = new CancellationTokenSource();
        var token = _ndiPoll.Token;

        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                var sources = _finder.GetSources(TimeSpan.FromSeconds(2));
                Dispatcher.UIThread.Post(() => ShowNdiSources(sources));
            }
        }, token);
    }

    private void ShowNdiSources(IReadOnlyList<NdiSourceInfo> sources)
    {
        NdiChipText.Text = sources.Count switch
        {
            0 => "NO NDI SOURCES",
            1 => "1 NDI SOURCE",
            _ => $"{sources.Count} NDI SOURCES",
        };

        NdiDot.Fill = sources.Count > 0 ? OnlineBrush : ConnectingBrush;
    }

    // ---- State ----

    private void ShowLink(AtemLink link)
    {
        var (text, brush) = link.State switch
        {
            AtemLinkState.Online => ("ATEM ONLINE", OnlineBrush),
            AtemLinkState.Connecting => ("ATEM CONNECTING", ConnectingBrush),
            AtemLinkState.Offline => ("ATEM OFFLINE", OfflineBrush),
            _ => ("ATEM IDLE", IdleBrush),
        };

        AtemChipText.Text = text;
        AtemDot.Fill = brush;

        if (link.Message.Length > 0) FooterStatus.Text = link.Message;
    }

    private void ShowState(AtemState state)
    {
        ProgramSource.Text = state.Name(state.Program.GetValueOrDefault(0));
        NextSource.Text = state.Name(state.Preview.GetValueOrDefault(0));

        var preview = state.Preview.GetValueOrDefault(0);
        var changed = preview != _nextInput || !_initialised;
        _initialised = true;
        _nextInput = preview;

        UpdateTally(state);

        var product = state.Product.Length > 0 ? state.Product : "ATEM";
        var version = state.Version.Length > 0 ? $" · v{state.Version}" : "";
        var transition = state.InTransition.GetValueOrDefault(0) ? " · transition running" : "";
        FooterStatus.Text = $"{product}{version} · PGM {state.Name(state.Program.GetValueOrDefault(0))} / PVW {state.Name(preview)}{transition}";

        if (changed)
        {
            UpdateCameraPanel();
            UpdateNextTile();
        }

        UpdateKeyButtons(state);
    }

    private void UpdateTally(AtemState state)
    {
        foreach (var (input, button) in _sourceButtons)
        {
            byte bits;
            if (state.Tally.TryGetValue(input, out var bit))
            {
                bits = bit;
            }
            else
            {
                bits = (byte)((state.Program.Values.Contains(input) ? 1 : 0) | (state.Preview.Values.Contains(input) ? 2 : 0));
            }

            button.Background = (bits & 1) != 0 ? TallyProgram
                : (bits & 2) != 0 ? TallyPreview
                : SourceIdle;
            button.Foreground = bits != 0 ? Brushes.White : Ink;
        }
    }

    private void UpdateKeyButtons(AtemState state)
    {
        foreach (var (config, button) in _keyButtons)
        {
            var on = config.Dsk is { } dsk
                ? state.DskOnAir.GetValueOrDefault(dsk)
                : (config.Me ?? 1) - 1 is var me &&
                  state.KeyOnAir.TryGetValue(me, out var keys) &&
                  keys.GetValueOrDefault(config.Keyer ?? 0);

            button.Background = on ? TallyKeyOn : SourceIdle;
            button.Foreground = on ? Brushes.Black : Ink;
        }
    }
}
