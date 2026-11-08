using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;
using Microsoft.AspNetCore.RateLimiting;

namespace FantasyTale.Launcher.DirectoryService;

/// <summary>Deployment settings for the independent directory API.</summary>
public sealed class DirectoryOptions
{
    /// <summary>HTTP listener, normally loopback behind the operator's reverse proxy.</summary>
    public string Listen { get; set; } = "http://127.0.0.1:8096";
    /// <summary>Directory for reviewed server and pending submission state.</summary>
    public string DataPath { get; set; } = "directory-data";
    /// <summary>Plain-text source name returned by discovery.</summary>
    public string Name { get; set; } = "Fantasy Tale";
    /// <summary>Private source administrator bearer token, supplied by deployment configuration.</summary>
    public string AdminToken { get; set; } = "";
    /// <summary>Maximum approved servers, pending submissions and rooms, separately.</summary>
    public int MaximumEntries { get; set; } = 512;
    /// <summary>Room lifetime since its last successful heartbeat.</summary>
    public int RoomLeaseSeconds { get; set; } = 90;
    /// <summary>Explicit ip or domain access policy; auto inherits the launcher's preference.</summary>
    public string HostPolicy { get; set; } = "auto";
    /// <summary>Optional two-letter deployment region, such as CN.</summary>
    public string? Region { get; set; }
    /// <summary>Global read budget per minute; it bounds anonymous list traffic without an unbounded IP cache.</summary>
    public int ReadRequestsPerMinute { get; set; } = 1200;
    /// <summary>Global submission and publication budget per minute.</summary>
    public int WriteRequestsPerMinute { get; set; } = 120;
    /// <summary>Separate heartbeat and withdrawal budget so publications cannot starve existing room renewals.</summary>
    public int LeaseRequestsPerMinute { get; set; } = 8192;
}

