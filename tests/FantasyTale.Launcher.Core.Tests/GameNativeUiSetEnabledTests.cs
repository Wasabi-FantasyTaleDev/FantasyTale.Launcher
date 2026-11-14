using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class GameNativeUiSetEnabledTests
{
    private static readonly GameNativeUiControlTarget Button = new() { Form = "Ui/DlgCreateRoom.ui", Name = "btnGameMode" };

    [Fact]
    public void SetEnabledNeedsAStateAndATarget()
    {
        new GameNativeUiRequest("op1", "control.setEnabled", "") { Target = Button, Enabled = true }.Validate();
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("op2", "control.setEnabled", "") { Target = Button }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("op3", "control.setEnabled", "") { Enabled = false }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("op4", "control.setEnabled", "x") { Target = Button, Enabled = true }.Validate());
    }

    [Fact]
    public void OnlySetEnabledAcceptsAnEnabledState()
    {
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("op1", "control.activate", "") { Target = Button, Enabled = true }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("op2", "notification", "hello") { Enabled = true }.Validate());
    }
}
