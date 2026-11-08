using FantasyTale.Launcher.Sdk;
using System.Reflection.PortableExecutable;
using System.Text;

namespace FantasyTale.Launcher.Games.BubbleFighter;

/// <summary>
/// Starts Bubble Fighter clients. Everything build-specific comes from the build definition:
/// the executable and an argument template with <c>{account}</c>, <c>{host}</c> and <c>{port}</c>.
/// Hooks are loaded by the client itself through <c>nmcogame.dll</c> and <c>session.toml</c>.
/// </summary>
public sealed partial class BubbleFighterModule : IGameModule, ILocalServerModule, ILaunchBootstrapProvider, IAutomaticServerSelectionCapability
{
    /// <inheritdoc />
    public bool SupportsAutomaticSelection(BuildDefinition build) => GatewayBuildCapabilities.SupportsAutomaticSelection(build.Id);
    /// <inheritdoc />
    public string GameId => "bubblefighter";

    /// <inheritdoc />
    public OverlayBuildProfile GetOverlayProfile(BuildDefinition build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var profile = build.Overlay ?? new OverlayBuildProfile(build.BuildDate is { Year: <= 2012 } ? "v1" : "v2", [], []);
        return build.BuildDate >= new DateTime(2017, 2, 7) ? profile with { Theme = "v3" } : profile;
    }

    /// <inheritdoc />
    public LaunchPlan CreateLaunchPlan(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var build = context.Build;
        if (build.Login == "unverified")
            throw new InvalidOperationException($"Build {build.Id} has no verified launch arguments yet.");
        var executable = ClientExecutable.FindInstalled(context.InstanceFolder, build) ?? ClientExecutable.CandidatePaths(context.InstanceFolder, build)[0];

        var gameEndpoint = context.GameEndpoint ?? context.Server;
        var host = gameEndpoint?.Host;
        var port = gameEndpoint?.Port ?? build.DefaultPort;
        var arguments = build.Arguments.Select(template => Expand(template, build.Id, context.Account, host, port)).ToArray();
        var files = new List<RuntimeFileBinding>
        {
            new("nmcogame.dll", "nmcogame.dll", build.Messenger == "forward" ? "nmcogame.nexon.dll" : null),
        };
        if (build.Login == "gamewidget-ticket")
        {
            if (build.LoginModule is not { } target || target.Equals("nmcogame.dll", StringComparison.OrdinalIgnoreCase)
                || target.Equals("nmcogame.nexon.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Build {build.Id} requires a separate login_module target.");
            if (!context.FilePluginTargets.Contains(target)) files.Add(new("login_bridge.dll", target));
        }
        else if (build.LoginModule is not null)
            throw new InvalidOperationException($"Build {build.Id} has a login_module without a supported authentication bridge.");
        var minimumPluginVersions = !context.NameLogin ? new Dictionary<string, string>() : build.Login switch
        {
            "hook" => new Dictionary<string, string> { ["bubblefighter.fantasy.login-fix-kr"] = "0.1.3" },
            "gamewidget-ticket" when build.LoginModule is { } target && context.FilePluginTargets.Contains(target)
                => new Dictionary<string, string> { ["bubblefighter.fantasy.login-fix-cn"] = "1.0.2" },
            _ => new Dictionary<string, string>(),
        };
        return new LaunchPlan(executable, arguments, context.InstanceFolder)
        {
            RequiresElevation = build.RequiresElevation,
            // The hook login build's required native component is the sole owner of the
            // launch credential callback; the shared launcher validates that it is selected.
            AuthenticationPlugin = build.Login == "hook" ? "bubblefighter.fantasy.login-fix-kr" : null,
            MinimumPluginVersions = minimumPluginVersions,
            Runtime = new RuntimeDeploymentPlan(Path.Combine(context.Game.Folder, "runtime", "nmcogame"), files),
        };
    }

    /// <summary>Checks the client fingerprint and messenger files without changing the installed client.</summary>
    public void ValidateInstallation(LaunchContext context)
    {
        var plan = CreateLaunchPlan(context);
        if (context.Build.PeTimestamp.HasValue || context.Build.SignerCertificateSha256 is not null)
        {
            if (!ClientExecutable.Matches(ClientExecutable.ReadIdentity(plan.ExecutablePath), context.Build))
                throw new InvalidOperationException($"The client executable does not match build {context.Build.Id}.");
        }
        if (context.Build.Messenger == "forward")
        {
            var original = Path.Combine(context.InstanceFolder, "nmcogame.nexon.dll");
            if (!File.Exists(original)) original = Path.Combine(context.InstanceFolder, "nmcogame.dll");
            ValidateOriginalMessenger(original);
        }
    }

    private static void ValidateOriginalMessenger(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked messenger files are not supported.");
        using var file = File.OpenRead(path);
        using var pe = new PEReader(file);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader is not { } header)
            throw new InvalidOperationException("Forward mode needs the original x86 messenger library.");
        var directory = pe.GetSectionData(header.ExportTableDirectory.RelativeVirtualAddress).GetReader(0, 40);
        directory.Offset = 24;
        var count = directory.ReadUInt32(); directory.Offset = 32; var namesRva = directory.ReadInt32();
        // Original messenger builds have ten exports; our host exposes the union plus its login bridge.
        if (count is 0 or > 10) throw new InvalidOperationException("The forwarding target appears to be a replacement messenger. Restore the original library first.");
        var names = pe.GetSectionData(namesRva).GetReader(0, checked((int)count * 4));
        var found = false;
        for (var i = 0; i < count; i++)
        {
            var name = pe.GetSectionData(names.ReadInt32()).GetReader();
            var bytes = new List<byte>();
            for (var n = 0; n < 128; n++) { var b = name.ReadByte(); if (b == 0) break; bytes.Add(b); }
            var export = Encoding.ASCII.GetString(bytes.ToArray());
            found |= export == "NMCO_CallNMFunc";
        }
        if (!found) throw new InvalidOperationException("Forward mode needs the original messenger call interface, not a stand-in.");
    }

    private static string Expand(string template, string build, string? account, string? host, int? port)
    {
        string Need(string? value, string name) =>
            value ?? throw new InvalidOperationException($"Build {build} needs {name} to start.");
        var result = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{') { result.Append(template[i]); continue; }
            var end = template.IndexOf('}', i + 1);
            if (end < 0) throw new InvalidOperationException($"Build {build} has an unterminated argument placeholder.");
            result.Append(template[(i + 1)..end] switch
            {
                "account" => Need(account, "an account"), "host" => Need(host, "a server address"),
                "port" => Need(port?.ToString(System.Globalization.CultureInfo.InvariantCulture), "a server port"),
                _ => throw new InvalidOperationException($"Build {build} has an unknown argument placeholder."),
            });
            i = end;
        }
        return result.ToString();
    }
}
