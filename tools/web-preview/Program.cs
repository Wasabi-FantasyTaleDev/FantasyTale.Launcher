using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args is ["--stdin"])
{
    Console.Write(JsonSerializer.Serialize(TomlSerializer.Deserialize<TomlTable>(Console.In.ReadToEnd())));
    return;
}
if (args.Length != 1) throw new ArgumentException("Supply the games root or --stdin.");
var root = Path.GetFullPath(args[0]);
var files = Directory.EnumerateDirectories(root).SelectMany(folder =>
    new[] { Path.Combine(folder, "game.toml") }.Where(File.Exists)
        .Concat(Directory.Exists(Path.Combine(folder, "servers")) ? Directory.EnumerateFiles(Path.Combine(folder, "servers"), "*.toml") : [])
        .Concat(Directory.Exists(Path.Combine(folder, "plugins")) ? Directory.EnumerateDirectories(Path.Combine(folder, "plugins"))
            .Select(plugin => Path.Combine(plugin, "plugin.toml")).Where(File.Exists) : []));
var definitions = files.ToDictionary(file => file.Replace('\\', '/'), file => TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(file)));
Console.Write(JsonSerializer.Serialize(definitions));
