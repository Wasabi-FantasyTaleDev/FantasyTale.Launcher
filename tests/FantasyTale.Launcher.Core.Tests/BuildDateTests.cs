using System.Globalization;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BuildDateTests
{
    private static Sdk.BuildDefinition Build(string date) => GameCatalog.ParseBuild("test-1.toml", $$"""
        schema = 1
        [build]
        id = "test-1"
        game = "test"
        branch = "test"
        protocol_version = 1
        executable = "game.exe"
        name = { en = "Fixture" }
        {{date}}
        [launch]
        login = "none"
        """, "test");

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("ko-KR")]
    [InlineData("th-TH")]
    public void DateIsOptionalAndNeverUsesTheSystemCalendar(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Null(Build("").BuildDate);
            var date = Assert.IsType<DateTime>(Build("build_date = \"2011-01-01 00:00:00\"").BuildDate);
            Assert.Equal(new DateTime(2011, 1, 1), date);
            Assert.Equal(DateTimeKind.Unspecified, date.Kind);
            Assert.Equal("2011-01-01 00:00:00", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("build_date = \"2011-01-01\"")]
    [InlineData("build_date = \"2011-1-1 0:00:00\"")]
    [InlineData("build_date = \"2011-02-29 00:00:00\"")]
    [InlineData("build_date = \"2011-01-01T00:00:00Z\"")]
    [InlineData("build_date = 2011-01-01T00:00:00")]
    [InlineData("year = 2011")]
    public void InvalidOrRetiredDateFieldsAreRejected(string field) =>
        Assert.Throws<DefinitionException>(() => Build(field));
}
