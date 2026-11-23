namespace CueVideo.Atem;

/// <summary>
/// The switcher seam the rest of the app drives: its latest mirror and the commands the
/// Operator screen sends. <see cref="AtemClient"/> speaks UDP; tests push state through a
/// fake without a network.
/// </summary>
public interface ISwitcher : IDisposable
{
    AtemLink Link { get; }
    AtemState State { get; }
    string Host { get; }

    event Action<AtemLink>? LinkChanged;
    event Action<AtemState>? StateChanged;

    void Connect(string host, int port = 9910);
    void SetPreview(int source, int me = 0);
    void Cut(int me = 0);
    void Auto(int me = 0);
    void FadeToBlack(int me = 0);
    void SetKeyOnAir(int keyer, bool on, int me = 0);
    void SetDskOnAir(int dsk, bool on);
}
