using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CueVideo.Atem;
using CueVideo.Core;

namespace CueVideo.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The composition root: the window is handed the Booth and never builds a client.
            // Config changes from the file arrive on a poll thread, so Booth marshals them
            // here before touching camera links or the switcher.
            var booth = new Booth(new ConfigStore(), new AtemClient(), action => Dispatcher.UIThread.Post(action));
            desktop.MainWindow = new MainWindow { Booth = booth };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