/// <summary>Owns a self-hostable directory API; it neither starts game servers nor fetches submitted URLs.</summary>
public sealed class DirectoryHttp : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly DirectoryRegistry _registry;
    private DirectoryHttp(WebApplication app, DirectoryRegistry registry) { _app = app; _registry = registry; }

    /// <summary>Actual bound addresses, including the selected port when a test requests port zero.</summary>
    public IReadOnlyList<string> Addresses => _app.Urls.ToArray();

    /// <summary>Validates deployment settings and starts bounded HTTP endpoints with private administration.</summary>
    public static async Task<DirectoryHttp> StartAsync(DirectoryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        DirectoryValidation.Text(options.Name, 128);
        if (new[] { options.ReadRequestsPerMinute, options.WriteRequestsPerMinute, options.LeaseRequestsPerMinute }.Any(n => n is < 1 or > 100000))
            throw new ArgumentException("Directory request budgets must be between 1 and 100000 per minute.");
        if (options.AdminToken.Length is < 16 or > 256 || options.AdminToken.Any(char.IsControl)) throw new ArgumentException("Configure a 16..256 character directory administrator token.");
        var region = string.IsNullOrWhiteSpace(options.Region) ? null : options.Region;
        if (options.HostPolicy is not ("auto" or "ip" or "domain") || region is not null && (region.Length != 2 || !region.All(char.IsAsciiLetterUpper)))
            throw new ArgumentException("Invalid directory network policy.");
        var listen = ServerStore.ApiBase(options.Listen);
        if (listen.AbsolutePath != "/" || listen.Scheme != "http") throw new ArgumentException("Use an HTTP listener root behind a TLS proxy when needed.");
        var registry = new DirectoryRegistry(options.DataPath, options.MaximumEntries, options.RoomLeaseSeconds);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(DirectoryHttp).Assembly.FullName });
        builder.WebHost.UseUrls(options.Listen);
        builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxConcurrentConnections = 64; k.Limits.MaxRequestBodySize = 16384; k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(j => { j.SerializerOptions.MaxDepth = 12; j.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; });
        builder.Services.Configure<RouteHandlerOptions>(r => r.ThrowOnBadRequest = true);
        builder.Services.AddRateLimiter(r =>
        {
            r.RejectionStatusCode = 429;
            r.AddFixedWindowLimiter("read", l => { l.PermitLimit = options.ReadRequestsPerMinute; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0; l.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; });
            r.AddFixedWindowLimiter("write", l => { l.PermitLimit = options.WriteRequestsPerMinute; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0; l.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; });
            r.AddFixedWindowLimiter("lease", l => { l.PermitLimit = options.LeaseRequestsPerMinute; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0; l.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; });
        });
        var app = builder.Build();
        var adminHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.AdminToken));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                if (context.Request.Path.StartsWithSegments("/v1/admin") && !CryptographicOperations.FixedTimeEquals(adminHash, SHA256.HashData(Encoding.UTF8.GetBytes(Bearer(context)))))
                    throw new UnauthorizedAccessException();
                await next(context).ConfigureAwait(false);
            }
            catch (Exception error) when (!context.Response.HasStarted && error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or JsonException or BadHttpRequestException or IOException)
            {
                var status = error switch { UnauthorizedAccessException => 401, KeyNotFoundException => 404, BadHttpRequestException e => e.StatusCode, InvalidOperationException => 409, IOException => 503, _ => 400 };
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { error = status switch { 401 => "unauthorized", 404 => "not_found", 409 => "conflict_or_capacity", 503 => "storage_unavailable", _ => "invalid_request" } }, context.RequestAborted).ConfigureAwait(false);
            }
        });
        app.UseRateLimiter();
        var info = new DirectoryServiceInfo(1, options.Name, options.RoomLeaseSeconds, options.HostPolicy, region);
        app.MapGet("/v1/info", () => info).RequireRateLimiting("read");
        app.MapGet("/v1/entries", registry.List).RequireRateLimiting("read");
        app.MapPost("/v1/submissions", (DirectoryAdvertisement details) => Results.Json(registry.Submit(details), statusCode: 201)).RequireRateLimiting("write");
        app.MapPost("/v1/rooms", (DirectoryAdvertisement details) => Results.Json(registry.Publish(details), statusCode: 201)).RequireRateLimiting("write");
        app.MapPost("/v1/rooms/{id}/heartbeat", (string id, HttpContext context) => registry.Renew(id, Bearer(context))).RequireRateLimiting("lease");
        app.MapDelete("/v1/rooms/{id}", (string id, HttpContext context) => { registry.Withdraw(id, Bearer(context)); return Results.NoContent(); }).RequireRateLimiting("lease");
        app.MapGet("/v1/admin/submissions", registry.Pending).RequireRateLimiting("read");
        app.MapPost("/v1/admin/submissions/{id}/approve", (string id) => { registry.Review(id, true); return Results.NoContent(); }).RequireRateLimiting("write");
        app.MapPost("/v1/admin/submissions/{id}/reject", (string id) => { registry.Review(id, false); return Results.NoContent(); }).RequireRateLimiting("write");
        app.MapDelete("/v1/admin/servers/{id}", (string id) => { registry.RemoveServer(id); return Results.NoContent(); }).RequireRateLimiting("write");
        try { await app.StartAsync(ct).ConfigureAwait(false); return new(app, registry); }
        catch { await app.DisposeAsync().ConfigureAwait(false); registry.Dispose(); throw; }
    }

    private static string Bearer(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.Ordinal) && value.Length <= 263 ? value[7..] : "";
    }

    /// <summary>Waits for the service lifetime signal used by console and service managers.</summary>
    public Task WaitForShutdownAsync(CancellationToken ct = default) => _app.WaitForShutdownAsync(ct);

    /// <summary>Drains active HTTP requests before releasing persisted state and invalidating room leases.</summary>
    public async ValueTask DisposeAsync()
    {
        try { await _app.StopAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); await _app.DisposeAsync().ConfigureAwait(false); }
        finally { _registry.Dispose(); }
    }
}
