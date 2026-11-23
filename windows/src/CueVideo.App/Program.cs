using Avalonia;
using System;

namespace CueVideo.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CueVideo", "logs");
                Directory.CreateDirectory(directory);

                var path = Path.Combine(directory, "crash.log");
                File.AppendAllText(path, $"{DateTime.Now:O} {e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // nothing useful to do while crashing
            }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
