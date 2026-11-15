using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Warzone;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameServer>();
builder.Services.AddHostedService<GameLoop>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "warzone.guest";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 8192;
    options.MaximumParallelInvocationsPerClient = 1;
    options.AddFilter<GameHubFilter>();
}).AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("session", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseForwardedHeaders();

GameCatalog.Validate();
// Same-origin requests only, including WebSocket handshakes. No wildcard CORS.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/game"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        var expected = $"{context.Request.Scheme}://{context.Request.Host}";
        if ((origin.Length > 0 && !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase))
            || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        { context.Response.StatusCode = 403; return; }
        context.Response.Headers.CacheControl = "no-store";
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapPost("/api/session", async (HttpContext context) =>
{
    var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (id is null)
    {
        id = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
    return Results.Ok(new { playerId = id });
}).RequireRateLimiting("session");
app.MapGet("/api/catalog", () => Results.Ok(GameCatalog.Units.Values));
app.MapGet("/health", () => Results.Ok(new { status = "ok", phase = 5, version = ResolveAppVersion() }));
app.MapGet("/assets/units/{unitId}/{variant}.svg", (string unitId, string variant) =>
    variant is "icon" or "map" && GameCatalog.Units.TryGetValue(unitId, out var unit)
        ? Results.Content(GameCatalog.UnitSprite(unit), "image/svg+xml") : Results.NotFound());
app.MapGet("/assets/tribes/{tribe}/sheet.svg", (string tribe) =>
    Enum.TryParse<TribeType>(tribe, true, out var kind) && Enum.IsDefined(kind)
        ? Results.Content(GameCatalog.TribeSheet(kind), "image/svg+xml") : Results.NotFound());
app.MapGet("/assets/tribes/{tribe}.svg", (string tribe) =>
    GameCatalog.Units.TryGetValue($"{tribe}-infantry", out var unit)
        ? Results.Content(GameCatalog.UnitSprite(unit), "image/svg+xml") : Results.NotFound());
app.MapHub<GameHub>("/game", options => options.CloseOnAuthenticationExpiration = true);

// Build the client first; one origin serves both the client and SignalR.
var clientPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../frontend/dist"));
if (Directory.Exists(clientPath))
{
    var files = new PhysicalFileProvider(clientPath);
    var contentTypes = new FileExtensionContentTypeProvider();
    contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        ContentTypeProvider = contentTypes,
        OnPrepareResponse = ctx =>
        {
            var path = ctx.Context.Request.Path.Value ?? "";
            if (path.Equals("/", StringComparison.Ordinal) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase))
                ctx.Context.Response.Headers.CacheControl = "no-cache";
            else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            else
                ctx.Context.Response.Headers.CacheControl = "public, max-age=604800";
        }
    });
}
app.Run();

static string ResolveAppVersion()
{
    var env = Environment.GetEnvironmentVariable("APP_VERSION");
    if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
    foreach (var dir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            var count = RunGit(dir, "rev-list --count HEAD");
            var sha = RunGit(dir, "rev-parse --short=7 HEAD");
            if (string.IsNullOrWhiteSpace(count) || string.IsNullOrWhiteSpace(sha)) continue;
            var dirty = string.IsNullOrWhiteSpace(RunGit(dir, "status --porcelain")) ? "" : "-dirty";
            return $"v1.0.{count}-{sha}{dirty}";
        }
        catch { }
    }
    return "v1.0.0-local";
}

static string RunGit(string workingDirectory, string arguments)
{
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = "git",
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    });
    if (process is null) return "";
    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit(1500);
    return process.ExitCode == 0 ? output : "";
}
