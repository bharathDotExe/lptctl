using AspNetCoreRateLimit;
using LaptopCtl.Backend.Data;
using LaptopCtl.Backend.Services;
using LaptopCtl.Backend.WebSockets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ─── Logging ─────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ─── Database ─────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Connection string 'Postgres' is not configured. Set ConnectionStrings__Postgres.");

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connStr));

// ─── Authentication (JWT) ─────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException("JWT:Key is not configured. Set Jwt__Key.");

var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
if (keyBytes.Length < 32)
    throw new InvalidOperationException("JWT:Key must be at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidAudience            = jwtSection["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(keyBytes),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };

        // Support JWT in WebSocket upgrade query string
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/ws/controller"))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ─── Rate limiting ────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ─── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));

// ─── App services ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<EnrollmentService>();
builder.Services.AddSingleton<AgentConnectionManager>();
builder.Services.AddSingleton<AgentRelayService>();

// ─── Controllers ─────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();

// ─── Build app ────────────────────────────────────────────────────────────────
var app = builder.Build();

// Auto-migrate on startup (for development; production should use explicit migration)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseIpRateLimiting();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// WebSocket endpoints
app.Map("/ws/agent/{deviceId}", async (HttpContext ctx, string deviceId, AgentConnectionManager mgr) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await mgr.HandleAgentConnectionAsync(deviceId, ws, ctx);
});

app.Map("/ws/controller/{deviceId}", async (HttpContext ctx, string deviceId, AgentRelayService relay) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    if (ctx.User?.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = 401; return; }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await relay.HandleControllerConnectionAsync(deviceId, ws, ctx);
});

app.MapControllers();
app.MapHealthChecks("/health");

// ─── Direct installer script endpoint ─────────────────────────────────────────
app.MapGet("/install.ps1", async (HttpContext ctx) =>
{
    var candidates = new[]
    {
        Path.Combine(app.Environment.ContentRootPath, "..", "..", "installer", "install.ps1"),
        Path.Combine(app.Environment.ContentRootPath, "installer", "install.ps1"),
        Path.Combine(AppContext.BaseDirectory, "installer", "install.ps1")
    };

    var scriptPath = candidates.FirstOrDefault(File.Exists);
    if (scriptPath is not null)
    {
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.SendFileAsync(scriptPath);
    }
    else
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("# LaptopCtl installer script not found on server.");
    }
});

// ─── Dashboard static files ───────────────────────────────────────────────────
var dashboardCandidates = new[]
{
    Path.Combine(app.Environment.ContentRootPath, "..", "..", "dashboard"),
    Path.Combine(app.Environment.ContentRootPath, "dashboard"),
    Path.Combine(AppContext.BaseDirectory, "dashboard")
};
var dashboardDir = dashboardCandidates.FirstOrDefault(Directory.Exists);
if (dashboardDir is not null)
{
    app.UseFileServer(new FileServerOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(dashboardDir)),
        RequestPath = "",
        EnableDefaultFiles = true
    });
}

Log.Information("LaptopCtl Backend starting on {Env}", app.Environment.EnvironmentName);
await app.RunAsync();
