using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CueVideo.Atem;
using CueVideo.Core;
using CueVideo.Ndi;
using CueVideo.Visca;

namespace CueVideo.App;

public partial class MainWindow : Window
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#6FCF7A"));
    private static readonly IBrush ConnectingBrush = new SolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#E0453A"));

    private static readonly IBrush SourceIdle = new SolidColorBrush(Color.Parse("#1A1C20"));
    private static readonly IBrush TallyProgram = new SolidColorBrush(Color.Parse("#E0453A"));
    private static readonly IBrush TallyPreview = new SolidColorBrush(Color.Parse("#6FCF7A"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#F4F5F7"));
    private static readonly IBrush InkDim = new SolidColorBrush(Color.Parse("#9AA0AA"));

    private readonly ConfigStore _config = new();
    private readonly Dictionary<int, Button> _sourceButtons = [];
    private readonly Dictionary<int, ViscaIpLink> _links = [];

    private AtemClient? _atem;
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
        WireTransport();
        WireCameraPanel();

        _atem = new AtemClient();
        _atem.LinkChanged += link => Dispatcher.UIThread.Post(() => ShowLink(link));
        _atem.StateChanged += state => Dispatcher.UIThread.Post(() => ShowState(state));
        _atem.Connect(host);

        AttachLive();

        StatusText.Text = _config.FileError ?? $"Connecting to {host}…";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
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
                Margin = new Thickness(0, 0, 7, 0),
                Foreground = Ink,
                Background = SourceIdle,
                BorderBrush = new SolidColorBrush(Color.Parse("#23252A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 10),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            var input = source.Input;
            button.Click += (_, _) => _atem?.SetPreview(input);

            _sourceButtons[input] = button;
            SourceRow.Children.Add(button);
        }
    }

    private void WireTransport()
    {
        var me = _config.Config.Me - 1;

        CutButton.Click += (_, _) => _atem?.Cut(me);
        AutoButton.Click += (_, _) => _atem?.Auto(me);
        BlackButton.Click += (_, _) => _atem?.FadeToBlack(me);
    }

    // ---- Camera ----

    private void WireCameraPanel()
    {
        Hold(PanUpLeft, () => Link()?.Drive(-1, 1), () => Link()?.StopMove());
        Hold(PanUp, () => Link()?.Drive(0, 1), () => Link()?.StopMove());
        Hold(PanUpRight, () => Link()?.Drive(1, 1), () => Link()?.StopMove());
        Hold(PanLeft, () => Link()?.Drive(-1, 0), () => Link()?.StopMove());
        Hold(PanRight, () => Link()?.Drive(1, 0), () => Link()?.StopMove());
        Hold(PanDownLeft, () => Link()?.Drive(-1, -1), () => Link()?.StopMove());
        Hold(PanDown, () => Link()?.Drive(0, -1), () => Link()?.StopMove());
        Hold(PanDownRight, () => Link()?.Drive(1, -1), () => Link()?.StopMove());

        ZoomInButton.Click += (_, _) => Link()?.Zoom(0.6);
        ZoomOutButton.Click += (_, _) => Link()?.Zoom(-0.6);
        HomeButton.Click += (_, _) => Link()?.Home();
        MoreButton.Click += (_, _) => OpenMore();
    }

    private static void Hold(Button button, Action start, Action stop)
    {
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => start(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => stop(), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => stop();
    }

    private ViscaIpLink? Link() => LinkFor(_nextInput);

    private ViscaIpLink? LinkFor(int input)
    {
        var camera = _config.Config.CameraForInput(input);
        if (camera is null || string.IsNullOrWhiteSpace(camera.Host)) return null;

        if (_links.TryGetValue(input, out var existing)) return existing;

        var link = new ViscaIpLink(camera.Host, camera.EffectivePort, camera.FirstPreset);
        link.Start();
        _links[input] = link;
        return link;
    }

    private void BuildShots()
    {
        ShotPanel.Children.Clear();

        var camera = _config.Config.CameraForInput(_nextInput);
        if (camera is null) return;

        foreach (var preset in new[] { 8, 9 })
        {
            var label = preset == 8
                ? (camera.PresetName(8) == "8" ? "Full Stage" : camera.PresetName(8))
                : (camera.PresetName(9) == "9" ? "Sermon" : camera.PresetName(9));

            var button = new Button
            {
                Content = label,
                Height = 46,
                FontSize = 12,
                Classes = { "shot" },
            };

            var slot = preset;
            button.Click += (_, _) => Link()?.Recall(slot);
            ShotPanel.Children.Add(button);
        }
    }

    private async Task ShowExposureAsync(TextBlock gain, TextBlock shutter, TextBlock iris, TextBlock white)
    {
        var link = Link();
        if (link is null)
        {
            gain.Text = shutter.Text = iris.Text = white.Text = "—";
            return;
        }

        var exposure = await link.ReadExposureAsync(TimeSpan.FromMilliseconds(1500));
        if (exposure is null) return;

        gain.Text = exposure.Gain is { } value ? $"{value} dB" : "Auto";
        shutter.Text = exposure.Shutter is { } s ? $"1/{s:0}" : "Auto";
        iris.Text = exposure.Iris is { } i ? $"f/{i / 100.0:0.#}" : "Auto";
        white.Text = exposure.WhiteBalance switch
        {
            "auto" => "Auto",
            "kelvin" => exposure.Kelvin is { } kelvin ? $"{kelvin} K" : "Kelvin",
            null => "—",
            var mode => mode,
        };
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

    // ---- State ----

    private void ShowLink(AtemLink link)
    {
        (StatusDot.Fill, StatusText.Text) = link.State switch
        {
            AtemLinkState.Online => (OnlineBrush, "Ready"),
            AtemLinkState.Connecting => (ConnectingBrush, "Connecting…"),
            AtemLinkState.Offline => (OfflineBrush, "Switcher offline"),
            _ => (ConnectingBrush, "Starting…"),
        };
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

        if (changed)
        {
            var camera = _config.Config.CameraForInput(_nextInput);
            CameraNextName.Text = camera?.Name ?? state.Name(_nextInput);

            BuildShots();
            UpdateNextTile();
        }
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

    // ---- More ----

    private void OpenMore()
    {
        var dialog = new Window
        {
            Title = "More controls",
            Width = 460,
            Height = 600,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#0A0A0C")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(22) };
        panel.Children.Add(Heading("EXPOSURE"));

        var exposure = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            RowSpacing = 4,
        };

        var gain = ValueText();
        var shutter = ValueText();
        var iris = ValueText();
        var white = ValueText();

        AddRow(exposure, 0, "Gain", gain);
        AddRow(exposure, 1, "Shutter", shutter);
        AddRow(exposure, 2, "Iris", iris);
        AddRow(exposure, 3, "White", white);
        panel.Children.Add(exposure);

        var autoExposure = Ghost("Auto exposure");
        autoExposure.Click += async (_, _) =>
        {
            if (Link() is { } link)
            {
                await link.AutoExposureAsync();
                await ShowExposureAsync(gain, shutter, iris, white);
            }
        };
        panel.Children.Add(autoExposure);

        panel.Children.Add(Heading("FOCUS"));
        var focusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var near = Ghost("Near");
        var auto = Ghost("Auto");
        var far = Ghost("Far");
        Hold(near, () => Link()?.FocusNear(), () => Link()?.StopFocus());
        Hold(far, () => Link()?.FocusFar(), () => Link()?.StopFocus());
        auto.Click += (_, _) => Link()?.AutoFocus(true);
        focusRow.Children.Add(near);
        focusRow.Children.Add(auto);
        focusRow.Children.Add(far);
        panel.Children.Add(focusRow);

        panel.Children.Add(Heading("KEYS"));
        var keys = new UniformGrid { Columns = 2 };
        foreach (var key in _config.Config.Keys)
        {
            var button = new Button
            {
                Content = key.Label,
                Height = 42,
                Margin = new Thickness(0, 0, 6, 6),
                Classes = { "ghost" },
                FontSize = 11,
            };

            var configKey = key;
            button.Click += (_, _) => ToggleKey(configKey);
            keys.Children.Add(button);
        }
        panel.Children.Add(keys);

        panel.Children.Add(Heading("PRESETS 1-9"));
        var presets = new UniformGrid { Columns = 3 };
        for (var number = 1; number <= 9; number++)
        {
            var button = new Button
            {
                Content = number.ToString(),
                Height = 40,
                Margin = new Thickness(0, 0, 6, 6),
                Classes = { "ghost" },
                FontSize = 11,
            };

            var preset = number;
            button.Click += (_, _) => Link()?.Recall(preset);
            presets.Children.Add(button);
        }
        panel.Children.Add(presets);

        dialog.Content = new ScrollViewer { Content = panel };
        dialog.ShowDialog(this);

        _ = ShowExposureAsync(gain, shutter, iris, white);
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

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 10,
        LetterSpacing = 1.6,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#767C87")),
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static TextBlock ValueText() => new()
    {
        Text = "—",
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#9AA0AA")),
    };

    private static void AddRow(Grid grid, int row, string label, TextBlock value)
    {
        var name = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#767C87")),
        };

        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(name);
        grid.Children.Add(value);
    }

    private static Button Ghost(string text) => new()
    {
        Content = text,
        Height = 36,
        FontSize = 11,
        Classes = { "ghost" },
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
}
