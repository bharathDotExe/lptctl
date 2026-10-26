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

        // 1. "open <url or path>"
        if (trimmed.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(5).Trim().Trim('"', '\'');
            return true;
        }

        // 2. "browse <url>"
        if (trimmed.StartsWith("browse ", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed.Substring(7).Trim().Trim('"', '\'');
            return true;
        }

        // 3. "start <url or path or app>"
        if (trimmed.StartsWith("start ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed.Substring(6).Trim();
            // start http... or start chrome ... or start explorer ...
            target = rest;
            return true;
        }

        // 4. Direct URL: "http://..." or "https://..." or "www...." or "youtube.com"
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtube", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            target = trimmed;
            return true;
        }

        // 5. Explorer or GUI app commands
        if (trimmed.StartsWith("explorer", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("calc.exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("notepad", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Start-Process http", StringComparison.OrdinalIgnoreCase))
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
            output = $"[LaptopCtl Interactive] Successfully launched '{target}' on user screen!\nTarget: {target}\nUser: {userName}\nSession: {sessionId}";
            return true;
        }

        // Attempt 2: Task Scheduler interactive launch (/IT /RU)
        if (!string.IsNullOrEmpty(userName) && TryTaskSchedulerInteractive(userName, commandLine, out var taskError))
        {
            output = $"[LaptopCtl Interactive] Successfully launched '{target}' on user screen via Task Scheduler!\nTarget: {target}\nUser: {userName}\nSession: {sessionId}";
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
        if (target.StartsWith("Start-Process", StringComparison.OrdinalIgnoreCase))
        {
            var parts = target.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) return parts[1].Trim().Trim('"', '\'');
        }
        return target;
    }

    private static string BuildUserCommandLine(string target)
    {
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
