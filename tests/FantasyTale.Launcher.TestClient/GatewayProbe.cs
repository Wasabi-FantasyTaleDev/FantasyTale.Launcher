using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

internal static class GatewayProbe
{
    internal static async Task<int> RunAsync(string pipeName, int controlMaximum)
    {
        var mode = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gateway-probe"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, timeout.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > controlMaximum) return 9;
        var body = new byte[length];
        await pipe.ReadExactlyAsync(body, timeout.Token);
        using var configuration = JsonDocument.Parse(body);
        var build = configuration.RootElement.GetProperty("rows")[0].GetProperty("config").GetProperty("build").GetString();
        await Console.Error.WriteLineAsync("bf-center-selector: native greeting sent.");
        await Console.Error.WriteLineAsync("{\"ticket\":\"fixture-secret\",\"account\":\"fixture-identity\"}");
        await Console.Error.WriteLineAsync("unlabelled-fixture-secret");
        await Console.Error.WriteLineAsync(new string('x', 4096));
        await Console.Error.WriteLineAsync("bf-center-selector: rejected bootstrap frame; phase=handshake, opcode=271, payload_bytes=0.");
        var ready = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1, type = mode == "invalid-ready" ? "invalid" : "ready",
            rows = new[] { new { id = "selector", build, mode = "bootstrap", host = "127.0.0.1", port = 12345 } },
        });
        BinaryPrimitives.WriteInt32LittleEndian(header, ready.Length);
        await pipe.WriteAsync(header, timeout.Token);
        await pipe.WriteAsync(ready, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        if (mode is "exit-7" or "invalid-ready") return 7;
        if (mode is "retire-naturally" or "retire-on-stop")
        {
            var control = pipe.ReadExactlyAsync(header, timeout.Token).AsTask();
            while (!File.Exists(Path.Combine(AppContext.BaseDirectory, "retire-gateway")))
            {
                if (control.IsCompleted)
                {
                    await control;
                    length = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (length < 1 || length > controlMaximum) return 9;
                    var message = new byte[length];
                    await pipe.ReadExactlyAsync(message, timeout.Token);
                    using var command = JsonDocument.Parse(message);
                    return mode == "retire-on-stop" && command.RootElement.GetProperty("type").GetString() == "stop" ? 0 : 11;
                }
                await Task.Delay(20, timeout.Token);
            }
            await Console.Error.WriteLineAsync("bf-center-selector: unavailable auxiliary retirement started.");
            // The pending read belongs to this still-open pipe until process exit.
            return 0;
        }
        await pipe.ReadExactlyAsync(header, timeout.Token);
        length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > controlMaximum) return 9;
        await pipe.ReadExactlyAsync(new byte[length], timeout.Token);
        return 0;
    }
}
