# LaptopCtl Command-Line Reference

`laptopctl` provides an Android Debug Bridge (ADB) inspired command-line experience for inspecting and administering enrolled Windows computers.

## Global Options

```bash
laptopctl [command] [options]

Options:
  -b, --backend <url>   LaptopCtl backend URL (defaults to saved credential or https://api.laptopctl.example.com)
  -?, -h, --help        Show help and usage information
  --version             Show version information
```

---

## Command Reference

### Authentication

#### `laptopctl login`
Authenticates the operator with the backend API and stores the JWT session securely in your local user profile.
```bash
# Prompt securely for password
laptopctl login --email admin@company.com

# Supply password directly (automation/scripts)
laptopctl login --email admin@company.com --password "SecureP@ssw0rd!"
```

#### `laptopctl logout`
Revokes the current session and clears locally cached credentials.
```bash
laptopctl logout
```

---

### Device Discovery & Management

#### `laptopctl devices`
Lists all enrolled Windows machines bound to your account with real-time status.
```bash
laptopctl devices
```
*Output:*
```text
┌──────────┬────────────────────┬───────────────────────────────┬───────┬──────────┬─────────────────┐
│ Device ID│ Name               │ OS                            │ Agent │ Status   │ Last Seen       │
├──────────┼────────────────────┼───────────────────────────────┼───────┼──────────┼─────────────────┤
│ win-7f9a…│ WORKSTATION-ENG-01 │ Windows 11 Enterprise (22631) │ 1.0.0 │ ● Online │ 10/26/2026 1:15 │
│ win-4e8c…│ LAPTOP-EXEC-04     │ Windows 11 Pro (22631.3296)   │ 1.0.0 │ ● Online │ 10/26/2026 1:14 │
│ win-9b1d…│ DEV-RIG-ALPHA      │ Windows 10 Pro (19045.4291)   │ 1.0.0 │ ○ Offline│ 10/25/2026 5:30 │
└──────────┴────────────────────┴───────────────────────────────┴───────┴──────────┴─────────────────┘
```

#### `laptopctl status <device-id>`
Displays detailed status panel for an enrolled machine.
```bash
laptopctl status win-7f9a2b1c
```

#### `laptopctl enroll-code`
Generates a new single-use, time-limited enrollment code and outputs the ready-to-run PowerShell one-liner for the target computer.
```bash
laptopctl enroll-code
```

#### `laptopctl uninstall <device-id>`
Revokes a device enrollment from your account (requires typed confirmation of the device ID).
```bash
laptopctl uninstall win-7f9a2b1c
```

---

### Diagnostics & Inspection (Telemetry)

#### `laptopctl info <device-id>`
Queries hardware specifications, CPU model, total RAM, Windows build, and architecture.
```bash
laptopctl info win-7f9a2b1c
```

#### `laptopctl processes <device-id>`
Lists running processes on the remote machine sorted by memory consumption (PID, name, working set, and start time).
```bash
laptopctl processes win-7f9a2b1c
```

#### `laptopctl disk <device-id>`
Inspects all local storage volumes and reports total size, free space, and utilization percentages.
```bash
laptopctl disk win-7f9a2b1c
```

#### `laptopctl network <device-id>`
Queries network adapters, operational status (Up/Down), descriptions, and bound IPv4/IPv6 addresses.
```bash
laptopctl network win-7f9a2b1c
```

---

### Remote Execution

#### `laptopctl exec <device-id> <command>`
Executes an administrative PowerShell command remotely on the target computer under the `LaptopCtlAgent` Windows Service context.
*Alias: `laptopctl shell`*
```bash
# Check service status
laptopctl exec win-7f9a2b1c "Get-Service LaptopCtlAgent"

# Query network configuration
laptopctl exec win-7f9a2b1c "ipconfig /all"

# Inspect active local users
laptopctl exec win-7f9a2b1c "Get-LocalUser"
```

---

### Authorized File Transfers

#### `laptopctl files list <device-id> <remote-path>`
Lists files and subdirectories at the specified path on the target machine.
```bash
laptopctl files list win-7f9a2b1c "C:\Users\Public"
```

#### `laptopctl files upload <device-id> <local-path> <remote-path>`
Transfers a local file to the target machine with SHA-256 integrity verification.
```bash
laptopctl files upload win-7f9a2b1c "./patch.zip" "C:\Users\Public\patch.zip"
```

#### `laptopctl files download <device-id> <remote-path> <local-path>`
Downloads a file from the target computer to your local workstation.
```bash
laptopctl files download win-7f9a2b1c "C:\Users\Public\report.log" "./downloaded_report.log"
```

---

### Session Management

#### `laptopctl sessions`
Lists all active remote administration sessions across your account.
```bash
laptopctl sessions
```

#### `laptopctl connect <device-id>`
Opens an administrative session channel to an enrolled machine.
```bash
laptopctl connect win-7f9a2b1c
```

#### `laptopctl disconnect <device-id>`
Terminates an active session for the target device.
```bash
laptopctl disconnect win-7f9a2b1c
```
