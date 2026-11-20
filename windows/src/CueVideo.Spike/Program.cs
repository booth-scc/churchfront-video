using System.Runtime.InteropServices;

namespace CueVideo.Spike;

internal static class Program
{
    private static readonly (string Label, string Address, string Role)[] BoothDevices =
    [
        ("ATEM", "192.168.10.159:9910", "1 M/E Constellation HD"),
        ("Cam-1", "192.168.10.183", "X4 Ultra"),
        ("Cam-2S", "192.168.10.137", "MAKI Ultra (static)"),
        ("Cam-3", "192.168.10.149", "X4 Ultra"),
        ("Cam-4", "192.168.10.136", "X4 Ultra"),
        ("Companion", "192.168.10.101:8000", "web UI / OSC"),
        ("OBS WebSocket", "192.168.10.101:4455", "read-only reference"),
    ];

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            Console.WriteLine("CueVideo.Spike - Phase 0 harness (doctor mode)");
            Console.WriteLine("  dotnet run --project src/CueVideo.Spike");
            Console.WriteLine("  Reports runtime, NDI runtime, config compatibility and the expected booth endpoints.");
            return 0;
        }

        Console.WriteLine("Churchfront Video - Phase 0 harness");
        Console.WriteLine($"Runtime     : .NET {Environment.Version} ({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture})");
        Console.WriteLine();

        var ndi = FindNdiRuntime();
        Console.WriteLine(ndi is null
            ? "NDI runtime  : NOT FOUND - install NDI 6 Tools before gate 1"
            : $"NDI runtime  : {ndi}");

        var config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CueVideo", "config.json");
        Console.WriteLine(File.Exists(config)
            ? $"Config       : {config}"
            : $"Config       : none yet (will be created at {config})");

        var obs = System.Diagnostics.Process.GetProcessesByName("obs64");
        Console.WriteLine($"OBS running  : {(obs.Length > 0 ? "yes" : "no")}");
        Console.WriteLine();

        Console.WriteLine("Expected booth endpoints (from the 2026-11-20 audit):");
        foreach (var (label, address, role) in BoothDevices)
        {
            Console.WriteLine($"  {label,-15} {address,-22} {role}");
        }

        Console.WriteLine();
        Console.WriteLine("Gates still to run: G1 camera NDI, G2 ATEM tally, G3 VISCA readback, G4 OBS program NDI.");
        Console.WriteLine("Cameras and ATEM must be powered on; the spike UI opens with `dotnet run --project src/CueVideo.App`.");
        return 0;
    }

    private static string? FindNdiRuntime()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Tools", "Runtime", "Processing.NDI.Lib.x64.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 5 Tools", "Runtime", "Processing.NDI.Lib.x64.dll"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
