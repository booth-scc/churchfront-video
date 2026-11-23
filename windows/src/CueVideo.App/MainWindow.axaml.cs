using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    private readonly List<SourceTile> _tiles = [];
    private readonly List<NdiView> _thumbs = [];

    private Booth _booth = null!;
    private EngineerView? _engineerView;
    private NdiView? _liveView;
    private NdiView? _nextView;
    private string? _nextNdiName;
    private string? _liveNdiName;
    private int _shownPreview = 1;
    private int _shownProgram = 1;
    private bool _initialised;
    private double _speed = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The app's shared Booth, set by the composition root before the window opens.</summary>
    public required Booth Booth
    {
        get => _booth;
        init
        {
            _booth = value;
            value.Rendered += state => Dispatcher.UIThread.Post(() => ShowBooth(state));
            value.ConfigChanged += RebuildFromConfig;
        }
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
        _booth.Start();

        BuildSourceTiles();
        WireTransport();
        WireCameraPanel();
        WireSpeed();

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

        _engineerView?.Dispose();
        _engineerView = null;

        // The Booth owns the camera links and closes them with itself.
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
        _engineerView ??= new EngineerView(_booth);
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

    /// <summary>
    /// Rebuilds the controls that come from the setup after Booth applied a change, whether
    /// the Engineer screen made it or someone edited config.json. Booth owns every reload
    /// decision; the window only rebuilds what it renders.
    /// </summary>
    private void RebuildFromConfig()
    {
        foreach (var thumb in _thumbs) thumb.Dispose();
        _thumbs.Clear();

        BuildSourceTiles();

        // The Engineer rows hold the camera objects they were built from; a hand edit swaps
        // in new ones, so build the view again rather than edit a setup nobody uses.
        if (_engineerView is not null && !_engineerView.IsBuiltFrom(_booth.Config))
        {
            _engineerView.Dispose();
            _engineerView = null;
            EngineerHost.Content = null;
            if (EngineerHost.IsVisible) OpenEngineer();
        }

        // The rebuilt tiles must take every part of the snapshot, including exposure.
        _liveNdiName = null;
        _nextNdiName = null;
        _initialised = false;
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
        Hold(PanUpLeft, () => Drive(-1, 1), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanUp, () => Drive(0, 1), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanUpRight, () => Drive(1, 1), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanLeft, () => Drive(-1, 0), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanRight, () => Drive(1, 0), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanDownLeft, () => Drive(-1, -1), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanDown, () => Drive(0, -1), () => _booth.LinkOnNext()?.StopMove());
        Hold(PanDownRight, () => Drive(1, -1), () => _booth.LinkOnNext()?.StopMove());

        ZoomInButton.Click += (_, _) => _booth.LinkOnNext()?.Zoom(Math.Clamp(0.6 * _speed, 0, 1));
        ZoomOutButton.Click += (_, _) => _booth.LinkOnNext()?.Zoom(-Math.Clamp(0.6 * _speed, 0, 1));
        FocusButton.Click += (_, _) => _booth.LinkOnNext()?.AutoFocus(true);

        ExposureAutoButton.Click += async (_, _) => await _booth.AutoExposureAsync();
    }

    private void Drive(double pan, double tilt)
    {
        _booth.LinkOnNext()?.Drive(Math.Clamp(pan * _speed, -1, 1), Math.Clamp(tilt * _speed, -1, 1));
    }

    private static void Hold(Button button, Action start, Action stop)
    {
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => start(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => stop(), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => stop();
    }

    private void BuildShots(PtzCameraConfig? cameraOnNext)
    {
        ShotRows.Children.Clear();

        foreach (var camera in _booth.Config.Cameras)
        {
            if (camera.AtemInput is not { } input) continue;

            var buttons = camera.ShotButtons;
            var row = new Grid
            {
                ColumnDefinitions = ShotColumns(buttons.Count),
                ColumnSpacing = 5,
            };

            // The row for the camera the snapshot put on Next, not a second lookup.
            var isNext = camera.Id == cameraOnNext?.Id;

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

            if (camera.DriveProblem is { } cameraProblem) ToolTip.SetTip(cameraButton, cameraProblem);

            cameraButton.Click += (_, _) => _booth.SetPreview(input);

            row.Children.Add(cameraButton);

            if (buttons.Count == 0)
            {
                var none = new TextBlock
                {
                    Text = CameraShotRow.NoShots,
                    FontSize = 9.5,
                    Foreground = InkDim,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                Grid.SetColumn(none, 1);
                row.Children.Add(none);
            }

            // A shot needs a link this port can open; say why when it cannot.
            var shotProblem = camera.DriveProblem;

            var column = 1;
            foreach (var shotButton in buttons)
            {
                var problem = shotProblem ?? shotButton.Problem;

                var shot = new Button
                {
                    Content = shotButton.Label,
                    Height = 30,
                    FontSize = 9.5,
                    Padding = new Thickness(6, 2),
                    Classes = { "ghost" },
                    IsEnabled = problem is null,
                };

                shot.Click += (_, _) => _ = _booth.RecallShotAsync(input, shotButton);
                if (problem is not null) ToolTip.SetTip(shot, problem);

                Grid.SetColumn(shot, column++);
                row.Children.Add(shot);
            }

            ShotRows.Children.Add(row);
        }
    }

    /// <summary>A camera column, then one column per shot button, or one for the empty state.</summary>
    private static ColumnDefinitions ShotColumns(int buttons) =>
        new(buttons == 0 ? "62,*" : "62" + string.Concat(Enumerable.Repeat(",*", buttons)));

    // ---- Pictures ----

    private void AttachLive()
    {
        UpdateLiveTile(_shownProgram);
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

    private void UpdateNextTile(int preview)
    {
        var source = Environment.GetEnvironmentVariable("CUEVIDEO_NDI_NEXT");

        if (string.IsNullOrWhiteSpace(source))
        {
            source = _booth.NdiFor(preview);
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
    /// The Operator screen's one render path: program, preview, tally, status, the camera on
    /// Next and its exposure all come from the same Booth snapshot.
    /// </summary>
    private void ShowBooth(BoothSnapshot state)
    {
        ProgramSource.Text = state.ProgramLabel;
        NextSource.Text = state.PreviewLabel;

        var previewChanged = state.PreviewInput != _shownPreview || !_initialised;
        var programChanged = state.ProgramInput != _shownProgram || !_initialised;

        _initialised = true;
        _shownPreview = state.PreviewInput;
        _shownProgram = state.ProgramInput;

        ShowTally(state);
        ShowSwitcherLabels(state);
        ShowStatus(state.Status);
        ShowCamera(state);

        if (programChanged) UpdateLiveTile(state.ProgramInput);

        if (!previewChanged) return;

        // The shot rows mark the camera on Next, so a preview change is what rebuilds them.
        BuildShots(state.CameraOnNext);
        UpdateNextTile(state.PreviewInput);
        _ = _booth.RefreshExposureAsync();
    }

    private void ShowCamera(BoothSnapshot state)
    {
        CameraChipName.Text = state.CameraOnNextLabel;

        CameraProtocolNotice.IsVisible = state.CameraOnNextNotice is not null;
        CameraProtocolNotice.Text = state.CameraOnNextNotice ?? "";

        ExposureCamText.Text = state.ExposureSubject;
        ExposureGain.Text = state.Exposure.Gain;
        ExposureShutter.Text = state.Exposure.Shutter;
        ExposureIris.Text = state.Exposure.Iris;
        ExposureWhite.Text = state.Exposure.WhiteBalance;
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
        Hold(near, () => _booth.LinkOnNext()?.FocusNear(), () => _booth.LinkOnNext()?.StopFocus());
        Hold(far, () => _booth.LinkOnNext()?.FocusFar(), () => _booth.LinkOnNext()?.StopFocus());
        auto.Click += (_, _) => _booth.LinkOnNext()?.AutoFocus(true);
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
            button.Click += (_, _) => _booth.LinkOnNext()?.Recall(preset);
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

        // One link for the check and the command, so a skipped command never reports success.
        void RunOnCameraOnNext(Action<ViscaIpLink> command, string done)
        {
            if (_booth.LinkOnNext() is not { } link)
            {
                saveStatus.Text = _booth.Render().CameraOnNextProblem ?? "No camera on Next.";
                return;
            }

            command(link);
            saveStatus.Text = done;
        }

        foreach (var number in new[] { 20, 21, 22, 23 })
        {
            var save = Ghost($"Save {number}");
            save.Width = 72;
            var slot = number;
            save.Click += (_, _) => RunOnCameraOnNext(
                link => link.Store(slot),
                $"Saved the current view to camera preset {slot}.");

            var recall = Ghost($"Go {number}");
            recall.Width = 68;
            recall.Click += (_, _) => RunOnCameraOnNext(
                link => link.Recall(slot),
                $"Recalled camera preset {slot}.");

            saveRow.Children.Add(save);
            recallRow.Children.Add(recall);
        }

        panel.Children.Add(saveRow);
        panel.Children.Add(recallRow);
        panel.Children.Add(saveStatus);

        panel.Children.Add(Heading("EXPOSURE"));
        var exposureAuto = Ghost("Auto exposure");
        exposureAuto.Click += async (_, _) => await _booth.AutoExposureAsync();
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
