namespace CueVideo.Atem;

/// <summary>
/// Everything the app mirrors from the switcher. Ported field for field from
/// <c>Sources/CueATEM/ATEM.swift</c> so the two implementations stay comparable.
/// </summary>
public sealed class AtemState
{
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<int, AtemInput> Inputs { get; } = new();
    public Dictionary<int, int> Program { get; } = new();
    public Dictionary<int, int> Preview { get; } = new();

    /// <summary>Transition handle 0..1 while a transition is running.</summary>
    public Dictionary<int, double> Position { get; } = new();
    public Dictionary<int, bool> InTransition { get; } = new();

    /// <summary>0 mix, 1 dip, 2 wipe, 3 DVE, 4 sting.</summary>
    public Dictionary<int, int> Style { get; } = new();

    /// <summary>Bit 0 background, bit 1 key 1, bit 2 key 2, ...</summary>
    public Dictionary<int, int> NextSelection { get; } = new();
    public Dictionary<int, int> MixRate { get; } = new();
    public Dictionary<int, Dictionary<int, bool>> KeyOnAir { get; } = new();
    public Dictionary<int, Dictionary<int, int>> KeyFill { get; } = new();
    public Dictionary<int, Dictionary<int, int>> KeyCut { get; } = new();
    public Dictionary<int, int> DskFill { get; } = new();
    public Dictionary<int, int> DskCut { get; } = new();
    public Dictionary<int, bool> DskOnAir { get; } = new();
    public Dictionary<int, bool> DskInTransition { get; } = new();
    public Dictionary<int, bool> DskTie { get; } = new();
    public Dictionary<int, bool> FtbBlack { get; } = new();
    public Dictionary<int, bool> FtbInTransition { get; } = new();
    public Dictionary<int, int> Aux { get; } = new();
    public Dictionary<int, string> Macros { get; } = new();
    public int? MacroRunning { get; set; }
    public int StreamStatus { get; set; }
    public int RecordStatus { get; set; }
    public bool HasStreaming { get; set; }
    public bool HasRecording { get; set; }

    /// <summary>Bit 0 program, bit 1 preview, per source.</summary>
    public Dictionary<int, byte> Tally { get; } = new();

    public int MeCount => Math.Max(1, (Program.Count == 0 ? 0 : Program.Keys.Max()) + 1);
    public int KeyerCount => KeyOnAir.TryGetValue(0, out var keys) ? keys.Count : 0;
    public int DskCount => DskOnAir.Count;
    public int AuxCount => Aux.Count;
    public bool Streaming => (StreamStatus & 4) != 0;
    public bool StreamConnecting => (StreamStatus & 2) != 0;
    public bool Recording => (RecordStatus & 1) != 0;

    public IReadOnlyList<AtemInput> BusSources =>
        Inputs.Values.Where(i => i.IsBusSource).OrderBy(i => i.SortKey).ToList();

    public IReadOnlyList<AtemInput> AuxSources =>
        Inputs.Values.Where(i => i.IsAuxSource).OrderBy(i => i.SortKey).ToList();

    public string Name(int? id) =>
        id is null ? "?" : Inputs.TryGetValue(id.Value, out var input) ? input.Short : id.Value.ToString();

    public AtemState Clone()
    {
        var copy = new AtemState
        {
            Product = Product,
            Version = Version,
            MacroRunning = MacroRunning,
            StreamStatus = StreamStatus,
            RecordStatus = RecordStatus,
            HasStreaming = HasStreaming,
            HasRecording = HasRecording,
        };

        Copy(Inputs, copy.Inputs);
        Copy(Program, copy.Program);
        Copy(Preview, copy.Preview);
        Copy(Position, copy.Position);
        Copy(InTransition, copy.InTransition);
        Copy(Style, copy.Style);
        Copy(NextSelection, copy.NextSelection);
        Copy(MixRate, copy.MixRate);
        Copy(DskFill, copy.DskFill);
        Copy(DskCut, copy.DskCut);
        Copy(DskOnAir, copy.DskOnAir);
        Copy(DskInTransition, copy.DskInTransition);
        Copy(DskTie, copy.DskTie);
        Copy(FtbBlack, copy.FtbBlack);
        Copy(FtbInTransition, copy.FtbInTransition);
        Copy(Aux, copy.Aux);
        Copy(Macros, copy.Macros);
        Copy(Tally, copy.Tally);

        foreach (var (me, keys) in KeyOnAir) copy.KeyOnAir[me] = new Dictionary<int, bool>(keys);
        foreach (var (me, fill) in KeyFill) copy.KeyFill[me] = new Dictionary<int, int>(fill);
        foreach (var (me, cut) in KeyCut) copy.KeyCut[me] = new Dictionary<int, int>(cut);

        return copy;
    }

    private static void Copy<TKey, TValue>(Dictionary<TKey, TValue> from, Dictionary<TKey, TValue> to)
        where TKey : notnull
    {
        foreach (var (key, value) in from) to[key] = value;
    }
}
