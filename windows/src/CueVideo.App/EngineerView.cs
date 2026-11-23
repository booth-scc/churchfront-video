using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.ObjectModel;
using CueVideo.Core;
using CueVideo.Ndi;

namespace CueVideo.App;

/// <summary>
/// Engineer screens: map NDI feeds to inputs, edit cameras and the switcher address,
/// and check what the app can actually see on the network.
/// </summary>
public sealed class EngineerView : UserControl
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#F4F5F7"));
    private static readonly IBrush InkDim = new SolidColorBrush(Color.Parse("#9AA0AA"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#767C87"));
    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#6FCF7A"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E0453A"));

    private readonly Booth _booth;
    private readonly IReadOnlyList<PtzCameraConfig> _builtFromCameras;
    private readonly NdiFinder? _finder;
    private readonly ObservableCollection<string> _sources = [];
    private readonly List<(string Key, ComboBox Combo)> _feedCombos = [];
    private readonly TextBlock _scanStatus;
    private readonly TextBlock _configStatus = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _cameraStatus = new() { Spacing = 4 };
    private readonly StackPanel _sourceList = new() { Spacing = 2 };

    public EngineerView(Booth booth)
    {
        _booth = booth;
        _builtFromCameras = booth.Config.Cameras.ToList();
        booth.ConfigChanged += RefreshConfigStatus;

        try
        {
            _finder = NdiFinder.Create(showLocalSources: true);
        }
        catch (Exception)
        {
            _finder = null;
        }

        _scanStatus = new TextBlock { Text = "Not scanned", FontSize = 10, Foreground = Muted };

        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            Margin = new Thickness(18),
        };

        tabs.Items.Add(new TabItem { Header = "Setup", Content = BuildSetup() });
        tabs.Items.Add(new TabItem { Header = "Status", Content = BuildStatus() });

        Content = tabs;
        AttachedToVisualTree += async (_, _) => await ScanAsync();
    }

    public void Dispose()
    {
        _booth.ConfigChanged -= RefreshConfigStatus;
        _finder?.Dispose();
    }

    /// <summary>Says whether the last save reached the file, or why it could not.</summary>
    private void RefreshConfigStatus()
    {
        var error = _booth.ConfigError;

        _configStatus.Text = error
            ?? (_booth.ReadOnly ? "Demo mode: edits reach the screen but are not saved to this file." : "");
        _configStatus.Foreground = error is null ? Muted : Bad;
        _configStatus.IsVisible = _configStatus.Text.Length > 0;
    }

    /// <summary>
    /// True while the camera rows still point at the camera objects they were built from.
    /// A hand edit swaps in a new setup, and the rows must be built again.
    /// </summary>
    public bool IsBuiltFrom(CueConfig config) =>
        config.Cameras.Count == _builtFromCameras.Count &&
        !config.Cameras.Where((camera, index) => !ReferenceEquals(camera, _builtFromCameras[index])).Any();

    private Control BuildSetup()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(6) };

        panel.Children.Add(Heading("NDI FEEDS"));
        panel.Children.Add(Note("Pick the NDI feed for each input. The operator's tiles use these pictures."));

        var scanRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var scan = Ghost("Scan NDI");
        scan.Click += async (_, _) => await ScanAsync();
        scanRow.Children.Add(scan);
        scanRow.Children.Add(_scanStatus);
        panel.Children.Add(scanRow);

        var feeds = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,320"),
            RowDefinitions = new RowDefinitions("Auto"),
            RowSpacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
        };

        feeds.Children.Add(Cell(new TextBlock { Text = "INPUT", FontSize = 9, Foreground = Muted }, 0, 0));
        feeds.Children.Add(Cell(new TextBlock { Text = "NDI SOURCE", FontSize = 9, Foreground = Muted }, 1, 0));

        var sources = _booth.Sources;

        var row = 1;
        foreach (var source in sources)
        {
            feeds.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock
            {
                Text = $"{(string.IsNullOrWhiteSpace(source.Label) ? source.Input.ToString() : source.Label)}  (input {source.Input})",
                FontSize = 11,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var combo = new ComboBox
            {
                Width = 320,
                FontSize = 11,
                ItemsSource = _sources,
            };

            var key = source.Input.ToString();
            var current = _booth.Config.Pictures.TryGetValue(key, out var picture) ? picture : "";
            if (!string.IsNullOrWhiteSpace(current)) combo.SelectedItem = current;

            _feedCombos.Add((key, combo));

            combo.SelectionChanged += (_, _) =>
            {
                _booth.SetPicture(source.Input, combo.SelectedItem as string);
            };

            feeds.Children.Add(Cell(label, 0, row));
            feeds.Children.Add(Cell(combo, 1, row));
            row++;
        }

        panel.Children.Add(feeds);

        panel.Children.Add(Heading("CAMERAS"));
        panel.Children.Add(Note("VISCA over IP. Port 52381 unless the camera was changed."));

        var cameras = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,130,70,70,70,190,Auto"),
            RowDefinitions = new RowDefinitions("Auto"),
            RowSpacing = 6,
            ColumnSpacing = 6,
        };

        var headers = new[] { "NAME", "ADDRESS", "PORT", "INPUT", "PRESET", "PROTOCOL", "" };
        for (var i = 0; i < headers.Length; i++)
        {
            cameras.Children.Add(Cell(new TextBlock { Text = headers[i], FontSize = 9, Foreground = Muted }, i, 0));
        }

        row = 1;
        foreach (var camera in _booth.Config.Cameras)
        {
            cameras.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var name = new TextBox { Text = camera.Name, FontSize = 11, Width = 110 };
            var host = new TextBox { Text = camera.Host, FontSize = 11, Width = 130 };
            var port = new TextBox { Text = camera.EffectivePort.ToString(), FontSize = 11, Width = 70 };
            var input = new TextBox { Text = camera.AtemInput?.ToString() ?? "", FontSize = 11, Width = 70 };
            var preset = new TextBox { Text = camera.FirstPreset.ToString(), FontSize = 11, Width = 70 };

            name.LostFocus += (_, _) => _booth.SetCameraName(camera.Id, name.Text ?? camera.Name);
            host.LostFocus += (_, _) => _booth.SetCameraHost(camera.Id, host.Text ?? camera.Host);
            port.LostFocus += (_, _) =>
            {
                if (int.TryParse(port.Text, out var value)) _booth.SetCameraPort(camera.Id, value);
            };
            input.LostFocus += (_, _) =>
            {
                _booth.SetCameraInput(camera.Id, int.TryParse(input.Text, out var value) ? value : null);
            };
            preset.LostFocus += (_, _) =>
            {
                if (int.TryParse(preset.Text, out var value)) _booth.SetCameraFirstPreset(camera.Id, value);
            };

            var test = Ghost("Test");
            test.Width = 64;
            test.Click += async (_, _) =>
            {
                test.Content = "…";
                var result = await ReadCameraAsync(camera);
                test.Content = "Test";
                _cameraStatus.Children.Insert(0, new TextBlock
                {
                    Text = $"{camera.Name}: {result}",
                    FontSize = 10,
                    Foreground = result.StartsWith("ok", StringComparison.OrdinalIgnoreCase) ? Ok : Bad,
                });
            };

            var protocol = new TextBlock
            {
                Text = camera.IsSupported
                    ? camera.ProtocolLabel
                    : $"{camera.ProtocolLabel} · UNSUPPORTED",
                FontSize = 10,
                Foreground = camera.IsSupported ? InkDim : Bad,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };

            cameras.Children.Add(Cell(name, 0, row));
            cameras.Children.Add(Cell(host, 1, row));
            cameras.Children.Add(Cell(port, 2, row));
            cameras.Children.Add(Cell(input, 3, row));
            cameras.Children.Add(Cell(preset, 4, row));
            cameras.Children.Add(Cell(protocol, 5, row));
            cameras.Children.Add(Cell(test, 6, row));
            row++;
        }

        panel.Children.Add(cameras);
        panel.Children.Add(_cameraStatus);

        panel.Children.Add(Heading("SWITCHER"));
        var switcherRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var atemHost = new TextBox { Text = _booth.Config.AtemHost, FontSize = 11, Width = 200 };
        var saveAtem = Ghost("Save");
        saveAtem.Click += (_, _) => _booth.SetSwitcherHost(atemHost.Text ?? "");
        switcherRow.Children.Add(atemHost);
        switcherRow.Children.Add(saveAtem);
        panel.Children.Add(switcherRow);

        panel.Children.Add(Note($"Config file: {_booth.ConfigPath}"));
        panel.Children.Add(_configStatus);
        RefreshConfigStatus();

        return new ScrollViewer { Content = panel };
    }

    private Control BuildStatus()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(6) };

        panel.Children.Add(Heading("WHAT THE APP CAN SEE"));

        var status = new TextBlock { Text = _scanStatus.Text, FontSize = 10, Foreground = Muted };

        var refresh = Ghost("Refresh");
        refresh.Click += async (_, _) =>
        {
            await ScanAsync();
            status.Text = _scanStatus.Text;
            RefreshSourceList();
        };
        panel.Children.Add(refresh);
        panel.Children.Add(status);
        panel.Children.Add(_sourceList);

        panel.Children.Add(Heading("CAMERAS"));

        var unsupported = _booth.Config.UnsupportedCameras;
        if (unsupported.Count > 0)
        {
            panel.Children.Add(Note(
                "This port drives VISCA over IP only. Not driven: " +
                string.Join(", ", unsupported.Select(c => $"{c.Name} ({c.ProtocolLabel})"))));
        }

        var results = new StackPanel { Spacing = 4 };
        var testAll = Ghost("Test all cameras");
        testAll.Click += async (_, _) =>
        {
            results.Children.Clear();
            foreach (var camera in _booth.Config.Cameras)
            {
                var result = await ReadCameraAsync(camera);
                results.Children.Add(new TextBlock
                {
                    Text = $"{camera.Name} ({camera.Host}:{camera.EffectivePort}): {result}",
                    FontSize = 10,
                    Foreground = result.StartsWith("ok", StringComparison.OrdinalIgnoreCase) ? Ok : Bad,
                });
            }
        };
        panel.Children.Add(testAll);
        panel.Children.Add(results);

        panel.Children.Add(Heading("SWITCHER"));
        panel.Children.Add(Note(_booth.Config.AtemHost));

        return new ScrollViewer { Content = panel };
    }

    private async Task<string> ReadCameraAsync(PtzCameraConfig camera)
    {
        if (!camera.IsSupported) return $"unsupported · {camera.ProtocolLabel}";

        using var link = CameraLinks.Open(camera);
        if (link is null) return "no address";

        var position = await link.ReadPositionAsync(counts: true, TimeSpan.FromSeconds(2));

        return position is { } value
            ? $"ok · pan {value.Pan}, tilt {value.Tilt}, zoom {value.Zoom}"
            : "no reply";
    }

    private async Task ScanAsync()
    {
        _sources.Clear();

        if (_finder is null)
        {
            _scanStatus.Text = "NDI runtime not available";
            return;
        }

        var found = await Task.Run(() => _finder.GetSources(TimeSpan.FromSeconds(2)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in found)
        {
            // NDI can advertise the same sender more than once (IPv4 and IPv6).
            var shortName = ShortName(source.Name).Trim();
            if (!seen.Add(shortName)) continue;
            _sources.Add(source.Name);
        }

        foreach (var picture in _booth.Config.Pictures.Values)
        {
            if (!string.IsNullOrWhiteSpace(picture) && seen.Add(picture.Trim())) _sources.Add(picture);
        }

        _scanStatus.Text = $"{_sources.Count} source{(_sources.Count == 1 ? "" : "s")} found";

        foreach (var (key, combo) in _feedCombos)
        {
            if (!_booth.Config.Pictures.TryGetValue(key, out var picture) || string.IsNullOrWhiteSpace(picture))
            {
                continue;
            }

            var match = _sources.FirstOrDefault(s =>
                s.Equals(picture, StringComparison.OrdinalIgnoreCase) ||
                ShortName(s).Equals(picture, StringComparison.OrdinalIgnoreCase) ||
                ShortName(s).Equals(ShortName(picture), StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                _sources.Add(picture);
                match = picture;
            }

            combo.SelectedItem = match;
        }

        RefreshSourceList();
    }

    private void RefreshSourceList()
    {
        _sourceList.Children.Clear();

        if (_sources.Count == 0)
        {
            _sourceList.Children.Add(new TextBlock { Text = "No NDI sources yet.", FontSize = 10, Foreground = Muted });
            return;
        }

        foreach (var name in _sources)
        {
            _sourceList.Children.Add(new TextBlock { Text = "• " + name, FontSize = 11, Foreground = Ink });
        }
    }

    private static string ShortName(string name)
    {
        var open = name.LastIndexOf('(');
        var close = name.LastIndexOf(')');
        return open >= 0 && close > open ? name[(open + 1)..close] : name;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 10,
        LetterSpacing = 1.6,
        FontWeight = FontWeight.SemiBold,
        Foreground = Muted,
        Margin = new Thickness(0, 10, 0, 0),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button Ghost(string text) => new()
    {
        Content = text,
        FontSize = 10,
        Padding = new Thickness(9, 4),
        Classes = { "ghost" },
    };

    private static Control Cell(Control control, int column, int row)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        return control;
    }
}
