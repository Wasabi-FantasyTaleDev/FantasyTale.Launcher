using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class ResourceEditPipelineTests
{
    [Theory]
    [InlineData("replace")]
    [InlineData("remove")]
    public void OnePluginCannotSilentlyEraseItsOwnDescendantEdits(string action)
    {
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        var original = File.ReadAllBytes(Path.Combine(_client, "Ui/Ui.bfz"));
        var plugin = Plugin("ui.contradiction", $$"""
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "{{action}}"
            control = "pnlMain"
            {{(action == "replace" ? "element = '<UiPanel Name=\"pnlMain\"><UiLabel Name=\"lblTitle\" Text=\"Replacement\" /></UiPanel>'" : "")}}
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "patch"
            control = "pnlMain/lblTitle"
            set = { Text = "Must not disappear" }
            """);
        var error = Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(Context(), [Select(plugin)]));
        Assert.Contains("contradictory subtree and descendant edits", error.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_client, "Ui/Ui.bfz")));
    }

    [Fact]
    public void OnePluginCanRemoveBothAParentAndItsDescendant()
    {
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        var plugin = Plugin("ui.removals", """
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "remove"
            control = "pnlMain"
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "remove"
            control = "pnlMain/lblTitle"
            """);
        using var prepared = new BubbleFighterModule().PrepareResources(Context(), [Select(plugin)]);
        var form = UiDocument.Load(Member(Assert.Single(prepared.Files), "Ui/Test.ui"), "Ui/Test.ui");
        Assert.Null(form.Read("pnlMain", "Name"));
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("remove")]
    public void DifferentPluginSubtreeEditsRemainUserResolvable(string action)
    {
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        var ancestor = Select(Plugin("ui.ancestor", $$"""
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "{{action}}"
            control = "pnlMain"
            {{(action == "replace" ? "element = '<UiPanel Name=\"pnlMain\" />'" : "")}}
            """));
        var descendant = Select(Plugin("ui.descendant", """
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "patch"
            control = "pnlMain/lblTitle"
            set = { Text = "Chosen child" }
            """));
        var module = new BubbleFighterModule();
        var report = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), [ancestor, descendant])).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal("resource.conflict.parent-child", conflict.Code);
        using var prepared = module.PrepareResources(Context() with
        { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "ui.descendant")] }, [ancestor, descendant]);
        var form = UiDocument.Load(Member(Assert.Single(prepared.Files), "Ui/Test.ui"), "Ui/Test.ui");
        Assert.Equal("Chosen child", form.Read("pnlMain/lblTitle", "Text"));
    }
}
