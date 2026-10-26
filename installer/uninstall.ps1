<#
.SYNOPSIS
    LaptopCtl Agent Uninstaller
.DESCRIPTION
    Stops the LaptopCtl agent service, deletes the Windows Service registration,
    and removes agent binaries and configuration files.
#>

[CmdletBinding()]
param(
    [switch]$KeepIdentity,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host " ====================================================== " -ForegroundColor Yellow
Write-Host "       LaptopCtl Agent Uninstaller                      " -ForegroundColor White -BackgroundColor DarkRed
Write-Host " ====================================================== " -ForegroundColor Yellow
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrative privileges required. Please re-run in an elevated PowerShell window (Run as Administrator)."
    return
}

$ServiceName = "LaptopCtlAgent"
$InstallDir  = Join-Path $env:ProgramFiles "LaptopCtl"
$DataDir     = Join-Path $env:ProgramData "LaptopCtl"

# 1. Stop and remove service
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "[1/3] Stopping service '$ServiceName'..." -ForegroundColor Yellow
    if ($svc.Status -eq "Running") {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    # Ensure any running process is terminated so files are unlocked
    Get-Process -Name $ServiceName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "[2/3] Removing service registration..." -ForegroundColor Yellow
    & sc.exe delete $ServiceName | Out-Null
}
else {
    Get-Process -Name $ServiceName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host "[1/3] Service '$ServiceName' was not found or already removed." -ForegroundColor Gray
}

# Clean any temporary interactive tasks
try {
    Get-ScheduledTask -TaskName "LaptopCtl_Interactive_*" -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
} catch {}

# 2. Clean directories
Write-Host "[3/3] Cleaning installation files..." -ForegroundColor Yellow
if (Test-Path $InstallDir) {
    try {
        Remove-Item -Path $InstallDir -Recurse -Force
        Write-Host "      Removed: $InstallDir" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not completely remove $InstallDir. A reboot may be required if files are in use."
    }
}

if (-not $KeepIdentity -and (Test-Path $DataDir)) {
    try {
        Remove-Item -Path $DataDir -Recurse -Force
        Write-Host "      Removed: $DataDir" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not completely remove $DataDir."
    }
}

Write-Host ""
Write-Host "LaptopCtl Agent has been successfully uninstalled from this computer." -ForegroundColor Green
Write-Host ""
