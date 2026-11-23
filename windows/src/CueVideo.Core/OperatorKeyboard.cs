namespace CueVideo.Core;

/// <summary>The main window's two screens.</summary>
public enum MainScreen
{
    Operator,
    Engineer,
}

/// <summary>The keys the Operator screen advertises as shortcuts.</summary>
public enum OperatorKey
{
    Space,
    Return,
    Other,
}

/// <summary>What a key press does on the Operator screen.</summary>
public enum OperatorCommand
{
    None,
    Cut,
    Fade,
}

/// <summary>
/// Space cuts and Return fades only on the Operator screen, and only when no dialog
/// or text field is taking the keyboard. Editing the Engineer setup must never
/// change what is live.
/// </summary>
public static class OperatorKeyboard
{
    public static OperatorCommand Resolve(
        OperatorKey key,
        MainScreen screen,
        bool dialogOpen,
        bool textFieldFocused)
    {
        if (screen != MainScreen.Operator || dialogOpen || textFieldFocused) return OperatorCommand.None;

        return key switch
        {
            OperatorKey.Space => OperatorCommand.Cut,
            OperatorKey.Return => OperatorCommand.Fade,
            _ => OperatorCommand.None,
        };
    }
}
