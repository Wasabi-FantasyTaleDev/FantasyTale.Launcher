namespace FantasyTale.Launcher.Communication;

/// <summary>One short-lived connection grant supplied by the launch authorization service.</summary>
public sealed class CompanionAuthorization
{
    /// <summary>
    /// Creates an in-memory grant; the ticket is never part of its diagnostic representation. Plain <c>ws</c> is accepted
    /// only with <paramref name="allowPlainText"/>, for a server whose launcher API is itself plain http, so the ticket
    /// travels no less protected than the bearer token that obtained it.
    /// </summary>
    public CompanionAuthorization(Uri endpoint, string ticket, bool allowPlainText = false)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);
        if (!endpoint.IsAbsoluteUri || !(endpoint.Scheme == "wss" || allowPlainText && endpoint.Scheme == "ws") || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || ticket.Length > 4096
            || ticket.Any(c => c < 0x21 || c > 0x7e))
            throw new ArgumentException("Invalid companion connection grant.");
        Endpoint = endpoint;
        Ticket = ticket;
    }

    internal Uri Endpoint { get; }
    internal string Ticket { get; }

    /// <summary>Returns a credential-free diagnostic label.</summary>
    public override string ToString() => "CompanionAuthorization [redacted]";
}

/// <summary>Nonsecret launcher-selected identity used during protocol negotiation.</summary>
public sealed record CompanionIdentity(string Game, string Build, string LaunchId, string Locale,
    IReadOnlyDictionary<string, int> Capabilities);

/// <summary>A transport failure with a bounded reason code and no remote payload or credentials.</summary>
public sealed class CompanionConnectionException(string code) : IOException(code);
