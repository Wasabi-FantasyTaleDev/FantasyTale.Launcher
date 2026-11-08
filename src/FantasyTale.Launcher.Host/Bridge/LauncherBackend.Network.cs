using System.Text.Json;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private async Task InitializeMirrorDefaultAsync(Uri address, TimeSpan timeout)
    {
        if (_networkSettings.MirrorDetectionComplete) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_mirrorDefaultStop.Token); deadline.CancelAfter(timeout);
        try
        {
            using var response = await _sourceHttp.GetAsync(address, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("country_code", out var value) && value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: 2 } country)
                _networkSettings.ObserveMirrorCountry(country.ToUpperInvariant());
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // Detection failures preserve the existing preference and never interrupt startup.
        }
        finally { _networkSettings.ObserveMirrorCountry(null); }
    }

    private object NetworkState() => new { _networkSettings.IpPreference, _networkSettings.GitHubMirror, _networkSettings.GitHubMirrorSource,
        suggestionPending = _networkSettings.MirrorSuggestionPending,
        defaultPending = !_mirrorDefaultTask.IsCompleted && !_networkSettings.MirrorDetectionComplete };

    private void RegisterNetworkSettings(BridgeRouter bridge)
    {
        bridge.Add("settings.network", _ => NetworkState());
        bridge.Add("settings.showGitHubMirrorSuggestion", _ =>
        {
            _networkSettings.MarkMirrorSuggestionShown();
            return NetworkState();
        });
        bridge.Add("settings.setNetwork", p =>
        {
            Keys(p, "ipPreference", "githubMirror", "githubMirrorSource");
            var source = p?["githubMirrorSource"]?.GetValue<string>();
            if (source is not null) _githubMirrors.ValidateSelection(source);
            _networkSettings.Save(p?["ipPreference"]?.GetValue<string>() ?? _networkSettings.IpPreference,
                p?["githubMirror"]?.GetValue<bool>(), source);
            if (source is not null) _githubMirrors.RestoreSelection(source);
            return NetworkState();
        });
        bridge.Add("settings.githubMirrors", _ => new { providers = _githubMirrors.Providers.Select(provider => new { provider.Id, provider.Name }), results = _githubMirrors.Results });
        bridge.Add("settings.testGitHubMirrors", async (_, ct) =>
        {
            await _githubMirrors.MeasureAsync(ct).ConfigureAwait(false);
            return _githubMirrors.Results;
        });
    }
}
