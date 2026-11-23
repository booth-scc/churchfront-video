using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    private readonly Booth _booth = new(new ConfigStore(), new AtemClient());
    private readonly List<SourceTile> _tiles = [];
    private readonly List<NdiView> _thumbs = [];
    private readonly Dictionary<int, ViscaIpLink> _links = [];

    private EngineerView? _engineerView;
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

        _booth.Rendered += state => Dispatcher.UIThread.Post(() => ShowBooth(state));

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Space and Return belong to the Operator screen. They must never cut or fade
        // while Engineer is showing, a dialog is open, or someone is typing in a field.
        var key = e.Key switch
        {
            Key.Space => OperatorKey.Space,
            Key.Enter => OperatorKey.Return,
            _ => OperatorKey.Other,
        };

        if (key == OperatorKey.Other) return;

        var focused = FocusManager?.GetFocusedElement();
        var textFieldFocused = focused is Visual visual &&
            visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null;

        var command = OperatorKeyboard.Resolve(
            key,
            OperatorView.IsVisible ? MainScreen.Operator : MainScreen.Engineer,
            dialogOpen: OwnedWindows.Count > 0,
            textFieldFocused: textFieldFocused);

        switch (command)
        {
            case OperatorCommand.Cut:
                _booth.Cut();
                e.Handled = true;
                break;

            case OperatorCommand.Fade:
                _booth.Auto();
                e.Handled = true;
                break;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _booth.Load();

        var host = Environment.GetEnvironmentVariable("CUEVIDEO_ATEM");
        if (string.IsNullOrWhiteSpace(host)) host = _booth.Config.AtemHost;

        BuildSourceTiles();
        WireTransport();
        WireCameraPanel();
        WireSpeed();
        UpdateCameraProtocolNotice();

        _booth.Connect(host ?? "");

        AttachLive();
        ShowBooth(_booth.Render());

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

        _engineerView?.Dispose();
        _engineerView = null;

        _booth.Dispose();
    }

    // ---- Switcher ----

    private void BuildSourceTiles()
    {
        SourceTiles.Children.Clear();
        _tiles.Clear();

        var sources = _booth.Sources;

        foreach (var source in sources)
        {
            var name = string.IsNullOrWhiteSpace(source.Label) ? source.Input.ToString() : source.Label;

            var host = new Border { Background = new SolidColorBrush(Color.Parse("#0D0E11")), CornerRadius = new CornerRadius(5) };

            var ndi = _booth.PictureFor(source.Input) ?? source.Ndi;
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
            tile.PointerPressed += (_, _) => _booth.SetPreview(input);

            _tiles.Add(new SourceTile(input, tile, badge, badgeText));
            SourceTiles.Children.Add(tile);
        }

        foreach (var thumb in _thumbs) thumb.Start();
    }

    private void WireTransport()
    {
        CutButton.Click += (_, _) => _booth.Cut();
        AutoButton.Click += (_, _) => _booth.Auto();
        BlackButton.Click += (_, _) => _booth.FadeToBlack();
        MoreButton.Click += (_, _) => OpenMore();
        EngineerButton.Click += (_, _) => OpenEngineer();
        OperatorButton.Click += (_, _) => ShowOperator();
    }

    private void OpenEngineer()
    {
        _engineerView ??= new EngineerView(_booth.Store, ReloadConfig);
        EngineerHost.Content = _engineerView;

        OperatorView.IsVisible = false;
        EngineerHost.IsVisible = true;

        OperatorButton.Background = Brushes.Transparent;
        OperatorButton.Foreground = InkDim;
        EngineerButton.Background = new SolidColorBrush(Color.Parse("#DDE7F2"));
        EngineerButton.Foreground = new SolidColorBrush(Color.Parse("#0A0A0C"));
    }

    private void ShowOperator()
    {
        OperatorView.IsVisible = true;
        EngineerHost.IsVisible = false;

        OperatorButton.Background = new SolidColorBrush(Color.Parse("#DDE7F2"));
        OperatorButton.Foreground = new SolidColorBrush(Color.Parse("#0A0A0C"));
        EngineerButton.Background = Brushes.Transparent;
        EngineerButton.Foreground = InkDim;
    }

    private void ReloadConfig()
    {
        foreach (var thumb in _thumbs) thumb.Dispose();
        _thumbs.Clear();

        // A camera may have changed address or protocol; never keep driving the old one.
        foreach (var link in _links.Values) link.Dispose();
        _links.Clear();

        BuildSourceTiles();

        var host = _booth.Config.AtemHost;
        if (host.Length > 0 && !string.Equals(_booth.Host, host, StringComparison.OrdinalIgnoreCase))
        {
            _booth.Connect(host);
        }

        _liveNdiName = null;
        _nextNdiName = null;
        UpdateLiveTile(_programInput);
        UpdateNextTile();
        UpdateCameraProtocolNotice();
        BuildShots();
        _ = ShowExposureAsync();

        // The rebuilt tiles must take their tally and status from the same snapshot.
        ShowBooth(_booth.Render());
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
        var camera = _booth.Config.CameraForInput(input);
        if (camera is null || !camera.IsSupported) return null;

        if (_links.TryGetValue(input, out var existing)) return existing;

        var link = CameraLinks.Open(camera);
        if (link is not null) _links[input] = link;
        return link;
    }

    private void UpdateCameraProtocolNotice()
    {
        var camera = _booth.Config.CameraForInput(_nextInput);
        var unsupported = camera is { IsSupported: false };

        CameraProtocolNotice.IsVisible = unsupported;
        CameraProtocolNotice.Text = unsupported
            ? $"{camera!.ProtocolLabel} · unsupported by this port"
            : "";
    }

    /// <summary>Why the camera on Next cannot be driven, for buttons that would otherwise lie.</summary>
    private string CannotDriveReason()
    {
        var camera = _booth.Config.CameraForInput(_nextInput);
        if (camera is null) return "No camera on Next.";
        if (!camera.IsSupported) return $"{camera.Name} · {camera.ProtocolLabel} unsupported by this port.";
        return $"{camera.Name} has no address.";
    }

    private void BuildShots()
    {
        ShotRows.Children.Clear();

        foreach (var camera in _booth.Config.Cameras)
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

            if (!camera.IsSupported) ToolTip.SetTip(cameraButton, $"{camera.ProtocolLabel} unsupported by this port");

            cameraButton.Click += (_, _) => _booth.SetPreview(input);

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
                    IsEnabled = camera.IsSupported,
                };

                var slot = preset;
                var cameraInput = input;
                shot.Click += (_, _) => LinkFor(cameraInput)?.Recall(slot);
                if (!camera.IsSupported) ToolTip.SetTip(shot, $"{camera.ProtocolLabel} unsupported by this port");

                Grid.SetColumn(shot, column++);
                row.Children.Add(shot);
            }

            ShotRows.Children.Add(row);
        }
    }

    private async Task ShowExposureAsync()
    {
        var camera = _booth.Config.CameraForInput(_nextInput);
        ExposureCamText.Text = camera is null
            ? "No camera on Next"
            : camera.IsSupported
                ? $"{camera.Name} exposure"
                : $"{camera.Name} · {camera.ProtocolLabel} unsupported";

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
            source = _booth.PictureFor(program);
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
            source = _booth.NdiFor(_nextInput);
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

    /// <summary>
    /// The Operator screen's one render path: program, preview, tally, status and the
    /// transition labels all come from the same Booth snapshot.
    /// </summary>
    private void ShowBooth(BoothSnapshot state)
    {
        ProgramSource.Text = state.ProgramLabel;
        NextSource.Text = state.PreviewLabel;

        var previewChanged = state.PreviewInput != _nextInput || !_initialised;
        var programChanged = state.ProgramInput != _programInput || !_initialised;

        _initialised = true;
        _nextInput = state.PreviewInput;
        _programInput = state.ProgramInput;

        ShowTally(state);
        ShowSwitcherLabels(state);
        ShowStatus(state.Status);

        if (programChanged) UpdateLiveTile(state.ProgramInput);

        if (!previewChanged) return;

        var camera = _booth.Config.CameraForInput(_nextInput);
        CameraChipName.Text = camera?.Name ?? state.PreviewLabel;
        UpdateCameraProtocolNotice();

        BuildShots();
        UpdateNextTile();
        _ = ShowExposureAsync();
    }

    private void ShowTally(BoothSnapshot state)
    {
        foreach (var tile in _tiles)
        {
            var tally = state.TallyFor(tile.Input);

            tile.Border.BorderBrush = tally.Live ? TileProgram : tally.Next ? TilePreview : TileIdleBorder;
            tile.Badge.IsVisible = tally.Live || tally.Next;
            tile.Badge.Background = tally.Live ? TileProgram : TilePreview;
            tile.BadgeText.Text = tally.Live ? "LIVE" : "NEXT";
        }
    }

    private void ShowSwitcherLabels(BoothSnapshot state)
    {
        FadeSub.Text = $"{state.FadeSeconds:0.0} s · Return";
        BlackSub.Text = state.Black ? "On" : "Off";
        AutoButton.Opacity = state.InTransition ? 0.75 : 1.0;
    }

    private void ShowStatus(BoothStatus status)
    {
        (StatusDot.Fill, StatusText.Text) = status.Tone switch
        {
            BoothStatusTone.Ready => (OnlineBrush, status.Text),
            BoothStatusTone.Offline => (OfflineBrush, status.Text),
            _ => (ConnectingBrush, status.Text),
        };
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
        foreach (var key in _booth.Config.Keys)
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
                if (Link() is not { } link)
                {
                    saveStatus.Text = CannotDriveReason();
                    return;
                }

                link.Store(slot);
                saveStatus.Text = $"Saved the current view to camera preset {slot}.";
            };

            var recall = Ghost($"Go {number}");
            recall.Width = 68;
            recall.Click += (_, _) =>
            {
                if (Link() is not { } link)
                {
                    saveStatus.Text = CannotDriveReason();
                    return;
                }

                link.Recall(slot);
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
    }

    private void ToggleKey(KeyButtonConfig key) => _booth.Toggle(key);

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
