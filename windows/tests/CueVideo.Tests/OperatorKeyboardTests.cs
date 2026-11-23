using CueVideo.Core;

namespace CueVideo.Tests;

public sealed class OperatorKeyboardTests
{
    [Theory]
    [InlineData(OperatorKey.Space, OperatorCommand.Cut)]
    [InlineData(OperatorKey.Return, OperatorCommand.Fade)]
    public void OperatorShortcutsCutAndFade(OperatorKey key, OperatorCommand command)
    {
        Assert.Equal(command, OperatorKeyboard.Resolve(key, MainScreen.Operator, dialogOpen: false, textFieldFocused: false));
    }

    [Theory]
    [InlineData(OperatorKey.Space)]
    [InlineData(OperatorKey.Return)]
    public void EngineerPageNeverCutsOrFades(OperatorKey key)
    {
        Assert.Equal(OperatorCommand.None, OperatorKeyboard.Resolve(key, MainScreen.Engineer, dialogOpen: false, textFieldFocused: false));
    }

    [Theory]
    [InlineData(OperatorKey.Space)]
    [InlineData(OperatorKey.Return)]
    public void ATextFieldTakesTheKeyInstead(OperatorKey key)
    {
        Assert.Equal(OperatorCommand.None, OperatorKeyboard.Resolve(key, MainScreen.Operator, dialogOpen: false, textFieldFocused: true));
    }

    [Theory]
    [InlineData(OperatorKey.Space)]
    [InlineData(OperatorKey.Return)]
    public void ADialogTakesTheKeyInstead(OperatorKey key)
    {
        Assert.Equal(OperatorCommand.None, OperatorKeyboard.Resolve(key, MainScreen.Operator, dialogOpen: true, textFieldFocused: false));
    }

    [Fact]
    public void OtherKeysDoNothing()
    {
        Assert.Equal(OperatorCommand.None, OperatorKeyboard.Resolve(OperatorKey.Other, MainScreen.Operator, dialogOpen: false, textFieldFocused: false));
    }
}
