using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Persists the global IP preference independently of the web view's browser storage.</summary>
public sealed class NetworkSettings
{
    private readonly string _file;
    private readonly object _gate = new();
    private string _preference = "china";
    private bool _githubMirror;
    private string _githubMirrorSource = "auto";
    private bool _mirrorDetectionComplete;
    private bool _mirrorPrompted;
    private bool _mirrorSuggestionPending;

    /// <summary>Loads network.toml, defaulting to the mainland-China policy when absent.</summary>
    public NetworkSettings(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        _file = Path.Combine(dataRoot, "network.toml");
        InstanceStore.RejectLinks(_file);
        if (!File.Exists(_file)) return;
        var t = TomlReader.Load(_file); t.OnlyKeys("ip_preference", "github_mirror", "github_mirror_source",
            "github_mirror_checked", "github_mirror_prompted", "github_mirror_suggestion_pending");
        _preference = Validate(t.String("ip_preference", true));
        _githubMirror = t.Boolean("github_mirror");
        _mirrorDetectionComplete = t.Boolean("github_mirror_checked");
        _mirrorPrompted = t.Boolean("github_mirror_prompted");
        _mirrorSuggestionPending = t.Boolean("github_mirror_suggestion_pending") && !_mirrorPrompted;
        if (t.Has("github_mirror_source")) _githubMirrorSource = t.String("github_mirror_source", true);
    }

    /// <summary>Gets china, all or disabled.</summary>
    public string IpPreference { get { lock (_gate) return _preference; } }

    /// <summary>Gets whether public GitHub downloads use mirror routing.</summary>
    public bool GitHubMirror { get { lock (_gate) return _githubMirror; } }

    /// <summary>Gets the automatic ranking policy or selected provider identity.</summary>
    public string GitHubMirrorSource { get { lock (_gate) return _githubMirrorSource; } }

    /// <summary>Gets whether the one-time country detection was recorded or superseded by a new manual choice.</summary>
    public bool MirrorDetectionComplete { get { lock (_gate) return _mirrorDetectionComplete; } }

    /// <summary>Gets whether mainland-China detection is waiting for the user's mirror choice.</summary>
    public bool MirrorSuggestionPending { get { lock (_gate) return _mirrorSuggestionPending; } }

    /// <summary>Suggests mirror acceleration for mainland China without enabling it before confirmation.</summary>
    public void ObserveMirrorCountry(string? countryCode)
    {
        lock (_gate)
        {
            if (_mirrorDetectionComplete) return;
            SaveCore(_preference, detected: true, pending: countryCode == "CN");
        }
    }

    /// <summary>Records an actually displayed suggestion so application restarts cannot show it again.</summary>
    public void MarkMirrorSuggestionShown()
    {
        lock (_gate)
        {
            if (!_mirrorSuggestionPending) return;
            SaveCore(_preference, pending: false, prompted: true);
        }
    }

    /// <summary>Validates and atomically saves the global preference.</summary>
    public void Save(string preference, bool? githubMirror = null, string? githubMirrorSource = null)
        => SaveCore(preference, githubMirror, githubMirrorSource, detected: githubMirror.HasValue ? true : null,
            pending: githubMirror.HasValue ? false : null);

    private void SaveCore(string preference, bool? githubMirror = null, string? githubMirrorSource = null,
        bool? detected = null, bool? pending = null, bool? prompted = null)
    {
        Validate(preference);
        lock (_gate)
        {
            InstanceStore.RejectLinks(_file);
            var temporary = _file + "." + Guid.NewGuid().ToString("N");
            try
            {
                var mirror = githubMirror ?? _githubMirror;
                var source = githubMirrorSource ?? _githubMirrorSource;
                var checkedOnce = detected ?? _mirrorDetectionComplete;
                var suggestionPending = pending ?? _mirrorSuggestionPending;
                var suggestionShown = prompted ?? _mirrorPrompted;
                if (source.Length == 0 || source.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
                    throw new ArgumentException("Invalid mirror provider identity.");
                File.WriteAllText(temporary, $"ip_preference = \"{preference}\"\ngithub_mirror = {TomlBool(mirror)}\ngithub_mirror_source = \"{source}\"\n" +
                    $"github_mirror_checked = {TomlBool(checkedOnce)}\ngithub_mirror_prompted = {TomlBool(suggestionShown)}\ngithub_mirror_suggestion_pending = {TomlBool(suggestionPending)}\n", new UTF8Encoding(false));
                File.Move(temporary, _file, true); _preference = preference; _githubMirror = mirror;
                _githubMirrorSource = source;
                _mirrorDetectionComplete = checkedOnce; _mirrorSuggestionPending = suggestionPending; _mirrorPrompted = suggestionShown;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static string TomlBool(bool value) => value ? "true" : "false";

    private static string Validate(string value) => value is "china" or "all" or "disabled" ? value : throw new ArgumentException("IP preference must be china, all or disabled.");
}
