using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>Credential-free native activation result for one resolved plugin.</summary>
/// <param name="Id">Resolved package identifier.</param>
/// <param name="Version">Exact selected package version.</param>
/// <param name="Outcome">Native runtime's stable outcome name.</param>
/// <param name="Required">Whether activation is required for this launch.</param>
public sealed record HookPluginStatus(string Id, string Version, string Outcome, bool Required);

/// <summary>Validated native readiness for a single launch, independent of process creation or game-server login.</summary>
/// <param name="State">starting, ready, failed, or not-required.</param>
/// <param name="Error">A bounded machine-readable reason; never a ticket, account or plugin diagnostic.</param>
/// <param name="Plugins">Results belonging only to this launch's resolved package list.</param>
public sealed record HookLaunchStatus(string State, string Error, IReadOnlyList<HookPluginStatus> Plugins)
{
    /// <summary>Whether the authenticated native host has completed the game's bootstrap handoff.</summary>
    public bool BootstrapComplete { get; init; }
}

internal static class HookStatusReader
{
    internal static string FileName(string launchId) => "fthook-status-" + launchId + ".toml";

    internal static HookLaunchStatus Read(string file, SessionSpec expected, int processId, long processStarted, string runtimeVersion)
    {
        InstanceStore.RejectLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64 * 1024) throw new IOException("Native readiness report exceeds its limit.");
        var bytes = new byte[64 * 1024 + 1];
        var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count == bytes.Length) throw new IOException("Native readiness report exceeds its limit.");
        return Parse(file, new System.Text.UTF8Encoding(false, true).GetString(bytes, 0, count), expected, processId, processStarted, runtimeVersion);
    }

    internal static HookLaunchStatus Parse(string source, string text, SessionSpec expected, int processId, long processStarted, string runtimeVersion)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 64 * 1024) throw new IOException("Native readiness report exceeds its limit.");
        var root = TomlReader.Parse(source, text);
        root.OnlyKeys("schema", "launch_id", "game", "build", "instance", "pid", "process_started", "runtime", "state", "error", "plugins", "bootstrap_complete");
        if (root.Integer("schema", true) != 1 || root.String("launch_id", true) != expected.LaunchId
            || root.String("game", true) != expected.Game || root.String("build", true) != expected.Build.Id
            || root.String("instance", true) != expected.Instance || root.Integer("pid", true) != processId
            || root.Integer("process_started", true) != processStarted || root.String("runtime", true) != runtimeVersion)
            throw new IOException("Native readiness report does not belong to this launch.");
        var state = root.String("state", true);
        var error = root.String("error", true);
        if (state is not ("starting" or "ready" or "failed") || error is not ("" or "host-initialization" or "fingerprint" or "required-plugin" or "patch-restoration" or "startup-timeout" or "status-write"))
            throw new IOException("Unknown native readiness result.");
        if ((state == "failed") != (error.Length > 0)) throw new IOException("Inconsistent native readiness error.");
        var bootstrapComplete = root.Boolean("bootstrap_complete");
        if (bootstrapComplete && state != "ready") throw new IOException("Bootstrap completion requires a ready native host.");
        var rows = root.Tables("plugins", 64);
        if (rows.Count > expected.Plugins.Count || (state == "ready" && rows.Count != expected.Plugins.Count))
            throw new IOException("Native readiness report has an incomplete plugin list.");
        var plugins = new List<HookPluginStatus>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; var selected = expected.Plugins[i];
            row.OnlyKeys("id", "version", "outcome", "required");
            var outcome = row.String("outcome", true);
            if (row.String("id", true) != selected.Id || row.String("version", true) != selected.Version || row.Boolean("required") != selected.Required
                || outcome is not ("installed" or "already-present" or "skipped-option" or "unavailable-build" or "signature-mismatch" or "timeout" or "failed" or "rollback-failed"))
                throw new IOException("Native readiness report does not match a resolved plugin.");
            if (state == "ready" && selected.Required && outcome is not ("installed" or "already-present" or "skipped-option"))
                throw new IOException("A required plugin failed in a ready report.");
            if (outcome == "rollback-failed" && (state != "failed" || error != "patch-restoration"))
                throw new IOException("Incomplete patch restoration must fail native readiness.");
            plugins.Add(new HookPluginStatus(selected.Id, selected.Version, outcome, selected.Required));
        }
        return new HookLaunchStatus(state, error, plugins) { BootstrapComplete = bootstrapComplete };
    }
}
