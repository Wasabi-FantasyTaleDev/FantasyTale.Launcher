namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Safe diagnostics from the selected release's last unsuccessful external installation.</summary>
/// <param name="Status">failed, rolled-back or recovery-required; successful attempts are omitted.</param>
/// <param name="Failure">Allowlisted storage, startup or integrity category, when recorded.</param>
/// <param name="Phase">Allowlisted installation operation, when recorded by the helper.</param>
/// <param name="ErrorCode">Numeric HRESULT from the helper, without exception text or source locations.</param>
public sealed record LauncherUpdateAttempt(string Status, string? Failure, string? Phase, int? ErrorCode)
{
    internal static LauncherUpdateAttempt? From(LauncherUpdateHandoff.Outcome? outcome, string sourceIdentity, string manifestSha256)
    {
        if (outcome is null || outcome.SourceIdentity != sourceIdentity || outcome.ManifestSha256 != manifestSha256 ||
            outcome.Status is not ("failed" or "rolled-back" or "recovery-required")) return null;

        var failure = outcome.Failure is "disk" or "startup" or "integrity" or "network" ? outcome.Failure : null;
        var phase = outcome.Phase is "handoff" or "staged-verification" or "parent-exit" or "installation-lock" or
            "recovery" or "staging" or "replacement" or "frontend-startup" or "installed-verification" or "commit" ? outcome.Phase : null;
        return new(outcome.Status, failure, phase, outcome.ErrorCode);
    }
}
