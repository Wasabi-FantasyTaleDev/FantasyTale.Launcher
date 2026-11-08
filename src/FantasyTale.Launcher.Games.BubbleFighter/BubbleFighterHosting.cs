using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule
{
    /// <summary>Builds an isolated deployment patch for a supported server profile and its managed-stdio host.</summary>
    public LocalServerPlan CreateServerPlan(LocalServerContext context)
    {
        var profile = context.Profile; var build = context.Build;
        if (build.HostingAdapter is not ("legacy" or "modern") || build.ServerProfile.Length is < 1 or > 64 || build.ServerProfile.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("This build has no supported local server adapter.");
        var root = Path.GetFullPath(profile.PackagePath);
        var storage = profile.EffectiveStorageMode;
        if (storage is not ("package" or "local" or "temporary") || profile.StorageMode is not null && profile.TemporaryData != (storage == "temporary"))
            throw new ArgumentException("Invalid or conflicting storage selection.");
        string? savePath = null;
        if (storage == "local")
        {
            if (context.DataDirectory is null || !Path.IsPathFullyQualified(context.DataDirectory) || context.DataDirectory.Contains("${", StringComparison.Ordinal))
                throw new ArgumentException("Local storage requires an absolute owned data directory.");
            foreach (var required in new[] { "plugins/BubbleFighter.Fantasy.Persistence.Sqlite/BubbleFighter.Fantasy.Persistence.Sqlite.dll", "plugins/BubbleFighter.Fantasy.Persistence.Sqlite/e_sqlite3.dll" })
                if (!File.Exists(Path.Combine(root, required))) throw new InvalidOperationException("This server package does not include complete local-storage support: " + required);
            savePath = Path.Combine(context.DataDirectory, "save.sqlite").Replace('\\', '/');
        }
        foreach (var required in new[] { "profiles/" + build.ServerProfile + "/profile.yaml", "packets/builds.yaml" })
            if (!File.Exists(Path.Combine(root, required))) throw new InvalidOperationException("The server package is incomplete: " + required);
        if (profile.Name.Contains("${", StringComparison.Ordinal) || context.SessionDirectory.Contains("${", StringComparison.Ordinal))
            throw new ArgumentException("Hosting names and paths cannot contain configuration substitutions.");
        var client = context.ClientDirectory;
        if (client is not null && (!Path.IsPathFullyQualified(client) || client.Contains("${", StringComparison.Ordinal)))
            throw new ArgumentException("Local hosting requires an absolute client data directory.");
        var dataRoot = client ?? Path.Combine(root, "client-data", build.ServerProfile);
        // clientdata keys archives by their client-relative paths, including Pack/, Packs/ and Config/.
        var archives = client ?? Path.Combine(dataRoot, "data");
        var loose = client ?? Path.Combine(dataRoot, "dataraw");
        if (!Directory.Exists(archives) && !Directory.Exists(loose))
            throw new InvalidOperationException("local_client_data_missing");
        var host = profile.Mode == "private" ? "127.0.0.1" : profile.AdvertisedHost;
        var bind = profile.Mode switch { "private" => "127.0.0.1", "lan" => profile.AdvertisedHost, "room" => "0.0.0.0", _ => throw new ArgumentException("Unknown hosting scope.") };
        var patch = $$"""
            - set:
                id: game-listener
                config:
                  listen: {{Quote(bind + ":" + profile.GamePort)}}
            - set:
                id: admin.http
                config:
                  listen: {{Quote("127.0.0.1:" + profile.AdminPort)}}
            - replace:
                id: launcher.http
                disabled: false
            - set:
                id: launcher.http
                config:
                  name: {{Quote(profile.Name)}}
                  listen: {{Quote(bind + ":" + profile.ApiPort)}}
                  host_policy: auto
                  region: ""
                  minimum_password_length: 6
                  instances:
                    - id: {{Quote(build.Id)}}
                      build: {{Quote(build.Id)}}
                      name:
                        - language: en
                          text: {{Quote(build.Name.Values.GetValueOrDefault("en") ?? build.Id)}}
                      host: {{Quote(host)}}
                      port: {{profile.GamePort}}
                      launcher_login: {{(profile.RequireAuthentication ? "required" : "none")}}
            - set:
                id: lobby
                config:
                  launcher_tickets: {{(profile.RequireAuthentication ? "true" : "false")}}
                  authentication: {{(profile.RequireAuthentication ? "ticket" : "open")}}
            - set:
                id: clientdata
                config:
                  data_directory: {{Quote(archives.Replace('\\', '/'))}}
                  raw_directory: {{Quote(loose.Replace('\\', '/'))}}
            - set:
                id: diagnostics.packet-log
                config:
                  file: {{Quote(Path.Combine(context.SessionDirectory, "packets-{timestamp}.log").Replace('\\', '/'))}}
                  capture_enabled: false
                  console_packets: false
                  max_log_files: 4
                  max_file_bytes: 2097152
            """;
        if (!profile.RequireAuthentication) patch += """

            - set:
                id: lobby
                config:
                  auto_create_players: true
                  character_creation: false
                  character_creation_preview: false
                  recording_always_create_character: false
            - set:
                id: dialect.rooms
                config:
                  tutorial_move_in: false
                  initial_tutorial: false
            - set:
                id: dialect.messages
                config:
                  character_creation: false
                  character_creation_preview: false
            """;
        if (build.HostingAdapter == "legacy") patch += $$"""

            - set:
                id: protocol
                config:
                  center_gates:
                    - key: ${LEGACY_GATE_KEY:-0}
                      sub_key: ${LEGACY_GATE_SUB_KEY:-0}
                      unknown1: 0
                      address: {{Quote(host)}}
                      port: {{profile.GamePort}}
            """;
        else patch += $$"""

            - set:
                id: relay-listener
                config:
                  listen: {{Quote(bind + ":" + profile.EffectiveRelayPort)}}
            - set:
                id: ingame
                config:
                  relay_advertise_ipv4: {{Quote(host)}}
                  relay_advertise_port: {{profile.EffectiveRelayPort}}
            """;
        if (storage == "temporary") patch += """

            - replace:
                id: persistence.mysql
                disabled: true
            - replace:
                id: persistence.memory
                disabled: false
            - set:
                id: lobby
                config:
                  inventory_source: mysql
                  money_source: mysql
            """;
        if (storage == "local") patch += $$"""

            - replace:
                id: persistence.mysql
                disabled: true
            - replace:
                id: persistence.memory
                disabled: true
            - replace:
                id: persistence.sqlite
                disabled: false
            - set:
                id: persistence.sqlite
                config:
                  database_path: {{Quote(savePath!)}}
            - set:
                id: lobby
                config:
                  inventory_source: mysql
                  money_source: mysql
            """;
        return new(Path.Combine(root, "BubbleFighter.Fantasy.Host.exe"), ["run", "--profile", build.ServerProfile, "--patch", Path.Combine(context.SessionDirectory, "hosting.patch.yaml"), "--managed-stdio"],
            [new("hosting.patch.yaml", patch + "\n")], $"http://{(profile.Mode == "lan" ? profile.AdvertisedHost : "127.0.0.1")}:{profile.ApiPort}/", $"http://127.0.0.1:{profile.AdminPort}/")
        {
            // The base bundle expands this variable before the final patch disables its database row.
            // This value is never used by a connection; the selected local provider owns the repositories.
            Environment = storage != "package" ? new Dictionary<string, string> { ["BFF_MYSQL_PASSWORD"] = "unused-local-provider" } : new Dictionary<string, string>(),
        };
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
