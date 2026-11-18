using System.Text.Json;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class OverlayUiTests
{
    [Fact]
    public void ConfirmationUsesGameUiAndReturnsOnlyActionEvents()
    {
        using var host = new FakeHost();
        string? result = null;
        using var panel = host.ShowConfirmation("test.confirm", "Title", "Continue?", "Yes", "No",
            e => result = e.Data.GetProperty("name").GetString());
        Assert.True(host.Descriptor!.Interactive);
        Assert.True(host.Descriptor.Modal);
        Assert.False(host.Panel.Shown);
        Assert.Equal("game-ui.message-box", host.Panel.EventName);
        var payload = JsonSerializer.SerializeToElement(host.Panel.Payload);
        Assert.Equal("Continue?", payload.GetProperty("text").GetString());
        Assert.Equal("confirm", payload.GetProperty("buttons")[0].GetProperty("name").GetString());
        host.Panel.Emit("game-ui.mounted", "{}");
        Assert.Null(result);
        host.Panel.Emit("game-ui.action", "{\"name\":\"confirm\"}");
        Assert.Equal("confirm", result);
    }

    [Fact]
    public void ComponentRegistrationIsHiddenAndReleasesFailedDialogs()
    {
        using var host = new FakeHost();
        using var panel = host.CreateGameUiPanel("test.hud");
        Assert.False(host.Panel.Shown);
        Assert.False(host.Descriptor!.Interactive);
        Assert.Contains("data-game-ui-root", host.Descriptor.InlineHtml);
        host.Panel.FailSend = true;
        Assert.Throws<IOException>(() => host.ShowMessageBox("test.dialog", "Title", "Text", "OK"));
        Assert.True(host.Panel.Disposed);
        Assert.Throws<ArgumentException>(() => host.ShowDialog("test.invalid", "Title", "Text", []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogOutsideClickPolicyIsIndependentOfModalPriority(bool passThrough)
    {
        using var host = new FakeHost();
        using var panel = host.ShowMessageBox("test.input", "Title", "Text", passThroughOutside: passThrough);
        Assert.True(host.Descriptor!.Modal);
        Assert.True(host.Descriptor.Interactive);
        Assert.Equal(passThrough, host.Descriptor.PassThroughOutside);
        panel.SetPassThroughOutside(!passThrough);
        Assert.Equal("game-ui.configure", host.Panel.EventName);
        Assert.Equal(!passThrough, JsonSerializer.SerializeToElement(host.Panel.Payload).GetProperty("passThroughOutside").GetBoolean());
    }

    [Fact]
    public void NativeEventsAreBoundedAndOverflowIsExplicit()
    {
        var queue = new OverlayEventQueue();
        using var doc = JsonDocument.Parse("{\"name\":\"ok\"}");
        for (var i = 0; i < 33; i++) queue.Add("game-ui.action", doc.RootElement);
        Assert.Contains("overlay.events-overflow", queue.Take());
        Assert.Contains("game-ui.action", queue.Take());
        Assert.Null(queue.Take());
        queue.Add("game-ui.action", JsonSerializer.SerializeToElement(new string('x', 5000)));
        Assert.Contains("overlay.event-too-large", queue.Take());
    }

    [Fact]
    public void AnchorsKeepSourceCoordinatesAndRejectPartialReferenceViewports()
    {
        using var host = new FakeHost();
        using var panel = host.CreateGameUiPanel("test.anchor");
        panel.SetAnchor(new OverlayAnchor(300, 100, 200, 50)
        {
            ReferenceWidth = 1024, ReferenceHeight = 768, Placement = OverlayAnchorPlacement.Right, Visible = false,
        });
        var payload = JsonSerializer.SerializeToElement(host.Panel.Payload);
        Assert.Equal("game-ui.anchor", host.Panel.EventName);
        Assert.Equal("right", payload.GetProperty("placement").GetString());
        Assert.False(payload.GetProperty("visible").GetBoolean());
        Assert.Throws<ArgumentException>(() => panel.SetAnchor(new OverlayAnchor(0, 0, 10, 10) { ReferenceWidth = 1024 }));
        panel.SetAnchor(null);
        Assert.Null(host.Panel.Payload);
    }

    [Fact]
    public void BuildProfilesSelectThemesCapabilitiesAndLocatorData()
    {
        var directory = Directory.CreateTempSubdirectory("ft-overlay-profile-");
        var file = Path.Combine(directory.FullName, "kr-1.toml");
        const string content = """
            schema = 1
            [build]
            id = "kr-1"
            game = "test"
            branch = "kr"

            protocol_version = 1
            executable = "test.exe"
            name = { en = "Test" }
            [launch]
            login = "unverified"
            [overlay]
            theme = "v3"
            capabilities = ["room.mode.extra"]
            [[overlay.targets]]
            id = "room.create"
            locator = "ui-resource"
            resource = "Ui/CreateRoom.ui"
            control = "CreateRoom"
            parent_target = "room.shell"
            [[overlay.targets]]
            id = "room.shell"
            locator = "hook"
            """;
        try
        {
            File.WriteAllText(file, content);
            var profile = GameCatalog.LoadBuild(file, "test").Overlay!;
            Assert.Equal("v3", profile.Theme);
            Assert.Contains("room.mode.extra", profile.Capabilities);
            Assert.Equal("room.shell", profile.Targets.Single(t => t.Id == "room.create").ParentTarget);
            File.WriteAllText(file, content.Replace("parent_target = \"room.shell\"", "parent_target = \"room.create\"", StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => GameCatalog.LoadBuild(file, "test"));
            File.WriteAllText(file, content.Replace("parent_target = \"room.shell\"", "parent_target = \"missing\"", StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => GameCatalog.LoadBuild(file, "test"));
            File.WriteAllText(file, content.Replace("theme = \"v3\"", "theme = \"v4\"", StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => GameCatalog.LoadBuild(file, "test"));
            File.WriteAllText(file, content.Replace("Ui/CreateRoom.ui", "../CreateRoom.ui", StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => GameCatalog.LoadBuild(file, "test"));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void UiResourceLayoutsResolveNestedControlsIntoSharedAnchors()
    {
        const string resource = """
            <ui designWidth="1024" designHeight="768">
              <panel id="room" x="100" y="40" width="500" height="300">
                <button id="create" x="20" y="30" width="120" height="40" />
              </panel>
            </ui>
            """;
        var document = OverlayUiResourceParser.Parse(resource);
        var anchor = OverlayUiResourceAnchors.Resolve(document, "create", OverlayAnchorPlacement.Right, 8);
        Assert.Equal(120, anchor.X);
        Assert.Equal(70, anchor.Y);
        Assert.Equal(1024, anchor.ReferenceWidth);
        Assert.Equal(768, anchor.ReferenceHeight);
        Assert.Equal(OverlayAnchorPlacement.Right, anchor.Placement);
        Assert.Throws<KeyNotFoundException>(() => OverlayUiResourceAnchors.Resolve(document, "missing"));
    }

    [Fact]
    public void UiResourceJsonRejectsDuplicateControlsAndSupportsHiddenControls()
    {
        const string resource = """
            { "width": 800, "height": 600, "children": [
              { "id": "hidden", "x": 1, "y": 2, "width": 3, "height": 4, "visible": false },
              { "id": "ok", "x": 5, "y": 6, "width": 7, "height": 8 }
            ] }
            """;
        var document = OverlayUiResourceParser.Parse(resource);
        Assert.True(document.TryGetNode("hidden", out _));
        Assert.False(OverlayUiResourceAnchors.Resolve(document, "hidden").Visible);
        Assert.True(document.TryGetNode("ok", out _));
        Assert.Throws<OverlayUiResourceFormatException>(() => OverlayUiResourceParser.Parse("{\"children\":[{\"id\":\"x\",\"width\":1,\"height\":1},{\"id\":\"x\",\"width\":1,\"height\":1}]}"));
    }

    [Fact]
    public void UiResourceProviderPublishesInitialAndReloadedCoordinates()
    {
        var first = OverlayUiResourceParser.Parse("{\"width\":100,\"height\":100,\"children\":[{\"id\":\"button\",\"x\":1,\"y\":2,\"width\":10,\"height\":20}]}");
        using var provider = new OverlayUiResourceAnchorProvider(first);
        var values = new List<OverlayAnchor>();
        using var subscription = provider.Subscribe("button", values.Add);
        var second = OverlayUiResourceParser.Parse("{\"width\":100,\"height\":100,\"children\":[{\"id\":\"button\",\"x\":3,\"y\":4,\"width\":10,\"height\":20}]}");
        provider.Update(second);
        Assert.Equal(2, values.Count);
        Assert.Equal(3, values[^1].X);
        provider.Update(OverlayUiResourceParser.Parse("{\"children\":[{\"id\":\"other\",\"width\":10,\"height\":20}]}"));
        Assert.False(values[^1].Visible);
        subscription.Dispose();
        provider.Update(first);
        Assert.Equal(3, values.Count);
    }

    [Theory]
    [InlineData("<ui width='1024'><control id='a' width='20' height='30'/></ui>")]
    [InlineData("<ui><control id='a' x='invalid' width='20' height='30'/></ui>")]
    [InlineData("<!DOCTYPE ui [<!ENTITY test 'x'>]><ui><control id='a' width='20' height='30'/></ui>")]
    [InlineData("{\"children\":[{\"id\":\"a\",\"x\":\"invalid\",\"width\":20,\"height\":30}]}")]
    [InlineData("{\"children\":[{\"id\":\"a\",\"visible\":\"false\",\"width\":20,\"height\":30}]}")]
    [InlineData("{\"children\":[{\"id\":\"a\",\"width\":20,\"height\":30,\"children\":{}}]}")]
    public void UiResourceAdaptersRejectMalformedGeometry(string content) =>
        Assert.Throws<OverlayUiResourceFormatException>(() => OverlayUiResourceParser.Parse(content));

    private static OverlayPresentation Report(string surfaces) => OverlayPresentation.FromState(JsonDocument.Parse(
        "{\"type\":\"presentation-state\",\"visible\":true,\"viewport\":{\"width\":1000,\"height\":800},\"surfaces\":[" + surfaces + "]}").RootElement);

    [Fact]
    public void PresentationReadsThePageReport()
    {
        var presentation = Report("{\"x\":10,\"y\":20,\"width\":30,\"height\":40,\"modal\":true,\"backdrop\":false}");
        Assert.True(presentation.Visible);
        Assert.Equal(1000, presentation.ViewportWidth);
        Assert.Equal(new OverlaySurface(10, 20, 30, 40, true, false), Assert.Single(presentation.Surfaces));
        Assert.False(OverlayPresentation.Hidden.CoversNativeNotice());
    }

    [Theory]
    // A modal or a scrim hides the native box wherever it sits.
    [InlineData("{\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"modal\":true,\"backdrop\":false}", true)]
    [InlineData("{\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"modal\":false,\"backdrop\":true}", true)]
    // A small button in a corner leaves the middle band (200..800 x 160..640) free.
    [InlineData("{\"x\":166,\"y\":102,\"width\":56,\"height\":23,\"modal\":false,\"backdrop\":false}", false)]
    // Touching the band's edge is not covering; reaching into it is.
    [InlineData("{\"x\":800,\"y\":300,\"width\":50,\"height\":50,\"modal\":false,\"backdrop\":false}", false)]
    [InlineData("{\"x\":790,\"y\":300,\"width\":50,\"height\":50,\"modal\":false,\"backdrop\":false}", true)]
    [InlineData("", false)]
    public void NativeNoticeIsCoveredByModalsScrimsAndTheMiddleBand(string surfaces, bool covered) =>
        Assert.Equal(covered, Report(surfaces).CoversNativeNotice());

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"viewport\":{\"width\":\"wide\",\"height\":1}}")]
    [InlineData("{\"surfaces\":[{\"x\":1,\"y\":1,\"width\":1}]}")]
    public void MalformedPresentationReportsAreRejected(string json) =>
        Assert.Throws<FormatException>(() => OverlayPresentation.FromState(JsonDocument.Parse(json).RootElement));

    [Fact]
    public async Task NotifyBoxIsRequestedOnAHiddenPanelAndReleasedWhenItCloses()
    {
        using var host = new FakeHost();
        var shown = host.ShowNotifyBoxAsync("test.notice", "Friend is not online.", TimeSpan.FromSeconds(2));
        Assert.False(host.Descriptor!.Interactive);
        Assert.False(host.Panel.Shown);
        Assert.Equal("game-ui.notify-box", host.Panel.EventName);
        var payload = JsonSerializer.SerializeToElement(host.Panel.Payload);
        Assert.Equal("Friend is not online.", payload.GetProperty("text").GetString());
        Assert.Equal(2000, payload.GetProperty("durationMs").GetDouble());
        Assert.False(shown.IsCompleted);
        host.Panel.Emit("game-ui.close", "{}");
        await shown;
        Assert.True(host.Panel.Disposed);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.ShowNotifyBoxAsync("test.long", "x", TimeSpan.FromSeconds(30)));
    }

    private sealed class FakeHost : IOverlayHost, IDisposable
    {
        internal OverlayPanelDescriptor? Descriptor;
        internal readonly FakePanel Panel = new();
        public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor) { Descriptor = descriptor; return Panel; }
        public void Dispose() => Panel.Dispose();
    }

    private sealed class FakePanel : IOverlayPanel
    {
        internal bool Shown, Disposed, FailSend;
        internal string? EventName;
        internal object? Payload;
        public string PanelId => "test.panel";
        public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
        public void Show() => Shown = true;
        public void Hide() => Shown = false;
        public void Send(string eventName, object? data = null)
        {
            if (FailSend) throw new IOException("Test transport failure.");
            EventName = eventName; Payload = data;
        }
        public void MountGameUi(string component, object? props = null) => Send("game-ui.mount", new { component, props });
        public void Dispose() => Disposed = true;
        internal void Emit(string name, string json)
        {
            using var data = JsonDocument.Parse(json);
            MessageReceived?.Invoke(this, new OverlayMessageEventArgs(PanelId, name, data.RootElement.Clone()));
        }
    }
}
