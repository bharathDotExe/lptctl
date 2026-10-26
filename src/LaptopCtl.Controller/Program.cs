using System.CommandLine;
using LaptopCtl.Controller;
using LaptopCtl.Controller.Commands;
using Spectre.Console;

AnsiConsole.Write(new FigletText("LaptopCtl").Color(Color.CornflowerBlue));
AnsiConsole.MarkupLine($"[grey]Controller CLI v{ControllerVersion.Current}[/]");
AnsiConsole.WriteLine();

var creds  = new CredentialStore();
var client = new ApiClient(creds);

// ─── Global options ───────────────────────────────────────────────────────────
var backendOpt = new Option<string>(
    new[] { "--backend", "-b" },
    description: "LaptopCtl backend URL",
    getDefaultValue: () => creds.BackendUrl ?? "https://api.laptopctl.example.com");

var root = new RootCommand("LaptopCtl - Remote Windows administration platform");
root.AddGlobalOption(backendOpt);

// ─── laptopctl login ─────────────────────────────────────────────────────────
var loginCmd  = new Command("login",  "Authenticate with LaptopCtl backend");
var emailOpt  = new Option<string?>("--email", "Account email address");
var pwdOpt    = new Option<string?>("--password", "Account password (omit to prompt securely)");
loginCmd.AddOption(emailOpt);
loginCmd.AddOption(pwdOpt);
loginCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await AuthHandler.LoginAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForOption(emailOpt),
        ctx.ParseResult.GetValueForOption(pwdOpt),
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(loginCmd);

// ─── laptopctl logout ────────────────────────────────────────────────────────
var logoutCmd = new Command("logout", "End your LaptopCtl session");
logoutCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await AuthHandler.LogoutAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(logoutCmd);

// ─── laptopctl devices ───────────────────────────────────────────────────────
var devicesCmd = new Command("devices", "List enrolled devices");
devicesCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DeviceHandler.ListAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(devicesCmd);

// ─── laptopctl status <device-id> ────────────────────────────────────────────
var deviceIdArg  = new Argument<string>("device-id", "Target device ID");
var statusCmd    = new Command("status", "Show device status") { deviceIdArg };
statusCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DeviceHandler.StatusAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(statusCmd);

// ─── laptopctl connect <device-id> ───────────────────────────────────────────
var connectCmd = new Command("connect", "Open a session to a device") { deviceIdArg };
connectCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DeviceHandler.ConnectAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(connectCmd);

// ─── laptopctl info <device-id> ──────────────────────────────────────────────
var infoCmd = new Command("info", "Get system information from a device") { deviceIdArg };
infoCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DiagnosticHandler.RunAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        "system.info", creds, client, ctx.GetCancellationToken());
});
root.AddCommand(infoCmd);

// ─── laptopctl processes <device-id> ─────────────────────────────────────────
var procsCmd = new Command("processes", "List processes on a device") { deviceIdArg };
procsCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DiagnosticHandler.RunAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        "processes", creds, client, ctx.GetCancellationToken());
});
root.AddCommand(procsCmd);

// ─── laptopctl disk <device-id> ──────────────────────────────────────────────
var diskCmd = new Command("disk", "Get disk usage from a device") { deviceIdArg };
diskCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DiagnosticHandler.RunAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        "disks", creds, client, ctx.GetCancellationToken());
});
root.AddCommand(diskCmd);

// ─── laptopctl network <device-id> ───────────────────────────────────────────
var netCmd = new Command("network", "Get network interface info from a device") { deviceIdArg };
netCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DiagnosticHandler.RunAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        "network", creds, client, ctx.GetCancellationToken());
});
root.AddCommand(netCmd);

// ─── laptopctl files ─────────────────────────────────────────────────────────
var filesCmd  = new Command("files", "File transfer commands");

var remotePathArg = new Argument<string>("remote-path", "Path on the remote device");
var localSrcArg   = new Argument<string>("source", "Local source file path");
var localDstArg   = new Argument<string>("destination", "Local destination path");

var filesListCmd  = new Command("list", "List files in a remote directory") { deviceIdArg, remotePathArg };
filesListCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await FileHandler.ListAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        ctx.ParseResult.GetValueForArgument(remotePathArg),
        creds, client, ctx.GetCancellationToken());
});

var filesUpCmd    = new Command("upload", "Upload a file to a device") { deviceIdArg, localSrcArg, remotePathArg };
filesUpCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await FileHandler.UploadAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        ctx.ParseResult.GetValueForArgument(localSrcArg),
        ctx.ParseResult.GetValueForArgument(remotePathArg),
        creds, client, ctx.GetCancellationToken());
});

var filesDlCmd    = new Command("download", "Download a file from a device") { deviceIdArg, remotePathArg, localDstArg };
filesDlCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await FileHandler.DownloadAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        ctx.ParseResult.GetValueForArgument(remotePathArg),
        ctx.ParseResult.GetValueForArgument(localDstArg),
        creds, client, ctx.GetCancellationToken());
});

filesCmd.AddCommand(filesListCmd);
filesCmd.AddCommand(filesUpCmd);
filesCmd.AddCommand(filesDlCmd);
root.AddCommand(filesCmd);

// ─── laptopctl sessions ───────────────────────────────────────────────────────
var sessionsCmd = new Command("sessions", "List active sessions");
sessionsCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await SessionHandler.ListAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(sessionsCmd);

// ─── laptopctl disconnect <device-id> ────────────────────────────────────────
var disconnectCmd = new Command("disconnect", "Terminate the active session for a device") { deviceIdArg };
disconnectCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await SessionHandler.DisconnectAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(disconnectCmd);

// ─── laptopctl exec <device-id> <command> ────────────────────────────────────
var execCmdArg = new Argument<string>("command", "PowerShell or administrative command to execute");
var execCmd = new Command("exec", "Execute an administrative command remotely on a device") { deviceIdArg, execCmdArg };
execCmd.AddAlias("shell");
execCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await CommandHandler.ExecAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        ctx.ParseResult.GetValueForArgument(execCmdArg),
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(execCmd);

// ─── laptopctl enroll-code ───────────────────────────────────────────────────
var enrollCodeCmd = new Command("enroll-code", "Generate a new device enrollment code and installer command");
enrollCodeCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await CommandHandler.GenerateEnrollmentCodeAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(enrollCodeCmd);

// ─── laptopctl uninstall <device-id> ─────────────────────────────────────────
var uninstallCmd = new Command("uninstall", "Revoke device enrollment (DESTRUCTIVE)") { deviceIdArg };
uninstallCmd.SetHandler(async (ctx) =>
{
    ctx.ExitCode = await DeviceHandler.UninstallAsync(
        ctx.ParseResult.GetValueForOption(backendOpt)!,
        ctx.ParseResult.GetValueForArgument(deviceIdArg),
        creds, client, ctx.GetCancellationToken());
});
root.AddCommand(uninstallCmd);

return await root.InvokeAsync(args);
