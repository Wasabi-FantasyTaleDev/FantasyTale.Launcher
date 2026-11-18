using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.BanPick;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BanPickRoomTests
{
    private static JsonElement Shipped()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "store-plugins"))) directory = Path.GetDirectoryName(directory);
        var path = Path.Combine(directory ?? throw new DirectoryNotFoundException("The repository root was not found."),
            "store-plugins", "BubbleFighter", "bubblefighter.fantasy.banpick", "room-layouts.json");
        return JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.Clone();
    }

    [Fact]
    public void TheButtonFollowsTheRoomWindowInClientPixels()
    {
        var form = new GameNativeUiForm("Ui/Stages/Room/RoomStage2_8vs8ver04.ui", 4, true, 100, 50, 1024, 768, 1024, 768);
        var data = BanPickRoomData.Parse(Shipped(), "tw-454");
        var layout = data.Layouts.Single(row => row.Style == "v3");

        // Host: immediately left of the native select-map button; member: the select-map button's own spot.
        Assert.Equal(new OverlayAnchor(274, 152, 56, 23), BanPickRoomController.Place(form, layout.Host));
        Assert.Equal(new OverlayAnchor(331, 152, 56, 23), BanPickRoomController.Place(form, layout.Member));
        Assert.Equal(layout.Host.X + layout.Host.Width + 1, layout.Member.X);
    }

    [Fact]
    public void EveryShippedBuildNamesItsRoomFormsOnce()
    {
        var root = Shipped();
        foreach (var build in root.GetProperty("builds").EnumerateObject())
        {
            var data = BanPickRoomData.Parse(root, build.Name);
            Assert.NotEmpty(data.Layouts);
            Assert.All(data.Layouts, layout => Assert.True(layout.Host.Width > 0 && layout.Member.Height > 0));
        }
    }

    [Fact]
    public void ABuildWithoutLayoutsHasNoRoomForm() =>
        Assert.Empty(BanPickRoomData.Parse(Shipped(), "no-such-build").Layouts);

    [Fact]
    public void TheSelectMapSheetHasFourFrames()
    {
        var table = new byte[8 + 4 * 32];
        BitConverter.GetBytes(2u).CopyTo(table, 0);
        BitConverter.GetBytes(4u).CopyTo(table, 4);
        for (var index = 0; index < 4; index++)
        {
            var record = 8 + index * 32;
            BitConverter.GetBytes(1).CopyTo(table, record);
            BitConverter.GetBytes(1 + index * 32).CopyTo(table, record + 4);
            BitConverter.GetBytes(62).CopyTo(table, record + 8);
            BitConverter.GetBytes(30 + index * 32).CopyTo(table, record + 12);
        }

        var frames = ClientSpriteReader.ParseFrames(table);

        Assert.NotNull(frames);
        Assert.Equal([1, 97, 62, 126], frames[3]);
        Assert.Null(ClientSpriteReader.ParseFrames(table[..^1]));
        BitConverter.GetBytes(3u).CopyTo(table, 0);
        Assert.Null(ClientSpriteReader.ParseFrames(table));
    }
}
