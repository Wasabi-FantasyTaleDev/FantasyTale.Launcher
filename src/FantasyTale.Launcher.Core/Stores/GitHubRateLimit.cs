using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace FantasyTale.Launcher.Core.Stores;

// Public API quotas apply across repositories, so every source in one catalog cache shares the server's retry deadline.
internal sealed class GitHubRateLimit(TimeProvider? clock = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset _resume;

    internal bool Active
    {
        get { lock (_gate) return _resume > _clock.GetUtcNow(); }
    }

    internal void Observe(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)) return;
        var now = _clock.GetUtcNow();
        var after = RetryConditionHeaderValue.TryParse(Header(response, "Retry-After"), out var parsed) ? parsed : null;
        var resume = after?.Date;
        if (after?.Delta is { } delta && delta > TimeSpan.Zero && delta <= DateTimeOffset.MaxValue - now)
            resume = now + delta;
        if (Header(response, "X-RateLimit-Remaining") == "0" &&
            long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch) &&
            epoch >= 0 && epoch <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
            if (resume is null || reset > resume) resume = reset;
        }
        if (resume is not { } deadline || deadline <= now) return;
        lock (_gate) if (deadline > _resume) _resume = deadline;
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var fields = values.Take(2).ToArray();
        return fields.Length == 1 ? fields[0] : null;
    }
}
