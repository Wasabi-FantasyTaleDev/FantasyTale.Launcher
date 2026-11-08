using System.Net;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>A structured server rejection or transport failure containing only public diagnostic fields.</summary>
public sealed class ServerFailureException : HttpRequestException
{
    /// <summary>Creates a failure without retaining the request body, credentials, response body or request URL.</summary>
    public ServerFailureException(string code, HttpStatusCode? status = null, string? transportCode = null,
        string? reason = null, DateTimeOffset? maintenanceStart = null, DateTimeOffset? maintenanceEnd = null, string? maintenanceDescription = null)
        : base("Server request failed: " + code + ".", null, status)
    { Code = code; TransportCode = transportCode; Reason = reason; MaintenanceStart = maintenanceStart; MaintenanceEnd = maintenanceEnd; MaintenanceDescription = maintenanceDescription; }
    /// <summary>Stable machine-readable failure code translated by the frontend.</summary>
    public string Code { get; }
    /// <summary>Runtime transport category such as NameResolutionError or timeout, without network request contents.</summary>
    public string? TransportCode { get; }
    /// <summary>Optional public operator explanation, including a ban reason.</summary>
    public string? Reason { get; }
    /// <summary>Optional maintenance start with an explicit time-zone offset.</summary>
    public DateTimeOffset? MaintenanceStart { get; }
    /// <summary>Optional maintenance end with an explicit time-zone offset.</summary>
    public DateTimeOffset? MaintenanceEnd { get; }
    /// <summary>Optional operator-provided maintenance explanation rendered as plain text.</summary>
    public string? MaintenanceDescription { get; }
}
