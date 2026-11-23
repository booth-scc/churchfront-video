using CueVideo.Atem;

namespace CueVideo.TestSupport;

/// <summary>A switcher that records commands and pushes state on demand instead of speaking UDP.</summary>
public sealed class RecordingSwitcher : ISwitcher
{
    public AtemLink Link { get; private set; } = new(AtemLinkState.Idle);
    public AtemState State { get; private set; } = new();
    public string Host { get; private set; } = "";
    public List<string> Commands { get; } = [];

    public event Action<AtemLink>? LinkChanged;
    public event Action<AtemState>? StateChanged;

    public void Connect(string host, int port = 9910)
    {
        Host = host;
        SetLink(new AtemLink(AtemLinkState.Connecting));
    }

    public void Push(AtemState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public void SetLink(AtemLink link)
    {
        Link = link;
        LinkChanged?.Invoke(link);
    }

    public void SetPreview(int source, int me = 0) => Commands.Add($"preview {source} on {me}");

    public void Cut(int me = 0) => Commands.Add($"cut {me}");

    public void Auto(int me = 0) => Commands.Add($"auto {me}");

    public void FadeToBlack(int me = 0) => Commands.Add($"black {me}");

    public void SetKeyOnAir(int keyer, bool on, int me = 0) => Commands.Add($"key {keyer} {on} on {me}");

    public void SetDskOnAir(int dsk, bool on) => Commands.Add($"dsk {dsk} {on}");

    public void Dispose() { }
}
