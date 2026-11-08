using System.Text;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceEditUiTests
{
    internal const string Form = "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<UiForm Name=\"Test\" Width=\"800\">\r\n"
        + "  <UiPanel Name=\"pnlMain\" X=\"0\" Y=\"0\" Width=\"400\" Height=\"300\">\r\n"
        + "    <UiLabel Name=\"lblTitle\" X=\"10\" Y=\"10\" Text=\"Title\" TranslateID=\"T_TITLE\" />\r\n"
        + "    <UiLabel Name=\"lblAlertStr\" ArrayIndex=\"0\" Text=\"first\" />\r\n"
        + "    <UiLabel Name=\"lblAlertStr\" ArrayIndex=\"1\" Text=\"second\" />\r\n"
        + "    <Children>\r\n      <UiButton Name=\"btnOk\" X=\"20\" Y=\"200\" Text=\"OK\" />\r\n    </Children>\r\n"
        + "  </UiPanel>\r\n</UiForm>\r\n";

    private static UiDocument Load() => UiDocument.Load(ResourceEditTextTests.Utf16(Form), "Ui/Test.ui");
    private static UiEdit Edit(EditAction action, string path, Dictionary<string, string>? set = null, string? element = null, int? position = null) =>
        new(action, path, set ?? [], new Dictionary<string, string>(), element, position);

    [Fact]
    public void PatchChangesOnlyNamedAttributesAndKeepsBomDeclarationAndLayout()
    {
        var form = Load();
        form.Apply(Edit(EditAction.Patch, "pnlMain/lblAlertStr[1]", new() { ["Text"] = "두 번째" }));
        var saved = form.Save();
        Assert.Equal(new byte[] { 0xFF, 0xFE }, saved[..2]);
        var text = Encoding.Unicode.GetString(saved, 2, saved.Length - 2);
        Assert.Equal(Form.Replace("Text=\"second\"", "Text=\"두 번째\"", StringComparison.Ordinal), text);
    }

    [Fact]
    public void AddRemoveAndReplaceUseNativeTypesAndStablePaths()
    {
        var form = Load();
        form.Apply(Edit(EditAction.Add, "pnlMain/btnCancel", element: "<UiButton Name=\"btnCancel\" X=\"120\" Y=\"200\" Text=\"Cancel\" />", position: 1));
        form.Apply(Edit(EditAction.Remove, "pnlMain/lblAlertStr[0]"));
        form.Apply(Edit(EditAction.Replace, "pnlMain/lblTitle", element: "<UiLabel Name=\"lblTitle\" X=\"5\" Y=\"5\" Text=\"New\" />"));
        var panel = Assert.Single(form.Controls());
        Assert.Equal(["pnlMain/lblTitle", "pnlMain/btnCancel", "pnlMain/lblAlertStr[1]", "pnlMain/btnOk"], panel.Children.Select(c => c.Path));
        Assert.Equal("UiButton", panel.Children[1].Type);
        Assert.Equal("5", panel.Children[0].Attributes.Single(a => a.Key == "X").Value);
        Assert.DoesNotContain(panel.Children[0].Attributes, a => a.Key == "TranslateID");
    }

    [Fact]
    public void AmbiguousForeignAndIdentityChangingEditsAreRejected()
    {
        var form = Load();
        Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Patch, "pnlMain/lblAlertStr", new() { ["Text"] = "x" })));
        Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Add, "pnlMain/x", element: "<PluginWidget Name=\"x\" />")));
        Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Add, "pnlMain/btnOk", element: "<UiButton Name=\"btnOk\" />")));
        Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Patch, "pnlMain/btnOk", new() { ["Name"] = "renamed" })));
        Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Replace, "pnlMain/btnOk", element: "<UiButton Name=\"other\" />")));
        Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Patch, "pnlMain/missing", new() { ["Text"] = "x" })));
    }

    [Fact]
    public void SameNamedAndUnnamedSiblingsAreAddressedByOccurrence()
    {
        // Over half of the kr-812 forms have same-named siblings without ArrayIndex, and some controls have an empty Name.
        const string form = "<UiForm Name=\"F\">\r\n  <UiPanel Name=\"pnl\">\r\n    <UiLabel Name=\"lbl\" Text=\"a\" />\r\n"
            + "    <UiLabel Name=\"lbl\" Text=\"b\" />\r\n    <UiLabel Name=\"\" Text=\"c\" />\r\n    <UiLabel Name=\"lbl\" Text=\"d\" />\r\n  </UiPanel>\r\n</UiForm>\r\n";
        var document = UiDocument.Load(ResourceEditTextTests.Utf16(form), "Ui/F.ui");
        Assert.Equal(["pnl", "pnl/lbl@0", "pnl/lbl@1", "pnl/@0", "pnl/lbl@2"], document.Paths());
        Assert.Equal(("b", "c"), (document.Read("pnl/lbl@1", "Text"), document.Read("pnl/@0", "Text")));
        var ambiguous = Assert.Throws<InvalidDataException>(() => document.Read("pnl/lbl", "Text"));
        Assert.Contains("lbl@0 to lbl@2", ambiguous.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => document.Read("pnl@0", "Text"));
        Assert.Throws<InvalidDataException>(() => document.Read("pnl/", "Text"));
        document.Apply(Edit(EditAction.Patch, "pnl/lbl@2", new() { ["Text"] = "D" }));
        // Removing @0 and @1 together resolves both first, so @1 still means the second label.
        document.RemoveAll([Edit(EditAction.Remove, "pnl/lbl@0"), Edit(EditAction.Remove, "pnl/lbl@1")]);
        var labels = Assert.Single(document.Controls()).Children;
        Assert.Equal([("pnl/@0", "c"), ("pnl/lbl", "D")], labels.Select(c => (c.Path, c.Attributes.Single(a => a.Key == "Text").Value)));
    }

    [Fact]
    public void HideIsReportedAsUnsupportedInsteadOfFaked()
    {
        var form = Load();
        var error = Assert.Throws<InvalidDataException>(() => form.Apply(Edit(EditAction.Hide, "pnlMain/btnOk")));
        Assert.StartsWith("resource.ui.hide-unsupported", error.Message, StringComparison.Ordinal);
        Assert.Equal(Form, Encoding.Unicode.GetString(form.Save()[2..]));
    }

    [Fact]
    public void PinnedDescendantOfReplacedSubtreeCannotReceiveInvisibleWrites()
    {
        var form = Load();
        var patch = Edit(EditAction.Patch, "pnlMain/lblTitle", new() { ["Text"] = "Must not disappear" });
        form.Prepare([patch]);
        form.Apply(Edit(EditAction.Replace, "pnlMain", element: "<UiPanel Name=\"pnlMain\"><UiLabel Name=\"lblTitle\" Text=\"New\" /></UiPanel>"));
        Assert.Throws<InvalidDataException>(() => form.Apply(patch));
        Assert.DoesNotContain("Must not disappear", Encoding.Unicode.GetString(form.Save()[2..]), StringComparison.Ordinal);
    }

    [Fact]
    public void ControlTreeIsReadableForClientPlugins()
    {
        var tree = new UiForms().Parse(ResourceEditTextTests.Utf16(Form), "Ui/Test.ui");
        var panel = Assert.Single(tree);
        Assert.Equal("pnlMain", panel.Path);
        var button = panel.Children.Single(c => c.Name == "btnOk");
        Assert.Equal("pnlMain/btnOk", button.Path);
        Assert.Equal([("Name", "btnOk"), ("X", "20"), ("Y", "200"), ("Text", "OK")], button.Attributes.Select(a => (a.Key, a.Value)));
        Assert.Equal(1, panel.Children.Single(c => c.Path == "pnlMain/lblAlertStr[1]").ArrayIndex);
    }

    [Theory]
    [InlineData("Add", "pnlMain/newLabel", "newLabel")]
    [InlineData("Replace", "pnlMain/lblTitle", "lblTitle")]
    public void FragmentAttributesApplyToTheInsertedOrReplacedControl(string action, string path, string name)
    {
        var form = Load();
        form.Apply(Edit(Enum.Parse<EditAction>(action), path, new() { ["Text"] = "翻译 / 한국어" }, "<UiLabel Name=\"" + name + "\" Text=\"Original\" />"));
        Assert.Equal("翻译 / 한국어", form.Read(path, "Text"));
    }
}
