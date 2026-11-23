using CueVideo.Atem;
using CueVideo.Ndi;
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
            Console.WriteLine("CueVideo.Spike - Phase 0 harness");
            Console.WriteLine("  dotnet run --project src/CueVideo.Spike                  report toolchain and booth facts");
            Console.WriteLine("  dotnet run --project src/CueVideo.Spike -- atem [host] [seconds]");
            Console.WriteLine("                                                          connect to the switcher and mirror state");
            return 0;
        }

        return args.Length > 0 && args[0].Equals("atem", StringComparison.OrdinalIgnoreCase)
            ? Atem(args.Skip(1).ToArray())
            : args.Length > 0 && args[0].Equals("fake-atem", StringComparison.OrdinalIgnoreCase)
                ? FakeAtem(args.Skip(1).ToArray())
                : args.Length > 0 && args[0].Equals("ndi", StringComparison.OrdinalIgnoreCase)
                    ? NdiProbe(args.Skip(1).ToArray())
                    : args.Length > 0 && args[0].Equals("fake-ndi", StringComparison.OrdinalIgnoreCase)
                        ? NdiTestPattern.Run(args.Skip(1).ToArray())
                        : args.Length > 0 && args[0].Equals("fake-ndi-multi", StringComparison.OrdinalIgnoreCase)
                            ? NdiTestPattern.RunMulti(args.Skip(1).ToArray())
                            : args.Length > 0 && args[0].Equals("fake-visca", StringComparison.OrdinalIgnoreCase)
                                ? FakeVisca(args.Skip(1).ToArray())
                                : Doctor();
    }

    private static int Doctor()
    {
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
        return 0;
    }

    private static int Atem(string[] args)
    {
        var host = args.Length > 0 ? args[0] : "192.168.10.159";
        var seconds = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 8;

        using var client = new AtemClient();
        client.LinkChanged += link => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] link    {link.State} {link.Message}");
        client.StateChanged += state => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] state   {Describe(state)}");
        client.Connect(host);

        Console.WriteLine($"Connecting to {host}:9910 for {seconds}s ...");
        Thread.Sleep(TimeSpan.FromSeconds(seconds));

        var final = client.State;
        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"Product  : {final.Product}");
        Console.WriteLine($"Version  : {final.Version}");
        Console.WriteLine($"Program  : {final.Name(final.Program.GetValueOrDefault(0))}");
        Console.WriteLine($"Preview  : {final.Name(final.Preview.GetValueOrDefault(0))}");
        Console.WriteLine($"Inputs   : {string.Join(", ", final.BusSources.Select(i => $"{i.Id}={i.Short}"))}");
        Console.WriteLine($"Link     : {client.Link.State} {client.Link.Message}");
        Console.WriteLine($"Commands : {client.SentLog.Count} sent");

        return final.Product.Length > 0 ? 0 : 2;
    }

    private static int FakeAtem(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 9910;
        FakeSwitcher.Run(port);
        return 0;
    }

    private static int FakeVisca(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 52381;
        FakeCamera.Run(port);
        return 0;
    }

    private static int NdiProbe(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 4;

        if (!NdiRuntime.Initialize())
        {
            Console.WriteLine($"NDI runtime : {NdiRuntime.Error}");
            return 2;
        }

        Console.WriteLine($"NDI runtime : {NdiRuntime.RuntimePath}");

        using var finder = NdiFinder.Create(showLocalSources: true);
        var sources = finder.GetSources(TimeSpan.FromSeconds(seconds));

        Console.WriteLine($"Sources     : {sources.Count}");
        foreach (var source in sources)
        {
            Console.WriteLine($"  - {source.Name}   (short: {source.ShortName})");
        }

        var target = args.Length > 1
            ? sources.FirstOrDefault(s => s.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase))
            : sources.FirstOrDefault(s => s.ShortName.Contains("OBS", StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            Console.WriteLine();
            Console.WriteLine("No source selected. Pass part of a name to receive frames, for example: ndi 4 OBS");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"Receiving from {target.Name} for {seconds}s ...");

        using var receiver = NdiReceiver.Open(target.Name, lowestBandwidth: false);
        var frames = 0;
        var firstWidth = 0;
        var firstHeight = 0;
        var fourCc = "";
        var until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until)
        {
            receiver.TryCapture(250, (in NdiFrameView frame) =>
            {
                if (frames == 0)
                {
                    firstWidth = frame.Width;
                    firstHeight = frame.Height;
                    fourCc = frame.FourCc;
                }

                frames++;
            });
        }

        Console.WriteLine(frames > 0
            ? $"Frames      : {frames} ({frames / (double)seconds:0.0}/s), {firstWidth}x{firstHeight} {fourCc}"
            : "Frames      : none - sender unreachable or wrong name");

        return frames > 0 ? 0 : 3;
    }

    private static string Describe(AtemState state)
    {
        var program = state.Name(state.Program.GetValueOrDefault(0));
        var preview = state.Name(state.Preview.GetValueOrDefault(0));
        var transition = state.InTransition.GetValueOrDefault(0) ? "running" : "idle";
        var keys = state.KeyOnAir.TryGetValue(0, out var map)
            ? string.Join(",", map.Where(kv => kv.Value).Select(kv => kv.Key + 1))
            : "-";
        return $"PGM {program,-6} PVW {preview,-6} transition {transition,-7} keys on: {keys}";
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
