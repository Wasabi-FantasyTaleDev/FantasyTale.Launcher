using System.Collections.Concurrent;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private sealed record AutomaticPluginTask(string From, bool Notified = false);
    private readonly ConcurrentDictionary<string, AutomaticPluginTask> _automaticPluginTasks = new(StringComparer.Ordinal);

    private void NotifyAutomaticPlugins(BridgeRouter bridge, IReadOnlyList<ClientDownloadTask> tasks)
    {
        foreach (var (id, pending) in _automaticPluginTasks)
        {
            var task = tasks.SingleOrDefault(item => item.Id == id);
            if (task is null || task.Stage == "canceled") { _automaticPluginTasks.TryRemove(id, out _); continue; }
            if (pending.Notified || task.Stage != "completed"
                || !_automaticPluginTasks.TryUpdate(id, pending with { Notified = true }, pending)) continue;
            bridge.Emit("plugins.autoUpdated", new { task.Id, task.Target.GameId, task.Target.PackageId,
                name = task.Target.NameLocalized, fallbackName = task.Target.Name, from = pending.From, to = task.Target.ToVersion });
            bridge.Emit("plugins.changed", new { game = task.Target.GameId });
        }
    }
}
