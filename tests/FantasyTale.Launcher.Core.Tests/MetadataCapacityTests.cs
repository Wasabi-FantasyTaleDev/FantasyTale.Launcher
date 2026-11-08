using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class MetadataCapacityTests
{
    [Fact]
    public void UpdateOutcomeReadsLargeReadableRecordsWithoutDroppingIdentity()
    {
        var root = Directory.CreateTempSubdirectory("ft-update-record-").FullName;
        try
        {
            var path = Path.Combine(root, "outcome.json");
            var outcome = new LauncherUpdateHandoff.Outcome("rolled-back", new string('a', 64), new string('b', 64));
            UpdateFiles.Write(path, outcome);
            File.AppendAllText(path, new string(' ', 256 * 1024));
            Assert.Equal(outcome, UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InstanceOptionsRoundTripBeyondFormerDocumentByteCeiling()
    {
        var root = Directory.CreateTempSubdirectory("ft-instance-metadata-").FullName;
        try
        {
            var value = new string('a', 256 * 1024 + 1);
            var instance = new InstanceDefinition("sample", "Sample", "test", "test-1", null,
                [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object> { ["content"] = value })]);
            var path = Path.Combine(root, "instance.toml");
            File.WriteAllText(path, InstanceFile.Render(instance));
            Assert.Equal(value, Assert.Single(InstanceFile.Load(path).Plugins).Options["content"]);
        }
        finally { Directory.Delete(root, true); }
    }
}
