using Spectre.Console;

namespace LaptopCtl.Controller.Commands;

public static class AuthHandler
{
    public static async Task<int> LoginAsync(
        string backend, string? email, string? password,
        CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!creds.IsLoggedIn == false)
        {
            AnsiConsole.MarkupLine($"[yellow]Already logged in as {creds.Email}. Run [bold]logout[/] first.[/]");
        }

        email    ??= AnsiConsole.Ask<string>("Email address:");
        password ??= AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());

        try
        {
            var result = await AnsiConsole.Status()
                .StartAsync("Authenticating...", async _ =>
                    await client.LoginAsync(backend, email, password, ct));

            creds.Save(backend, email, result.AccessToken, result.RefreshToken, result.ExpiresAt);

            AnsiConsole.MarkupLine($"[green]✓[/] Logged in as [bold]{result.Email}[/]");
            AnsiConsole.MarkupLine($"  Backend: {backend}");
            AnsiConsole.MarkupLine($"  Session expires: {result.ExpiresAt:f}");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Login failed:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
            return 2;
        }
    }

    public static async Task<int> LogoutAsync(
        string backend, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!creds.IsLoggedIn)
        {
            AnsiConsole.MarkupLine("[yellow]Not currently logged in.[/]");
            return 0;
        }

        var rt = creds.RefreshToken ?? string.Empty;
        try { await client.LogoutAsync(backend, rt, ct); }
        catch { /* best effort server-side revocation */ }

        var email = creds.Email;
        creds.Clear();
        AnsiConsole.MarkupLine($"[green]✓[/] Logged out ({email}).");
        return 0;
    }
}
