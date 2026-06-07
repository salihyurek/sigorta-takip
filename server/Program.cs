using System;
using System.IO;
using System.Linq;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SigortaTakip.Helpers;
using SigortaTakip.Services;

// Load environment variables from .env file at workspace root
var workspaceRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
EnvLoader.Load(workspaceRoot);

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel port (default to 5001, or dynamic port assigned by host environment like Render)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5001";
builder.WebHost.UseUrls($"http://*:{port}");

// Add services to the container
builder.Services.AddControllers();

// Persist Data Protection keys so encrypted SMTP passwords (and any other protected
// data) remain decryptable across restarts. Keys live under the data dir so they
// travel with the persistent disk.
var dataDirEnv = Environment.GetEnvironmentVariable("DATA_DIR");
var keysDir = string.IsNullOrWhiteSpace(dataDirEnv)
    ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data", "keys"))
    : Path.Combine(Path.GetFullPath(dataDirEnv), "keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("SigortaTakip");

// Register Custom Services as Singletons
builder.Services.AddSingleton<TimeService>();
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddSingleton<SchedulerService>();

// Register SchedulerService as a Hosted Service
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchedulerService>());

// Honor X-Forwarded-* headers ONLY when explicitly told we're behind a trusted
// reverse proxy (TRUST_PROXY=true). Otherwise a client could spoof X-Forwarded-For
// to get a fresh rate-limit bucket on every request and brute-force login/reset.
// ForwardLimit=1 means we take only the single hop the proxy itself added (the real
// client), so any client-injected leftmost entries are ignored. Assumes exactly one
// trusted proxy in front (e.g. Render). When enabled this rewrites RemoteIpAddress
// to the real client so the rate limiter partitions correctly.
var trustProxy = string.Equals(
    Environment.GetEnvironmentVariable("TRUST_PROXY"), "true", StringComparison.OrdinalIgnoreCase);
if (trustProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// Configure CORS. In production the API and SPA are served from the same origin so
// cross-origin requests are not needed. In development the Vite dev server (5173)
// talks to the API, so allow it. Extra origins can be supplied via ALLOWED_ORIGINS
// (comma separated).
var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToList();
if (builder.Environment.IsDevelopment())
{
    allowedOrigins.AddRange(new[]
    {
        "http://localhost:5173", "http://127.0.0.1:5173"
    });
}
allowedOrigins = allowedOrigins.Distinct().ToList();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        if (allowedOrigins.Count > 0)
        {
            policy.WithOrigins(allowedOrigins.ToArray())
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        // No origins configured => same-origin only (no CORS headers emitted).
    });
});

// Configure Rate Limiting, partitioned per client IP so one user fumbling their
// password cannot lock out everyone else (a single global window would).
// RemoteIpAddress is the real client (rewritten by ForwardedHeaders when TRUST_PROXY
// is on, the direct peer otherwise) — never the raw, spoofable X-Forwarded-For header.
static string GetClientKey(HttpContext ctx)
    => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Çok fazla deneme yaptınız. Lütfen daha sonra tekrar deneyin."
        }, token);
    };

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(GetClientKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(15),
            PermitLimit = 10,
            QueueLimit = 0
        }));

    options.AddPolicy("forgot-password", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(GetClientKey(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromHours(1),
            PermitLimit = 3,
            QueueLimit = 0
        }));
});

var app = builder.Build();

// Must run before anything that reads RemoteIpAddress (rate limiter, logging).
if (trustProxy)
{
    app.UseForwardedHeaders();
}

// Security headers (cheap, defensive). CSP allows inline styles because the UI uses
// style attributes; all scripts are bundled, so script-src stays locked to 'self'.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-XSS-Protection"] = "0";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("AppCors");
app.UseRateLimiter();

// Custom Middleware for Session Token authentication
app.Use(async (context, next) =>
{
    var authService = context.RequestServices.GetRequiredService<AuthService>();
    var authHeader = context.Request.Headers["Authorization"].ToString();

    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var token = authHeader.Substring(7).Trim();
        var session = authService.ValidateSession(token);
        if (session != null)
        {
            context.Items["Session"] = session;
            context.Items["Token"] = token;
        }
    }

    await next();
});

app.MapControllers();

// Serve static assets in production (Vite build folder: dist)
var distPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "dist"));
if (Directory.Exists(distPath))
{
    Console.WriteLine($"[Server] Serving production client files from: {distPath}");
    var fileProvider = new PhysicalFileProvider(distPath);

    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fileProvider
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider
    });

    // SPA Routing Fallback
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = fileProvider
    });
}
else
{
    Console.WriteLine("[Server] Development mode: 'dist' folder not found. Relying on frontend dev server proxy.");
}

Console.WriteLine($"[Server] Starting ASP.NET Core Web API on port {port}");
app.Run();
