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
var rawConn = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Postgres");

string? npgsqlConn = null;
bool isPostgres = false;

if (!string.IsNullOrWhiteSpace(rawConn) && !rawConn.Contains("REPLACE_WITH"))
{
    npgsqlConn = ConvertUriToNpgsql(rawConn);
    try
    {
        using var testConn = new Npgsql.NpgsqlConnection(npgsqlConn);
        testConn.Open();
        isPostgres = true;
        Log.Information("Connected to PostgreSQL database successfully.");
    }
    catch (Exception ex)
    {
        Log.Warning("PostgreSQL connection test failed ({Message}). Falling back to SQLite database.", ex.Message);
        isPostgres = false;
    }
}

if (isPostgres && npgsqlConn is not null)
{
    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseNpgsql(npgsqlConn));
}
else
{
    var dbPath = Path.Combine(AppContext.BaseDirectory, "laptopctl.db");
    Log.Information("Using SQLite local database at {Path}", dbPath);
    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseSqlite($"Data Source={dbPath}"));
}

// ─── Authentication (JWT) ─────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Contains("REPLACE_WITH") || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    jwtKey = "LaptopCtlDefaultSecureProductionSigningSecretKey2026!#*";
}

var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

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

// Auto-ensure database schema exists on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        Log.Information("Database schema verified and ready.");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to verify or initialize database schema.");
    }
}

// Normalize redundant slashes in request path (e.g. //api/auth -> /api/auth)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path != null && path.Contains("//"))
    {
        while (path.Contains("//"))
            path = path.Replace("//", "/");
        context.Request.Path = path;
    }
    await next();
});

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

// ─── URI to Npgsql helper ──────────────────────────────────────────────────────
static string ConvertUriToNpgsql(string uriString)
{
    if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
    {
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var db = uri.AbsolutePath.TrimStart('/');
        return $"Host={host};Port={port};Username={username};Password={password};Database={db};SSL Mode=Require;Trust Server Certificate=true;";
    }
    return uriString;
}
