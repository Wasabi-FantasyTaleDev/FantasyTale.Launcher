using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class OverlayRoomModeControllerTests
{
    [Fact]
    public void EntryRegionFollowsTheDialogInClientPixels()
    {
        var form = new GameNativeUiForm("Ui/DlgCreateRoom.ui", 3, true, 295, 60, 434, 648, 800, 600);
        var entry = OverlayRoomModeController.Place(form, new RoomEntry(5, 623, 141, 27));

        // The dialog's bottom left, level with the native OK button (form-relative y 623); forms draw one to one.
        Assert.Equal(new OverlayAnchor(300, 683, 141, 27), entry);
    }

    [Fact]
    public void ALabelIsRestoredOnlyWhileItStillShowsTheExtensionText()
    {
        var replaced = ("Classic team match.", "Zombie mode: survive the night.");
        // Still showing what the extension wrote: put the native description back.
        Assert.True(OverlayRoomModeController.RestoresTo(Label("Zombie mode: survive the night."), replaced));
        // A shared description label the client rewrote for a native pick (kr-1256 sub_5C9550) is already right.
        Assert.False(OverlayRoomModeController.RestoresTo(Label("Bomb mode: plant and defend."), replaced));
        Assert.False(OverlayRoomModeController.RestoresTo(Label("Classic team match."), replaced));
    }

    [Fact]
    public void ATagIsWrittenOverTheSelectionTheDialogShowsOnceDisguised()
    {
        // cn-406 sub_770E80 reads the selection as the disabled mode button, so re-enabling it for the disguise leaves
        // none; that empty selection is the baseline, not a native pick that would remove the tag.
        Assert.Null(OverlayRoomModeController.TaggedOver(new(true, 1), new(true, null)));
        // kr-812 keeps the selection in a form field, so the disguise does not change it.
        Assert.Equal(1, OverlayRoomModeController.TaggedOver(new(true, 1), new(true, 1)));
        // A read that did not reach the dialog says nothing about it.
        Assert.Equal(1, OverlayRoomModeController.TaggedOver(new(true, 1), default));
    }

    [Fact]
    public void TheMostRecentlyLoadedOpenDialogIsFollowedAcrossResources()
    {
        // cn-735 sub_A88A90 loads either resource; whichever is open is followed, and a later load wins.
        var survival = Open("Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui", 7);
        var standard = Open("Ui/Stages/Lobby/DlgCreateRoom.ui", 9);
        var closed = new GameNativeUiFormObservation(GameNativeUiFormWatchStatus.NotOpen, GameNativeUiFormChanges.None, null, null);
        var busy = new GameNativeUiFormObservation(GameNativeUiFormWatchStatus.Busy, GameNativeUiFormChanges.None, null, null);
        Assert.Equal(0, OverlayRoomModeController.Follow([survival, closed]));
        Assert.Equal(1, OverlayRoomModeController.Follow([closed, standard]));
        Assert.Equal(1, OverlayRoomModeController.Follow([survival, standard]));
        Assert.Null(OverlayRoomModeController.Follow([closed, busy]));
        Assert.Null(OverlayRoomModeController.Follow([]));
    }

    private static GameNativeUiFormObservation Open(string resource, int sequence) => new(GameNativeUiFormWatchStatus.Open,
        GameNativeUiFormChanges.Opened, "session", new GameNativeUiForm(resource, sequence, true, 26, 19, 747, 561, 800, 600));

    private static GameNativeUiControlSnapshot Label(string text) =>
        new("label-1", "Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui", "lblGameModeDescription", "label", text, true);
}
