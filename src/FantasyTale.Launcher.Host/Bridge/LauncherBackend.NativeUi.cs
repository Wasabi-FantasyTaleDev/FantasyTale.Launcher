using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private void RegisterNativeUi(BridgeRouter bridge)
    {
        GameNativeUiClient Client(JsonNode? p)
        {
            var id = Text(p, "id");
            var pid = p?["processId"]?.GetValue<int>() ?? throw new ArgumentException("Select a running client process.");
            var running = _launches.Running.SingleOrDefault(client => client.InstanceId == id && client.ProcessId == pid)
                ?? throw new InvalidOperationException("The selected client process is no longer running.");
            var control = running.Control ?? throw new InvalidOperationException("This client has no active hook channel.");
            return new(control, _store.Load(id).Build);
        }
        bridge.Add("nativeUi.capabilities", async (p, ct) =>
        {
            Keys(p, "id", "processId");
            return await Client(p).GetCapabilitiesAsync(ct).ConfigureAwait(false);
        });
        bridge.Add("nativeUi.invoke", async (p, ct) =>
        {
            Keys(p, "id", "processId", "request");
            var request = p?["request"]?.Deserialize<GameNativeUiRequest>(BridgeRouter.Json) ?? throw new ArgumentException("Native UI request required.");
            return await Client(p).InvokeAsync(request, ct).ConfigureAwait(false);
        });
        bridge.Add("nativeUi.result", async (p, ct) =>
        {
            Keys(p, "id", "processId", "operationId");
            return await Client(p).GetResultAsync(Text(p, "operationId"), ct).ConfigureAwait(false);
        });
    }
}
