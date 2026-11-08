using FantasyTale.Launcher.Host.Window;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class OverlayTestSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-overlay-settings-").FullName;

    [Fact]
    public void StartupTestIsOffWithoutAnExplicitLocalSetting()
    {
        var settings = OverlayTestSettings.Load(_root);
        Assert.False(settings.StartupMessage);
        Assert.Null(settings.ResourceRoot);
        Assert.True(settings.PassThroughOutside);
    }

    [Fact]
    public void LocalStartupTestKeepsAnExplicitExternalResourceDirectory()
    {
        var path = Path.Combine(_root, "overlay-test.toml");
        File.WriteAllText(path, $"startup_message = true\nresource_root = '{_root}'\n");
        var settings = OverlayTestSettings.Load(_root);
        Assert.True(settings.StartupMessage);
        Assert.Equal(_root, settings.ResourceRoot);
    }

    [Theory]
    [InlineData("startup_message = 'true'")]
    [InlineData("unknown = true")]
    [InlineData("resource_root = '../resources'")]
    [InlineData("resource_root = 123")]
    [InlineData("pass_through_outside = 'true'")]
    public void InvalidLocalTestSettingsFailExplicitly(string document)
    {
        File.WriteAllText(Path.Combine(_root, "overlay-test.toml"), document);
        Assert.Throws<InvalidDataException>(() => OverlayTestSettings.Load(_root));
    }

    [Fact]
    public void DeveloperTogglePersistsWithoutLosingResourceConfiguration()
    {
        var enabled = new OverlayTestSettings(true, _root, PassThroughOutside: false);
        enabled.Save(_root);
        Assert.Equal(enabled, OverlayTestSettings.Load(_root));
        var disabled = enabled with { StartupMessage = false };
        disabled.Save(_root);
        Assert.Equal(disabled, OverlayTestSettings.Load(_root));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void ExistingLocalStartupSettingDefaultsToPassingOutsideClicks()
    {
        File.WriteAllText(Path.Combine(_root, "overlay-test.toml"), "startup_message = true");
        Assert.True(OverlayTestSettings.Load(_root).PassThroughOutside);
    }

    [Fact]
    public void ChangingPreferenceAffectsFutureLaunchesWithoutMutatingTheirSnapshot()
    {
        var enabled = new OverlayTestSettings(true, null);
        var manager = new OverlayProcessManager(_root, test: enabled);
        var runningLaunch = manager.TestSettings;
        manager.SetTestSettings(enabled with { StartupMessage = false });
        Assert.False(manager.TestSettings.StartupMessage);
        Assert.True(runningLaunch.StartupMessage);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
