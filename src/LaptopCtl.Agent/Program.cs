using LaptopCtl.Agent;
using LaptopCtl.Agent.Services;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using Serilog.Events;
using System.Runtime.InteropServices;

// Verify platform before anything else
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    Console.Error.WriteLine("LaptopCtl Agent requires Windows 10 or later.");
    return 1;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(AgentPaths.LogDirectory, "agent-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("LaptopCtl Agent v{Version} starting on {OS}",
        AgentVersion.Current,
        RuntimeInformation.OSDescription);

    var builder = Host.CreateApplicationBuilder(args);

    // Windows service support
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "LaptopCtlAgent";
    });

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    // Configuration
    builder.Configuration
        .AddJsonFile(AgentPaths.ConfigFile, optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("LAPTOPCTL_");

    // Core services
    builder.Services.AddHttpClient<BackendClient>()
        .ConfigureHttpClient(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.Add("User-Agent", $"LaptopCtlAgent/{AgentVersion.Current}");
        });

    builder.Services.AddSingleton<AgentIdentityService>();
    builder.Services.AddSingleton<DiagnosticsService>();
    builder.Services.AddSingleton<FileTransferService>();
    builder.Services.AddSingleton<BackendClient>();
    builder.Services.AddHostedService<AgentWorker>();
    builder.Services.AddHostedService<StatusIndicatorService>();

    var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Agent terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
