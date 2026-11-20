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
    private sealed record SourceTile(int Input, Border Border, Border Badge, TextBlock BadgeText);

    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#6FCF7A"));
    private static readonly IBrush ConnectingBrush = new SolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#E0453A"));

    private static readonly IBrush TileIdleBorder = Brushes.Transparent;
    private static readonly IBrush TileProgram = new SolidColorBrush(Color.Parse("#E0453A"));
    private static readonly IBrush TilePreview = new SolidColorBrush(Color.Parse("#6FCF7A"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#F4F5F7"));
    private static readonly IBrush InkDim = new SolidColorBrush(Color.Parse("#9AA0AA"));
    private static readonly IBrush CardRaised = new SolidColorBrush(Color.Parse("#1A1C20"));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#23252A"));

    private readonly ConfigStore _config = new();
    private readonly List<SourceTile> _tiles = [];
    private readonly List<NdiView> _thumbs = [];
    private readonly Dictionary<int, ViscaIpLink> _links = [];

    private AtemClient? _atem;
    private EngineerWindow? _engineer;
    private NdiView? _liveView;
    private NdiView? _nextView;
    private string? _nextNdiName;
    private string? _liveNdiName;
    private int _nextInput = 1;
    private int _programInput = 1;
    private bool _initialised;
    private double _speed = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // The interface advertises these: Space cuts, Return fades.
        var me = _config.Config.Me - 1;

        switch (e.Key)
        {
            case Key.Space:
                _atem?.Cut(me);
                e.Handled = true;
                break;

            case Key.Enter:
                _atem?.Auto(me);
                e.Handled = true;
                break;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _config.Load();

        var host = Environment.GetEnvironmentVariable("CUEVIDEO_ATEM");
        if (string.IsNullOrWhiteSpace(host)) host = _config.Config.AtemHost;

        BuildSourceTiles();
        WireTransport();
        WireCameraPanel();
        WireSpeed();

        _atem = new AtemClient();
        _atem.LinkChanged += link => Dispatcher.UIThread.Post(() => ShowLink(link));
        _atem.StateChanged += state => Dispatcher.UIThread.Post(() => ShowState(state));
        _atem.Connect(host);

        AttachLive();

        StatusText.Text = _config.FileError ?? $"Connecting to {host}…";

        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--engineer", StringComparison.OrdinalIgnoreCase)))
        {
            OpenEngineer();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _liveView?.Dispose();
        _nextView?.Dispose();
        _liveView = null;
        _nextView = null;

        foreach (var thumb in _thumbs) thumb.Dispose();
        _thumbs.Clear();

        foreach (var link in _links.Values) link.Dispose();
        _links.Clear();

        _atem?.Dispose();
        _atem = null;
    }

    // ---- Switcher ----

    private void BuildSourceTiles()
    {
        SourceTiles.Children.Clear();
        _tiles.Clear();

        var sources = _config.Config.Sources.Count > 0
            ? _config.Config.Sources
            : _config.Config.Cameras
                .Where(c => c.AtemInput is not null)
                .Select(c => new VolunteerSource { Input = c.AtemInput!.Value, Label = c.Name })
                .ToList();

        foreach (var source in sources)
        {
            var name = string.IsNullOrWhiteSpace(source.Label) ? source.Input.ToString() : source.Label;

            var host = new Border { Background = new SolidColorBrush(Color.Parse("#0D0E11")), CornerRadius = new CornerRadius(5) };

            var ndi = _config.Config.Pictures.TryGetValue(source.Input.ToString(), out var picture) ? picture : source.Ndi;
            if (!string.IsNullOrWhiteSpace(ndi))
            {
                var thumb = new NdiView(ndi, "", lowestBandwidth: true);
                _thumbs.Add(thumb);
                host.Child = thumb;
            }

            var nameChip = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#C8000000")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2),
                Margin = new Thickness(7),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = name, FontSize = 10, Foreground = Ink },
            };

            var badgeText = new TextBlock { FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
            var badge = new Border
            {
                Background = TilePreview,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                Margin = new Thickness(7),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                Child = badgeText,
            };

            var tile = new Border
            {
                Width = 142,
                Height = 82,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = TileIdleBorder,
                ClipToBounds = true,
                Child = new Grid { Children = { host, nameChip, badge } },
            };

            var input = source.Input;
            tile.PointerPressed += (_, _) => _atem?.SetPreview(input);

            _tiles.Add(new SourceTile(input, tile, badge, badgeText));
            SourceTiles.Children.Add(tile);
        }

        foreach (var thumb in _thumbs) thumb.Start();
    }

    private void WireTransport()
    {
        var me = _config.Config.Me - 1;

        CutButton.Click += (_, _) => _atem?.Cut(me);
        AutoButton.Click += (_, _) => _atem?.Auto(me);
        BlackButton.Click += (_, _) => _atem?.FadeToBlack(me);
        MoreButton.Click += (_, _) => OpenMore();
        EngineerButton.Click += (_, _) => OpenEngineer();
    }

    private void OpenEngineer()
    {
        if (_engineer is { IsVisible: true })
        {
            _engineer.Activate();
            return;
        }

        _engineer = new EngineerWindow(_config, ReloadConfig);
        _engineer.Closed += (_, _) => _engineer = null;
        _engineer.Show(this);
    }

    private void ReloadConfig()
    {
        foreach (var thumb in _thumbs) thumb.Dispose();
        _thumbs.Clear();

        BuildSourceTiles();

        var host = _config.Config.AtemHost;
        if (_atem is not null && host.Length > 0 &&
            !string.Equals(_atem.Host, host, StringComparison.OrdinalIgnoreCase))
        {
            _atem.Connect(host);
        }

        _liveNdiName = null;
        _nextNdiName = null;
        UpdateLiveTile(_programInput);
        UpdateNextTile();
        BuildShots();
        _ = ShowExposureAsync();
    }

    private void WireSpeed()
    {
        SpeedSlider.ValueChanged += (_, args) =>
        {
            _speed = args.NewValue;
            SpeedText.Text = $"{_speed:0.0}×";
        };

        ResetSpeedButton.Click += (_, _) =>
        {
            SpeedSlider.Value = 1.0;
            _speed = 1.0;
            SpeedText.Text = "1.0×";
        };
    }

    // ---- Camera ----

    private void WireCameraPanel()
    {
        Hold(PanUpLeft, () => Drive(-1, 1), () => Link()?.StopMove());
        Hold(PanUp, () => Drive(0, 1), () => Link()?.StopMove());
        Hold(PanUpRight, () => Drive(1, 1), () => Link()?.StopMove());
        Hold(PanLeft, () => Drive(-1, 0), () => Link()?.StopMove());
        Hold(PanRight, () => Drive(1, 0), () => Link()?.StopMove());
        Hold(PanDownLeft, () => Drive(-1, -1), () => Link()?.StopMove());
        Hold(PanDown, () => Drive(0, -1), () => Link()?.StopMove());
        Hold(PanDownRight, () => Drive(1, -1), () => Link()?.StopMove());

        ZoomInButton.Click += (_, _) => Link()?.Zoom(Math.Clamp(0.6 * _speed, 0, 1));
        ZoomOutButton.Click += (_, _) => Link()?.Zoom(-Math.Clamp(0.6 * _speed, 0, 1));
        FocusButton.Click += (_, _) => Link()?.AutoFocus(true);

        ExposureAutoButton.Click += async (_, _) =>
        {
            if (Link() is { } link)
            {
                await link.AutoExposureAsync();
                await ShowExposureAsync();
            }
        };
    }

    private void Drive(double pan, double tilt)
    {
        Link()?.Drive(Math.Clamp(pan * _speed, -1, 1), Math.Clamp(tilt * _speed, -1, 1));
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
        ShotRows.Children.Clear();

        foreach (var camera in _config.Config.Cameras)
        {
            if (camera.AtemInput is not { } input) continue;

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("62,*,*"),
                ColumnSpacing = 5,
            };

            var isNext = input == _nextInput;

            var cameraButton = new Button
            {
                Content = camera.Name,
                Height = 30,
                FontSize = 9.5,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(6, 2),
                Background = isNext ? new SolidColorBrush(Color.Parse("#13301A")) : CardRaised,
                Foreground = isNext ? TilePreview : InkDim,
                BorderBrush = isNext ? TilePreview : LineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            cameraButton.Click += (_, _) => _atem?.SetPreview(input);

            row.Children.Add(cameraButton);

            var column = 1;
            foreach (var preset in new[] { 8, 9 })
            {
                var label = camera.PresetName(preset);
                if (label == preset.ToString()) label = preset == 8 ? "Full Stage" : "Sermon";

                var shot = new Button
                {
                    Content = label,
                    Height = 30,
                    FontSize = 9.5,
                    Padding = new Thickness(6, 2),
                    Classes = { "ghost" },
                };

                var slot = preset;
                var cameraInput = input;
                shot.Click += (_, _) => LinkFor(cameraInput)?.Recall(slot);

                Grid.SetColumn(shot, column++);
                row.Children.Add(shot);
            }

            ShotRows.Children.Add(row);
        }
    }

    private async Task ShowExposureAsync()
    {
        var camera = _config.Config.CameraForInput(_nextInput);
        ExposureCamText.Text = camera is null ? "No camera on Next" : $"{camera.Name} exposure";

        var link = Link();
        if (link is null)
        {
            ExposureGain.Text = ExposureShutter.Text = ExposureIris.Text = ExposureWhite.Text = "—";
            return;
        }

        var exposure = await link.ReadExposureAsync(TimeSpan.FromMilliseconds(1500));
        if (exposure is null) return;

        ExposureGain.Text = exposure.Gain is { } gain ? $"{gain} dB" : "Auto";
        ExposureShutter.Text = exposure.Shutter is { } shutter ? $"1/{shutter:0}" : "Auto";
        ExposureIris.Text = exposure.Iris is { } iris ? $"f/{iris / 100.0:0.#}" : "Auto";
        ExposureWhite.Text = exposure.WhiteBalance switch
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
        UpdateLiveTile(_programInput);
    }

    private void UpdateLiveTile(int program)
    {
        var source = Environment.GetEnvironmentVariable("CUEVIDEO_NDI_LIVE");

        if (string.IsNullOrWhiteSpace(source))
        {
            source = _config.Config.Pictures.TryGetValue(program.ToString(), out var picture) ? picture : null;
        }

        if (string.Equals(source, _liveNdiName, StringComparison.OrdinalIgnoreCase)) return;

        _liveNdiName = source;
        _liveView?.Dispose();
        _liveView = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            ProgramHost.Child = null;
            return;
        }

        _liveView = new NdiView(source, $"WAITING FOR LIVE NDI\n{source}");
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

        var program = state.Program.GetValueOrDefault(0);
        var preview = state.Preview.GetValueOrDefault(0);

        var changed = preview != _nextInput || !_initialised;
        var programChanged = program != _programInput || !_initialised;

        _initialised = true;
        _nextInput = preview;
        _programInput = program;

        UpdateTally(state);
        UpdateSwitcherLabels(state);

        if (programChanged) UpdateLiveTile(program);

        if (!changed) return;

        var camera = _config.Config.CameraForInput(_nextInput);
        CameraChipName.Text = camera?.Name ?? state.Name(_nextInput);

        BuildShots();
        UpdateNextTile();
        _ = ShowExposureAsync();
    }

    private void UpdateTally(AtemState state)
    {
        foreach (var tile in _tiles)
        {
            byte bits;
            if (state.Tally.TryGetValue(tile.Input, out var bit))
            {
                bits = bit;
            }
            else
            {
                bits = (byte)((state.Program.Values.Contains(tile.Input) ? 1 : 0) | (state.Preview.Values.Contains(tile.Input) ? 2 : 0));
            }

            var isProgram = (bits & 1) != 0;
            var isPreview = (bits & 2) != 0;

            tile.Border.BorderBrush = isProgram ? TileProgram : isPreview ? TilePreview : TileIdleBorder;
            tile.Badge.IsVisible = isProgram || isPreview;
            tile.Badge.Background = isProgram ? TileProgram : TilePreview;
            tile.BadgeText.Text = isProgram ? "LIVE" : "NEXT";
        }
    }

    private void UpdateSwitcherLabels(AtemState state)
    {
        var inTransition = state.InTransition.GetValueOrDefault(0);
        var frames = state.MixRate.GetValueOrDefault(0);
        var seconds = frames > 0 ? frames / 30.0 : 1.0;

        FadeSub.Text = $"{seconds:0.0} s · Return";
        BlackSub.Text = state.FtbBlack.GetValueOrDefault(0) ? "On" : "Off";
        AutoButton.Opacity = inTransition ? 0.75 : 1.0;
    }

    // ---- More ----

    private void OpenMore()
    {
        var dialog = new Window
        {
            Title = "More controls",
            Width = 440,
            Height = 560,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#0A0A0C")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(22) };
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
                Height = 40,
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
                Height = 38,
                Margin = new Thickness(0, 0, 6, 6),
                Classes = { "ghost" },
                FontSize = 11,
            };

            var preset = number;
            button.Click += (_, _) => Link()?.Recall(preset);
            presets.Children.Add(button);
        }
        panel.Children.Add(presets);

        panel.Children.Add(Heading("SAVE ON CAMERA · 20-39"));
        var saveStatus = new TextBlock
        {
            Text = "These write the camera's own preset memory, clear of the Stream Deck's 1-9.",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.Parse("#767C87")),
            TextWrapping = TextWrapping.Wrap,
        };

        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var recallRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var number in new[] { 20, 21, 22, 23 })
        {
            var save = Ghost($"Save {number}");
            save.Width = 72;
            var slot = number;
            save.Click += (_, _) =>
            {
                Link()?.Store(slot);
                saveStatus.Text = $"Saved the current view to camera preset {slot}.";
            };

            var recall = Ghost($"Go {number}");
            recall.Width = 68;
            recall.Click += (_, _) =>
            {
                Link()?.Recall(slot);
                saveStatus.Text = $"Recalled camera preset {slot}.";
            };

            saveRow.Children.Add(save);
            recallRow.Children.Add(recall);
        }

        panel.Children.Add(saveRow);
        panel.Children.Add(recallRow);
        panel.Children.Add(saveStatus);

        panel.Children.Add(Heading("EXPOSURE"));
        var exposureAuto = Ghost("Auto exposure");
        exposureAuto.Click += async (_, _) =>
        {
            if (Link() is { } link)
            {
                await link.AutoExposureAsync();
                await ShowExposureAsync();
            }
        };
        panel.Children.Add(exposureAuto);

        dialog.Content = new ScrollViewer { Content = panel };
        dialog.ShowDialog(this);
    }    private void ToggleKey(KeyButtonConfig key)
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

    private static Button Ghost(string text) => new()
    {
        Content = text,
        Height = 36,
        FontSize = 11,
        Classes = { "ghost" },
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
}
