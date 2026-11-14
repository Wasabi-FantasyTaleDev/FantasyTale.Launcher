using System.Diagnostics;

namespace FantasyTale.Launcher.Sdk;

/// <summary>How long <see cref="GameNativeUiInvocation.InvokeUntilDoneAsync"/> waits for one native operation.</summary>
/// <param name="Timeout">The whole budget, from the first submission to the last result read.</param>
/// <param name="PollInterval">The pause between a rejected submission and its retry, and between result reads.</param>
public sealed record GameNativeUiWaitOptions(TimeSpan Timeout, TimeSpan PollInterval)
{
    /// <summary>Rejects a budget or interval that is not positive, or an interval longer than the budget.</summary>
    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero || PollInterval <= TimeSpan.Zero || PollInterval > Timeout)
            throw new ArgumentException("A native UI wait needs a positive timeout and a positive poll interval no longer than it.");
    }
}

/// <summary>Submits a native operation and waits for its outcome, the loop every native UI caller otherwise rewrites.</summary>
public static class GameNativeUiInvocation
{
    /// <summary>The status returned when the budget ran out while the operation was still queued in the game.</summary>
    public const string OutcomeUnknown = "outcomeUnknown";

    /// <summary>
    /// Submits <paramref name="request"/>, retries the submission while the channel reports <c>channelBusy</c> (the
    /// operation was not submitted), then reads its result until it leaves the <c>queued</c> state. An admitted operation
    /// is never submitted again: when the budget runs out while it is queued the result is <see cref="OutcomeUnknown"/>
    /// with reason <c>nativeReplyUnavailable</c>, because the game may still perform it.
    /// </summary>
    /// <exception cref="ArgumentException">The request or the options are invalid.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled; an admitted operation may still complete.</exception>
    public static async Task<GameNativeUiResult> InvokeUntilDoneAsync(this IGameNativeUi native, GameNativeUiRequest request,
        GameNativeUiWaitOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        request.Validate();
        options.Validate();
        var started = Stopwatch.GetTimestamp();
        bool Expired() => Stopwatch.GetElapsedTime(started) >= options.Timeout;
        var result = await native.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        while (result.Status == "busy" && result.Reason == "channelBusy" && !Expired())
        {
            await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
            result = await native.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        while (result.Status == "queued")
        {
            if (Expired()) return new(request.OperationId, OutcomeUnknown, Reason: "nativeReplyUnavailable");
            await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
            // A rejected result query leaves the operation's outcome unchanged; read it again on the next pass.
            try { result = await native.GetResultAsync(request.OperationId, cancellationToken).ConfigureAwait(false); }
            catch (GameNativeUiBusyException) { }
        }
        return result;
    }
}
