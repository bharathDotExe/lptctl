# LaptopCtl Installation & Deployment Guide

## 1. Quick Start: Single-Command Target Machine Enrollment

Target Windows computers (laptops, workstations, or servers) can be completely enrolled with a **single PowerShell command**.

### Step 1: Generate an Enrollment Code
From the operator workstation or web dashboard:
```bash
laptopctl enroll-code
```
This produces an 8-character code (e.g. `LPT8X92K`) and the one-liner command.

### Step 2: Run Command on Target Computer
Open PowerShell as **Administrator** on the target computer and run:
```powershell
irm https://api.laptopctl.example.com/install.ps1 | iex; Install-LaptopCtl -EnrollmentCode "LPT8X92K" -Backend "https://api.laptopctl.example.com"
```

The script automatically:
1. Verifies elevation.
2. Creates `$env:ProgramFiles\LaptopCtl` and `$env:ProgramData\LaptopCtl`.
3. Locks down NTFS ACLs so only SYSTEM and local Administrators can read credentials.
4. Generates an RSA-2048 key pair protected by Windows DPAPI.
5. Registers the machine with the central backend.
6. Installs and starts the `LaptopCtlAgent` Windows Service (`sc.exe create LaptopCtlAgent start= auto`).

---

## 2. Controller CLI Installation

### Windows x64 Standalone Executable
Download the single-file `laptopctl.exe` and place it in your `PATH` (e.g. `C:\Windows\System32` or `$env:USERPROFILE\bin`):
```powershell
# Authenticate
laptopctl login --email admin@company.com

# Verify enrolled devices
laptopctl devices
```

---

## 3. Backend Deployment (Docker Compose)

### Prerequisites
- Docker & Docker Compose
- TLS Certificate (or reverse proxy terminating HTTPS)

### Deployment Steps
1. Clone the repository and navigate to `deploy/`:
   ```bash
   cd deploy
   ```
2. Create your `.env` configuration file:
   ```env
   DB_PASSWORD=StrongDatabasePassword123!
   JWT_SECRET_KEY=at-least-32-characters-random-secret-key-prod
   ```
3. Start the stack:
   ```bash
   docker compose up -d
   ```
4. Verify health:
   ```bash
   curl http://localhost/health
   # Returns 200 OK (Healthy)
   ```

The stack includes:
- **`postgres`**: PostgreSQL 16 database storing encrypted credentials and audit trail.
- **`backend`**: LaptopCtl ASP.NET Core 8 API & WebSocket relay.
- **`nginx`**: Reverse proxy terminating connections, handling WebSocket upgrade, and serving the Web Dashboard.

---

## 4. Uninstalling the Agent

To completely remove the agent from a computer:
```powershell
# Run uninstaller as Administrator
.\installer\uninstall.ps1
```
This stops the service, deletes the Windows Service registration, and purges all files and local identity data.
