using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ModeExtensionDataTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ft-mode-data-").FullName;

    public ModeExtensionDataTests()
    {
        Write("mode-catalog.json", """
            {"format":1,"modes":[
              {"id":1,"key":"Pvp","names":{"en":"No Item","zh-CN":"竞技模式"},"tokens":{"en":"NoItem","zh-CN":"竞技模式"},"aliases":["Pvp"],"nativeNames":{"en":"No Item"}},
              {"id":3,"key":"Item","names":{"en":"Item","zh-CN":"道具模式"},"tokens":{"en":"Item","zh-CN":"道具模式"},"aliases":[]},
              {"id":18,"key":"Bomb","names":{"en":"Bomb Mode","zh-CN":"爆破模式","ko":"폭파전"},"tokens":{"en":"BombMode","zh-CN":"爆破模式","ko":"폭파전"},"aliases":["Bomb"]}
            ]}
            """);
        Write("mode-availability.json", """{"format":1,"rules":[]}""");
        Write("room-bindings.json", """
            {"format":1,"builds":{"kr-812":{
              "form":"Ui/DlgCreateRoom.ui","roomName":"edtRoomTitle","modeButton":"btnGameMode",
              "descriptionLabel":"lblGameModeDescription","createButton":"btnCreate","cancelButton":"btnCancel",
              "entry":{"x":5,"y":623,"width":141,"height":27}
            }}}
            """);
        Write("controller.json", """
            {"format":1,"pollMs":500,"operationTimeoutMs":2000,"resultPollMs":50,"startupTimeoutMs":30000,"maxPendingActions":16,
             "selectedCaption":{"en":" (Selected: {mode})","zh-CN":"（已选择{mode}）","ko":" ({mode} 선택됨)"}}
            """);
    }

    [Fact]
    public void RealPackageRetainsCompleteCatalogueAndSupportedBinding()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var plugin = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
        var data = ModeExtensionData.Load(plugin, Build(), "zh-CN");
        Assert.Contains(plugin.Requires, requirement => requirement.Id == "bubble.fighter.fantasy.nirinsoulink");
        Assert.Equal(4, plugin.CompanionMessages.Count);
        foreach (var type in new[] { "game.room.create.context", "game.room.create.context.get", "game.session.changed", "game.session.state" })
            Assert.Equal(1, plugin.CompanionMessages[type]);
        Assert.Equal(47, data.Modes.Count);
        Assert.Equal(data.AllModes, data.Modes);
        Assert.Equal("Ui/DlgCreateRoom.ui", data.Bindings.Single().Form);
        Assert.Equal("模式扩展", data.Title);
        Assert.Equal("（已选择{mode}）", data.SelectedCaption);
        Assert.Equal((150, 2000, 50, 30000, 16), (data.PollMs, data.OperationTimeoutMs, data.ResultPollMs, data.StartupTimeoutMs, data.MaxPendingActions));
    }

    [Fact]
    public void RealPackageBindsTheKr1256DialogByItsOwnResource()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("kr-1256"), "en");
        // kr-1256 sub_5C5110 loads DlgCreateRoom from this resource; its controls keep the kr-812 names.
        Assert.Equal("Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 278,480 and 34 pixels high) without covering it.
        Assert.Equal(new RoomEntry(8, 480, 180, 34), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 278);
    }

    [Fact]
    public void RealPackageBindsTheCn406DialogItsLanguageLoads()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-406"), "zh-CN");
        // cn-406 sub_76D710 loads this resource when cn-406 sub_946E80 enables features 11, 13 and 62 (language 2).
        Assert.Equal("Ui/DlgCreateRoom8.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 152,526 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 526, 139, 27), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 152);
    }

    [Fact]
    public void RealPackageBindsTheCn774DialogByItsOwnResource()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-774"), "zh-CN");
        // cn-774 sub_6B5370, the only DlgCreateRoom constructor, loads this resource whatever the language.
        Assert.Equal("Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 278,473 and 33 pixels high) without covering it.
        Assert.Equal(new RoomEntry(8, 473, 180, 33), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 278);
    }

    [Fact]
    public void RealPackageBindsTheCn324DialogItsLanguageSelects()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-324"), "zh-CN");
        // cn-324 Config/Lambient.xml selects language 2; cn-324 sub_8B5380 enables feature 10 for it, so
        // cn-324 sub_7407D0 loads Ui/DlgCreateRoom5.ui.
        Assert.Equal("Ui/DlgCreateRoom5.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 290,478 and 25 pixels high) without covering it.
        Assert.Equal(new RoomEntry(8, 478, 180, 25), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 290);
    }

    [Fact]
    public void RealPackageBindsTheCn279DialogByItsOwnResource()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-279"), "zh-CN");
        // cn-279 sub_6EC9D0, the only DlgCreateRoom constructor, loads this resource whatever the language.
        Assert.Equal("Ui/DlgCreateRoom2.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 288,386 and 25 pixels high) without covering it.
        Assert.Equal(new RoomEntry(8, 386, 180, 25), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 288);
    }

    [Fact]
    public void RealPackageBindsTheCn404DialogItsFeatureSwitchSelects()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-404"), "zh-CN");
        // cn-404 Config/Lambient.xml selects language 2, whose feature 87 makes cn-404 sub_823240 load this resource.
        Assert.Equal("Ui/DlgCreateRoom13.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 152,618 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 618, 139, 27), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 152);
    }

    [Fact]
    public void RealPackageBindsTheCn706DialogByItsOwnResource()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-706"), "zh-CN");
        // cn-706 sub_7F9B30, the only DlgCreateRoom constructor, loads this resource whatever the language.
        Assert.Equal("Ui/DlgCreateRoom.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 150,623 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 623, 137, 27), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 150);
    }

    [Fact]
    public void RealPackageBindsTheCn717DialogByItsOwnResource()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-717"), "zh-CN");
        // cn-717 sub_8F72A0, the only DlgCreateRoom constructor, loads this resource whatever the language.
        Assert.Equal("Ui/DlgCreateRoom.ui", data.Bindings.Single().Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (data.Bindings.Single().RoomName, data.Bindings.Single().ModeButton, data.Bindings.Single().DescriptionLabel, data.Bindings.Single().CreateButton));
        // The entry sits on the native button row (btnCreate is at 150,623 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 623, 137, 27), data.Bindings.Single().Entry);
        Assert.True(data.Bindings.Single().Entry.X + data.Bindings.Single().Entry.Width <= 150);
    }

    [Fact]
    public void RealPackageBindsTheId400DialogItsUiArchiveShips()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("id-400"), "id");
        // id-400 sub_462360 would load Ui/DlgCreateRoom13.ui under feature 87, but the id-400 Ui/Ui.bfz ships only this one.
        var binding = Assert.Single(data.Bindings);
        Assert.Equal("Ui/DlgCreateRoom12.ui", binding.Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (binding.RoomName, binding.ModeButton, binding.DescriptionLabel, binding.CreateButton));
        // The entry sits on the native button row (btnCreate is at 150,380 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 380, 139, 27), binding.Entry);
        Assert.True(binding.Entry.X + binding.Entry.Width <= 150);
    }

    [Fact]
    public void RealPackageBindsTheOnlyId1809Dialog()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("id-1809"), "id");
        // id-1809 sub_8136D0, the only DlgCreateRoom constructor, loads this resource unconditionally.
        var binding = Assert.Single(data.Bindings);
        Assert.Equal("Ui/DlgCreateRoom.ui", binding.Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (binding.RoomName, binding.ModeButton, binding.DescriptionLabel, binding.CreateButton));
        // The entry sits on the native button row (btnCreate is at 150,479 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 479, 139, 27), binding.Entry);
        Assert.True(binding.Entry.X + binding.Entry.Width <= 150);
    }

    [Fact]
    public void RealPackageBindsTheOnlyTw454Dialog()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("tw-454"), "zh-TW");
        // tw-454 sub_ADE670, the only DlgCreateRoom constructor, loads this resource unconditionally.
        var binding = Assert.Single(data.Bindings);
        Assert.Equal("Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui", binding.Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (binding.RoomName, binding.ModeButton, binding.DescriptionLabel, binding.CreateButton));
        // The entry sits on the native button row (btnCreate is at 289,591 and 33 pixels high) without covering it.
        Assert.Equal(new RoomEntry(8, 591, 180, 33), binding.Entry);
        Assert.True(binding.Entry.X + binding.Entry.Width <= 289);
    }

    [Fact]
    public void RealPackageBindsTheOnlyCn408Dialog()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-408"), "zh-CN");
        // cn-408 sub_827560, the only DlgCreateRoom constructor, loads this resource without a feature check.
        var binding = Assert.Single(data.Bindings);
        Assert.Equal("Ui/DlgCreateRoom.ui", binding.Form);
        Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
            (binding.RoomName, binding.ModeButton, binding.DescriptionLabel, binding.CreateButton));
        // The entry sits on the native button row (btnCreate is at 150,623 and 27 pixels high) without covering it.
        Assert.Equal(new RoomEntry(7, 623, 139, 27), binding.Entry);
        Assert.True(binding.Entry.X + binding.Entry.Width <= 150);
    }

    [Theory]
    [InlineData("#Bomb##3#Room #friends#", "Room #friends#", 18)]
    [InlineData("#Unknown#Room", "#Unknown#Room", null)]
    [InlineData("#No Item#Room", "#No Item#Room", null)]
    [InlineData("Room #8#", "Room", 8)]
    [InlineData("#Unknown#Bomb#Room", "#Unknown Room", 18)]
    [InlineData("#0#Room #Bomb#", "#0#Room", null)]
    [InlineData("#65536#Room #Bomb#", "#65536#Room", null)]
    [InlineData("#Bomb#Room #0#", "Room #0#", 18)]
    [InlineData("#００３#Room #Bomb#", "#００３#Room", null)]
    [InlineData("#𝟛#Room #Bomb#", "#𝟛#Room", null)]
    [InlineData("#00003#Room #Bomb#", "Room", 3)]
    [InlineData("#bOmBmOdE#Room", "Room", 18)]
    [InlineData("#爆破模式#Room", "Room", 18)]
    [InlineData("#폭파전#Room", "Room", 18)]
    [InlineData("#No\u0085Item#Room", "#No\u0085Item#Room", null)]
    [InlineData("#No\ufeffItem#Room", "#No\ufeffItem#Room", null)]
    public void RoomTagsPreserveNumericPrecedenceAndUnknownHashes(string text, string room, int? selected)
    {
        Assert.Equal((room, selected), Load().ParseTitle(text));
    }

    [Fact]
    public void MatchingAllowListsIntersectAndHiddenModesRemainRecognizable()
    {
        Write("mode-availability.json", """
            {"format":1,"rules":[
              {"builds":["kr-*"],"allow":[1,3,18]},
              {"builds":["KR-812"],"allow":[1,18],"hidden":[18]},
              {"builds":["tw-*"],"hidden":[1]}
            ]}
            """);
        var data = Load();
        Assert.Equal(1, Assert.Single(data.Modes).Id);
        Assert.Equal(3, data.AllModes.Count);
        Assert.Equal(("Room", (int?)18), data.ParseTitle("#Bomb#Room"));
    }

    [Fact]
    public void ExplicitEmptyAllowListDoesNotFallBackToAllModes()
    {
        Write("mode-availability.json", """{"format":1,"rules":[{"builds":["*"],"allow":[]}]}""");
        Assert.Empty(Load().Modes);
    }

    [Fact]
    public void RealPackageBindsBothCn735DialogsItsConstructorChoosesBetween()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.mode-extension");
        var data = ModeExtensionData.Load(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")), Build("cn-735"), "zh-CN");
        // cn-735 sub_A88A90 loads the survival resource when features 507 and 23 are on, and the default one otherwise.
        Assert.Equal(["Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui", "Ui/Stages/Lobby/DlgCreateRoom.ui"], data.Bindings.Select(binding => binding.Form));
        foreach (var binding in data.Bindings)
        {
            Assert.Equal(("edtRoomTitle", "btnGameMode", "lblGameModeDescription", "btnCreate"),
                (binding.RoomName, binding.ModeButton, binding.DescriptionLabel, binding.CreateButton));
            Assert.True(binding.Entry.X + binding.Entry.Width <= 308);
        }
        // Each entry sits on its resource's button row: btnCreate is at 308,535 and at 308,475, 27 pixels high.
        Assert.Equal([new RoomEntry(8, 535, 180, 27), new RoomEntry(8, 475, 180, 27)], data.Bindings.Select(binding => binding.Entry));
    }

    [Fact]
    public void ABuildMayBindSeveralResourcesInOrder()
    {
        Write("room-bindings.json", "{\"format\":1,\"builds\":{\"kr-812\":[" + Binding("Ui/DlgCreateRoom.ui") + "," + Binding("Ui/DlgCreateRoom2.ui") + "]}}");
        Assert.Equal(["Ui/DlgCreateRoom.ui", "Ui/DlgCreateRoom2.ui"], Load().Bindings.Select(binding => binding.Form));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeveralBindingsMustNameDistinctForms(int copies)
    {
        // An empty list, or two bindings of one resource, fail even for a build other than the one loaded.
        var row = string.Join(",", Enumerable.Repeat(Binding("Ui/DlgCreateRoom.ui"), copies));
        Write("room-bindings.json", "{\"format\":1,\"builds\":{\"cn-735\":[" + row + "]}}");
        var error = Assert.Throws<InvalidDataException>(() => Load());
        Assert.Contains("room-bindings.json: builds.cn-735", error.Message, StringComparison.Ordinal);
    }

    private static string Binding(string form) => "{\"form\":\"" + form + "\",\"roomName\":\"edtRoomTitle\",\"modeButton\":\"btnGameMode\","
        + "\"descriptionLabel\":\"lblGameModeDescription\",\"createButton\":\"btnCreate\",\"cancelButton\":\"btnCancel\","
        + "\"entry\":{\"x\":1,\"y\":2,\"width\":3,\"height\":4}}";

    [Fact]
    public void UnadaptedBuildDoesNotBorrowTheBaselineBinding()
    {
        var data = Load("kr-1256");
        Assert.Empty(data.Bindings);
        Assert.Equal(3, data.Modes.Count);
    }

    [Theory]
    [InlineData("ZH-cn", "zh-CN", "模式扩展", "（已选择{mode}）")]
    [InlineData("ko-KR", "ko", "Mode Extension", " ({mode} 선택됨)")]
    [InlineData("unknown", "en", "Mode Extension", " (Selected: {mode})")]
    public void LocaleResolutionMatchesTheDisplayedLocale(string locale, string expected, string title, string caption)
    {
        var data = Load(locale: locale);
        Assert.Equal(expected, data.Language);
        Assert.Equal(title, data.Title);
        Assert.Equal(caption, data.SelectedCaption);
    }

    [Theory]
    [InlineData("{\"format\":1,\"rules\":[{\"builds\":[\"tw-*\"],\"hidden\":[999]}]}")]
    [InlineData("{\"format\":1,\"rules\":[{\"builds\":[\"*\"],\"allow\":[1,1]}]}")]
    [InlineData("{\"format\":1,\"rules\":[{\"builds\":[\"kr-[812]\"],\"hidden\":[1]}]}")]
    [InlineData("{\"format\":1,\"rules\":[{\"builds\":[\"*\"]}]}")]
    [InlineData("{\"format\":1,\"rules\":[],\"rules\":[]}")]
    public void InvalidPolicyFailsEvenIfItsBuildDoesNotMatch(string json)
    {
        Write("mode-availability.json", json);
        var error = Assert.Throws<InvalidDataException>(() => Load());
        Assert.Contains("mode-availability.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"id\":3", "\"id\":1")]
    [InlineData("\"en\":\"Item\",\"zh-CN\":\"道具模式\"},\"aliases\"", "\"en\":\"NoItem\",\"zh-CN\":\"道具模式\"},\"aliases\"")]
    [InlineData("\"en\":\"BombMode\"", "\"en\":\"Bomb Mode\"")]
    [InlineData("\"en\":\"BombMode\"", "\"en\":\"Bomb#Mode\"")]
    [InlineData("\"en\":\"BombMode\"", "\"en\":\"Bomb\\u0085Mode\"")]
    public void InvalidCatalogueFailsBeforeNativeOperations(string before, string after)
    {
        Replace("mode-catalog.json", before, after);
        var error = Assert.Throws<InvalidDataException>(() => Load());
        Assert.Contains("mode-catalog.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"pollMs\":500", "\"pollMs\":0")]
    [InlineData("\"resultPollMs\":50", "\"resultPollMs\":2001")]
    [InlineData("\"startupTimeoutMs\":30000", "\"startupTimeoutMs\":-1")]
    [InlineData("\"maxPendingActions\":16", "\"maxPendingActions\":0")]
    [InlineData(" (Selected: {mode})", " (Selected)")]
    public void ControllerSettingsRejectInvalidLifetimesAndCaptions(string before, string after)
    {
        Replace("controller.json", before, after);
        var error = Assert.Throws<InvalidDataException>(() => Load());
        Assert.Contains("controller.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDataDoesNotCreateAnImplicitDefault()
    {
        File.Delete(Path.Combine(_folder, "controller.json"));
        Assert.Throws<InvalidDataException>(() => Load());
    }

    [Fact]
    public void SerializedModesRetainNativeVisualMetadata()
    {
        var element = JsonSerializer.SerializeToElement(Load().Modes[0], JsonSerializerOptions.Web);
        Assert.Equal("Pvp", element.GetProperty("key").GetString());
        Assert.Equal("No Item", element.GetProperty("nativeNames").GetProperty("en").GetString());
        Assert.False(element.TryGetProperty("metadata", out _));
    }

    private ModeExtensionData Load(string build = "kr-812", string? locale = "en") => ModeExtensionData.Load(Plugin(), Build(build), locale);
    private PluginDefinition Plugin() => new("bubblefighter.fantasy.mode-extension", "0.1.1", "bubblefighter", "overlay", "", _folder,
        new LocalizedText(new Dictionary<string, string> { ["en"] = "Mode Extension", ["zh-CN"] = "模式扩展" }), null,
        [], "MIT", "", ["*"], [], [], [], null, null);
    private static BuildDefinition Build(string id = "kr-812") => new(id, "kr", 812,
        new LocalizedText(new Dictionary<string, string> { ["en"] = "Client" }), "game.exe", "", "", null, null, "", [], []);
    private void Write(string file, string json) => File.WriteAllText(Path.Combine(_folder, file), json);
    private void Replace(string file, string before, string after)
    {
        var path = Path.Combine(_folder, file);
        var json = File.ReadAllText(path);
        Assert.Contains(before, json, StringComparison.Ordinal);
        Write(file, json.Replace(before, after, StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
