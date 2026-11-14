using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class GameNativeUiTests
{
    /// <summary>A form and native control name are sufficient to set text without knowing the previous value.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("#爆破模式#房间")]
    public void KnownControlsAcceptDirectTextAssignment(string text)
    {
        new GameNativeUiRequest("set", "control.setText", text)
        {
            Target = new() { Form = "Ui/DlgCreateRoom.ui", Name = "edtRoomTitle" },
        }.Validate();
    }

    /// <summary>Automatic name or text lookup is available independently of a known .ui resource.</summary>
    [Fact]
    public void AutomaticSelectorsDoNotRequireAFormOrAnOldValue()
    {
        new GameNativeUiRequest("inspect", "control.inspect", "").Validate();
        new GameNativeUiRequest("set", "control.setText", "New title") { Target = new() { Name = "edtRoomTitle" } }.Validate();
        new GameNativeUiRequest("replace", "control.setText", "New caption") { Target = new() { Text = "Old caption" } }.Validate();
        new GameNativeUiRequest("handle", "control.setText", "New caption") { Target = new() { Id = "control-7" } }.Validate();
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("empty", "control.setText", "value") { Target = new() }.Validate());
    }

    /// <summary>Form selectors name resources rather than local filesystem paths or traversal expressions.</summary>
    [Theory]
    [InlineData("../Ui/DlgCreateRoom.ui")]
    [InlineData("Ui/../DlgCreateRoom.ui")]
    [InlineData("D:/client/Ui/DlgCreateRoom.ui")]
    [InlineData("/Ui/DlgCreateRoom.ui")]
    [InlineData("Ui\\DlgCreateRoom.ui")]
    [InlineData("Ui//DlgCreateRoom.ui")]
    [InlineData("Ui/DlgCreateRoom.xml")]
    public void InvalidFormResourcesAreRejected(string form) =>
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("read", "control.inspect", "") { Target = new() { Form = form } }.Validate());

    /// <summary>Control settings cannot be smuggled into notifications or unsupported control operations.</summary>
    [Fact]
    public void ControlsKeepTheirOwnParametersAndExplicitEditLimits()
    {
        var target = new GameNativeUiControlTarget { Name = "edtRoomTitle" };
        var set = new GameNativeUiRequest("set", "control.setText", "Full value") { Target = target, MaximumLength = 80 };
        set.Validate();
        Assert.Throws<ArgumentException>(() => (set with { MaximumLength = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (set with { Kind = "control.inspect", Text = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (set with { Title = "presentation only" }).Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("notice", "notification", "body") { Target = target }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("events", "control.events", "") { AfterSequence = -1 }.Validate());
        new GameNativeUiRequest("events", "control.events", "") { AfterSequence = 17 }.Validate();
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("events", "control.events", "") { Target = target }.Validate());
    }

    /// <summary>Native button creation needs a resolved parent and a valid rectangle, independently of the mode feature.</summary>
    [Fact]
    public void NativeButtonDefinitionsAreGenericAndBoundedByTheirCoordinates()
    {
        var add = new GameNativeUiRequest("add", "control.addButton", "Mode Extension")
        {
            Target = new() { Form = "Ui/DlgCreateRoom.ui" },
            Definition = new("mode-extension", 12, 220, 112, 27),
        };
        add.Validate();
        Assert.Throws<ArgumentException>(() => (add with { Definition = null }).Validate());
        Assert.Throws<ArgumentException>(() => (add with { Definition = new("button", int.MaxValue, 0, 2, 27) }).Validate());
        Assert.Throws<ArgumentException>(() => (add with { Definition = new("button", 0, 0, 0, 27) }).Validate());
        Assert.Throws<ArgumentException>(() => (add with { Kind = "control.setText" }).Validate());
    }

    /// <summary>Control transport preserves the explicit selector, Unicode assignment and caller-selected edit limit.</summary>
    [Fact]
    public async Task ControlAssignmentPreservesItsWireFieldsAndReturnedText()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("set", "control.setText", "#爆破模式#原房间")
        {
            Target = new() { Form = "Ui/DlgCreateRoom.ui", Name = "edtRoomTitle" }, MaximumLength = 80,
        };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("ok", """
            {"operationId":"set","status":"completed","controls":[{"id":"control-1","form":"Ui/DlgCreateRoom.ui","name":"edtRoomTitle","kind":"edit","text":"#爆破模式#原房间","writable":true,"maximumLength":80}]}
            """);
        Assert.Contains("maximum_length = 80", payload, StringComparison.Ordinal);
        Assert.Contains("[target]", payload, StringComparison.Ordinal);
        Assert.Contains("form = \"Ui/DlgCreateRoom.ui\"", payload, StringComparison.Ordinal);
        Assert.Contains("name = \"edtRoomTitle\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("icon =", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("title =", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("chat_style =", payload, StringComparison.Ordinal);
        var result = await operation;
        Assert.Equal("completed", result.Status);
        Assert.Equal(request.Text, Assert.Single(result.Controls).Text);
        Assert.Equal(80, result.Controls[0].MaximumLength);
    }

    /// <summary>Trusted package scopes are transported outside page-supplied request fields and reused for result polling.</summary>
    [Fact]
    public async Task ControlOwnersAreScopedAfterCapabilityNegotiation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var client = peer.Client.ForOwner("package.one");
        var operation = client.InvokeAsync(new("inspect", "control.inspect", "")).AsTask();
        var capabilities = await peer.ReplyAsync("ok", """
            {"build":"fixture","operations":[{"kind":"control.inspect","supported":true,"ready":true,"features":[]}]}
            """);
        Assert.DoesNotContain("owner_scope", capabilities, StringComparison.Ordinal);
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"inspect\",\"status\":\"queued\"}");
        Assert.Contains("owner_scope = \"package.one\"", payload, StringComparison.Ordinal);
        Assert.Equal("queued", (await operation).Status);
        var poll = client.GetResultAsync("inspect").AsTask();
        payload = await peer.ReplyAsync("ok", "{\"operationId\":\"inspect\",\"status\":\"completed\",\"controls\":[]}");
        Assert.Contains("owner_scope = \"package.one\"", payload, StringComparison.Ordinal);
        Assert.Equal("completed", (await poll).Status);
    }

    /// <summary>Older native providers keep receiving their original presentation schema when they do not advertise control operations.</summary>
    [Fact]
    public async Task ScopedPresentationsRemainCompatibleWithOlderProviders()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var client = peer.Client.ForOwner("package.old");
        var operation = client.InvokeAsync(new("notice", "notification", "text")).AsTask();
        await peer.ReplyAsync("ok", """
            {"build":"fixture","operations":[{"kind":"notification","supported":true,"ready":true,"features":["text"]}]}
            """);
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"notice\",\"status\":\"shown\"}");
        Assert.DoesNotContain("owner_scope", payload, StringComparison.Ordinal);
        Assert.Equal("shown", (await operation).Status);
    }

    /// <summary>Repeated native names are selected by array index, including zero, without using the previous caption.</summary>
    [Fact]
    public async Task NativeActivationPreservesArrayIndicesAndSelectionReadback()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("activate", "control.activate", "")
        { Target = new() { Form = "Ui/DlgCreateRoom.ui", Name = "btnGameMode", ArrayIndex = 0 } };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("ok", """
            {"operationId":"activate","status":"completed","reason":"alreadySelected","controls":[{"id":"control-1","form":"Ui/DlgCreateRoom.ui","name":"btnGameMode","kind":"button","text":"Mode","writable":true,"arrayIndex":0,"enabled":false,"selectionValue":1,"selected":true}]}
            """);
        Assert.Contains("array_index = 0", payload, StringComparison.Ordinal);
        var selected = Assert.Single((await operation).Controls);
        Assert.Equal(0, selected.ArrayIndex);
        Assert.Equal(1, selected.SelectionValue);
        Assert.True(selected.Selected);
        Assert.False(selected.Enabled);
        Assert.Throws<ArgumentException>(() => new GameNativeUiControlTarget { ArrayIndex = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiControlTarget { Name = "btnGameMode", ArrayIndex = -1 }.Validate());
    }

    /// <summary>Read, removal and activation requests omit even empty text because the native schema rejects its presence.</summary>
    [Theory]
    [InlineData("control.inspect")]
    [InlineData("control.events")]
    [InlineData("control.remove")]
    [InlineData("control.activate")]
    public async Task ControlRequestsMatchTheNativeTopLevelSchema(string kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var target = kind is "control.remove" or "control.activate" ? new GameNativeUiControlTarget { Id = "control-1" } : null;
        var operation = peer.Client.InvokeAsync(new("schema", kind, "") { Target = target }).AsTask();
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"schema\",\"status\":\"completed\"}");
        var expected = $"action = \"invoke\"\noperation_id = \"schema\"\nkind = \"{kind}\"\n";
        if (target is not null) expected += "[target]\nid = \"control-1\"\n";
        Assert.Equal(expected, payload);
        Assert.Equal("completed", (await operation).Status);
    }
}
