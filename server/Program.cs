using System;
using System.IO;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

// Register Custom Services as Singletons
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddSingleton<SchedulerService>();

// Register SchedulerService as a Hosted Service
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchedulerService>());

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure Rate Limiting (Parity with Node's express-rate-limit)
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

    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(15);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("forgot-password", opt =>
    {
        opt.Window = TimeSpan.FromHours(1);
        opt.PermitLimit = 3;
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseRateLimiter();

// Custom Middleware for Session Token authentication (Parity with requireAuth in Node)
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
