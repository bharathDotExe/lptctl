using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace LaptopCtl.Agent.Services;

/// <summary>
/// Launches interactive GUI applications, URLs (browsers), and File Explorer folders
/// inside the active logged-on user's interactive Windows desktop session (escaping Session 0 Isolation).
/// </summary>
public static class InteractiveLauncher
{
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int ImpersonationLevel,
        int TokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    /// <summary>
    /// Checks if a command line looks like an interactive GUI or URL open request.
    /// </summary>
    public static bool IsInteractiveCommand(string command, out string target)
    {
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(command)) return false;

        string trimmed = command.Trim();

        // 1. Explicit GUI / interactive prefix: "gui <command>" or "interactive <command>"
        if (trimmed.StartsWith("gui ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(4).Trim();
            return true;
        }
        if (trimmed.StartsWith("interactive ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(12).Trim();
            return true;
        }

        // 2. Keyboard typing helper: "type <text>" or "sendkeys <text>"
        if (trimmed.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
        {
            string textToType = trimmed.Substring(5).Trim().Trim('"', '\'');
            string escaped = EscapeSendKeys(textToType);
            target = $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait(\"{escaped}\")";
            return true;
        }
        if (trimmed.StartsWith("sendkeys ", StringComparison.OrdinalIgnoreCase))
        {
            string textToType = trimmed.Substring(9).Trim().Trim('"', '\'');
            target = $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait(\"{textToType}\")";
            return true;
        }

        // 3. Mouse click helper: "click", "click <x> <y>", "rightclick", "doubleclick"
        if (trimmed.StartsWith("click", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("rightclick", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("doubleclick", StringComparison.OrdinalIgnoreCase))
        {
            target = BuildMouseCommand(trimmed);
            return true;
        }

        // 4. Hotkey helper: "hotkey <key>" (e.g. "hotkey win+d", "hotkey win+l", "hotkey alt+f4")
        if (trimmed.StartsWith("hotkey ", StringComparison.OrdinalIgnoreCase))
        {
            target = BuildHotkeyCommand(trimmed.Substring(7).Trim());
            return true;
        }

        // 5. Native popup message box: "msg * <text>" or "alert <text>"
        if (trimmed.StartsWith("msg *", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("alert ", StringComparison.OrdinalIgnoreCase))
        {
            string msg = trimmed.StartsWith("alert ", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(6).Trim().Trim('"', '\'')
                : trimmed.Substring(5).Trim().Trim('"', '\'');
            target = $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show(\"{msg.Replace("\"", "\\\"")}\", 'LaptopCtl Administrator Notification', 'OK', 'Information')";
            return true;
        }

        // 6. Scripts containing SendKeys, Start-Process notepad/calc/mspaint, or GUI scripts
        if (trimmed.Contains("SendKeys", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Start-Process notepad", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Start-Process calc", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Start-Process mspaint", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("[System.Windows.Forms.", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed;
            return true;
        }

        // 7. Standard open/browse/start
        if (trimmed.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(5).Trim().Trim('"', '\'');
            return true;
        }

        if (trimmed.StartsWith("browse ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(7).Trim().Trim('"', '\'');
            return true;
        }

        if (trimmed.StartsWith("start ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(6).Trim();
            return true;
        }

        // 8. Direct URL or simple GUI app launch
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtube", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("explorer", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("calc.exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("notepad", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("taskmgr", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Launches the given target (URL, folder, application) into the active interactive desktop session.
    /// </summary>
    public static bool TryLaunchInActiveSession(string rawTarget, out string output)
    {
        string target = NormalizeTarget(rawTarget);

        // Determine active session ID and user name
        int sessionId = GetActiveSessionId();
        string userName = GetActiveUserName();

        // Build command line to execute inside the user session
        string commandLine = BuildUserCommandLine(target);

        // Attempt 1: Windows API CreateProcessAsUser
        if (sessionId > 0 && TryCreateProcessAsUser(sessionId, commandLine, out var procError))
        {
            output = $"[LaptopCtl Interactive] Successfully executed on user screen!\nAction: {target}\nUser: {userName}\nSession: {sessionId}";
            return true;
        }

        // Attempt 2: Task Scheduler interactive launch (/IT /RU)
        if (!string.IsNullOrEmpty(userName) && TryTaskSchedulerInteractive(userName, commandLine, out var taskError))
        {
            output = $"[LaptopCtl Interactive] Successfully executed on user screen via Task Scheduler!\nAction: {target}\nUser: {userName}\nSession: {sessionId}";
            return true;
        }

        // Fallback: Run standard Process.Start
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" {commandLine}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            output = $"[LaptopCtl] Dispatched '{target}'. (Note: Active desktop session was {sessionId}, user: {userName})";
            return true;
        }
        catch (Exception ex)
        {
            output = $"Failed to launch interactive target '{target}': {ex.Message}";
            return false;
        }
    }

    private static string NormalizeTarget(string raw)
    {
        string target = raw.Trim().Trim('"', '\'');
        if (target.Equals("youtube", StringComparison.OrdinalIgnoreCase))
            return "https://www.youtube.com";
        if (target.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return "https://" + target;

        // Only strip Start-Process if it's a simple single launch, not a multi-command script
        if (target.StartsWith("Start-Process", StringComparison.OrdinalIgnoreCase) && !target.Contains(';'))
        {
            var parts = target.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) return parts[1].Trim().Trim('"', '\'');
        }
        return target;
    }

    private static string BuildUserCommandLine(string target)
    {
        // If target contains PowerShell script constructs, execute it via powershell -EncodedCommand
        if (target.Contains(';') ||
            target.Contains("SendKeys", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("Start-Process", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("Add-Type", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("$", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("New-Object", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("::"))
        {
            string psScript = "$ProgressPreference = 'SilentlyContinue'; " + target;
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
            return $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {b64}";
        }

        // For URLs, folders, or document paths, 'cmd.exe /c start "" "target"' delegates to Windows Shell
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            (target.Length >= 2 && target[1] == ':') ||
            target.StartsWith(@"\\"))
        {
            return $"cmd.exe /c start \"\" \"{target}\"";
        }

        // If it's an explicit explorer command
        if (target.StartsWith("explorer", StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        // If it's another application or command
        return $"cmd.exe /c start \"\" {target}";
    }

    private static string BuildMouseCommand(string cmd)
    {
        var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string type = parts[0].ToLowerInvariant();
        int x = 0, y = 0;
        bool hasCoords = false;

        if (parts.Length >= 3 && int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y))
        {
            hasCoords = true;
        }

        string setPos = hasCoords
            ? $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point({x}, {y}); "
            : "";

        string clickCode = type switch
        {
            "rightclick" => @"$code = '[DllImport(""user32.dll"")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);'; Add-Type -MemberDefinition $code -Name Win32Input -Namespace Win32; [Win32.Win32Input]::mouse_event(0x08, 0, 0, 0, 0); [Win32.Win32Input]::mouse_event(0x10, 0, 0, 0, 0)",
            "doubleclick" => @"$code = '[DllImport(""user32.dll"")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);'; Add-Type -MemberDefinition $code -Name Win32Input -Namespace Win32; [Win32.Win32Input]::mouse_event(0x02, 0, 0, 0, 0); [Win32.Win32Input]::mouse_event(0x04, 0, 0, 0, 0); Start-Sleep -Milliseconds 100; [Win32.Win32Input]::mouse_event(0x02, 0, 0, 0, 0); [Win32.Win32Input]::mouse_event(0x04, 0, 0, 0, 0)",
            _ => @"$code = '[DllImport(""user32.dll"")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);'; Add-Type -MemberDefinition $code -Name Win32Input -Namespace Win32; [Win32.Win32Input]::mouse_event(0x02, 0, 0, 0, 0); [Win32.Win32Input]::mouse_event(0x04, 0, 0, 0, 0)"
        };

        return setPos + clickCode;
    }

    private static string BuildHotkeyCommand(string key)
    {
        string norm = key.ToLowerInvariant().Replace(" ", "");
        return norm switch
        {
            "win+l" or "lock" => "rundll32.exe user32.dll,LockWorkStation",
            "win+d" or "desktop" => "(New-Object -ComObject Shell.Application).ToggleDesktop()",
            "win+r" or "run" => "(New-Object -ComObject Shell.Application).FileRun()",
            "alt+f4" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('%{F4}')",
            "alt+tab" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('%{TAB}')",
            "taskmgr" or "ctrl+shift+esc" => "taskmgr.exe",
            "enter" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')",
            "tab" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{TAB}')",
            "esc" or "escape" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{ESC}')",
            "backspace" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{BS}')",
            "space" => "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait(' ')",
            _ => $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{key}')"
        };
    }

    private static string EscapeSendKeys(string text)
    {
        return text
            .Replace("{", "{{}")
            .Replace("}", "{}}")
            .Replace("+", "{+}")
            .Replace("^", "{^}")
            .Replace("%", "{%}")
            .Replace("~", "{~}")
            .Replace("(", "{(}")
            .Replace(")", "{)}")
            .Replace("[", "{[}")
            .Replace("]", "{]}");
    }

    private static int GetActiveSessionId()
    {
        try
        {
            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (explorer != null && explorer.SessionId > 0)
                return explorer.SessionId;
        }
        catch { }

        try
        {
            uint consoleSession = WTSGetActiveConsoleSessionId();
            if (consoleSession != 0xFFFFFFFF && consoleSession > 0)
                return (int)consoleSession;
        }
        catch { }

        return 1;
    }

    private static string _cachedUserName = string.Empty;
    private static DateTime _lastUserNameCheck = DateTime.MinValue;

    private static string GetActiveUserName()
    {
        if (!string.IsNullOrEmpty(_cachedUserName) && (DateTime.UtcNow - _lastUserNameCheck).TotalMinutes < 1)
            return _cachedUserName;

        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"(Get-CimInstance Win32_ComputerSystem).UserName\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd()?.Trim();
            if (!string.IsNullOrWhiteSpace(output))
            {
                _cachedUserName = output;
                _lastUserNameCheck = DateTime.UtcNow;
                return output;
            }
        }
        catch { }

        return Environment.UserName;
    }

    private static bool TryCreateProcessAsUser(int sessionId, string commandLine, out string? error)
    {
        error = null;
        IntPtr hUserToken = IntPtr.Zero;
        IntPtr hPrimaryToken = IntPtr.Zero;
        IntPtr pEnv = IntPtr.Zero;

        try
        {
            if (!WTSQueryUserToken((uint)sessionId, out hUserToken))
            {
                int err = Marshal.GetLastWin32Error();
                error = $"WTSQueryUserToken failed: Win32Error {err}";
                return false;
            }

            if (!DuplicateTokenEx(hUserToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out hPrimaryToken))
            {
                int err = Marshal.GetLastWin32Error();
                error = $"DuplicateTokenEx failed: Win32Error {err}";
                return false;
            }

            CreateEnvironmentBlock(out pEnv, hPrimaryToken, false);

            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default";

            PROCESS_INFORMATION pi;

            bool success = CreateProcessAsUser(
                hPrimaryToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                pEnv != IntPtr.Zero ? pEnv : IntPtr.Zero,
                null,
                ref si,
                out pi);

            if (success)
            {
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
                return true;
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                error = $"CreateProcessAsUser failed: Win32Error {err}";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (pEnv != IntPtr.Zero) DestroyEnvironmentBlock(pEnv);
            if (hPrimaryToken != IntPtr.Zero) CloseHandle(hPrimaryToken);
            if (hUserToken != IntPtr.Zero) CloseHandle(hUserToken);
        }
    }

    private static bool TryTaskSchedulerInteractive(string userName, string commandLine, out string? error)
    {
        error = null;
        string taskName = $"LaptopCtl_Interactive_{Guid.NewGuid():N}";
        try
        {
            // schtasks /create /tn taskName /tr "..." /sc ONCE /st 00:00 /ru userName /it /f
            var escapedCmd = commandLine.Replace("\"", "\\\"");
            var createArgs = $"/create /tn \"{taskName}\" /tr \"{escapedCmd}\" /sc ONCE /st 00:00 /ru \"{userName}\" /it /f";

            var psiCreate = new ProcessStartInfo("schtasks.exe", createArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using (var p = Process.Start(psiCreate))
            {
                p?.WaitForExit(5000);
            }

            // schtasks /run /tn taskName
            var psiRun = new ProcessStartInfo("schtasks.exe", $"/run /tn \"{taskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psiRun))
            {
                p?.WaitForExit(5000);
            }

            // schtasks /delete /tn taskName /f
            var psiDel = new ProcessStartInfo("schtasks.exe", $"/delete /tn \"{taskName}\" /f")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psiDel))
            {
                p?.WaitForExit(5000);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
