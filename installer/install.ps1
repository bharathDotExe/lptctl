<#
.SYNOPSIS
    LaptopCtl Agent Installer & Device Enrollment Script
.DESCRIPTION
    Installs the LaptopCtl Windows Service agent and securely enrolls
    the computer with the central management backend.
.PARAMETER EnrollmentCode
    The one-time or time-limited enrollment code provided by the administrator.
.PARAMETER Backend
    The LaptopCtl backend URL (e.g., https://api.laptopctl.example.com).
.PARAMETER Force
    Overwrites existing agent installation if present.
.EXAMPLE
    irm https://laptopctl.example.com/install.ps1 | iex; Install-LaptopCtl -EnrollmentCode "ABC12345" -Backend "https://api.laptopctl.example.com"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$EnrollmentCode,

    [Parameter(Position = 1)]
    [string]$Backend = "https://api.laptopctl.example.com",

    [Parameter()]
    [string]$AgentBinaryPath,

    [switch]$Force
)

function Install-LaptopCtl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnrollmentCode,

        [string]$Backend = "https://api.laptopctl.example.com",
        [string]$AgentBinaryPath,
        [switch]$Force
    )

    $ErrorActionPreference = "Stop"
    $Backend = $Backend.TrimEnd('/')

    Write-Host ""
    Write-Host " ====================================================== " -ForegroundColor Cyan
    Write-Host "        LaptopCtl Remote Administration Agent           " -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host "                Automated Installer                     " -ForegroundColor Cyan
    Write-Host " ====================================================== " -ForegroundColor Cyan
    Write-Host ""

    # 1. Verify Administrative Privileges
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Error "Administrative privileges required. Please re-run this script in an elevated PowerShell window (Run as Administrator)."
        return
    }

    $InstallDir = Join-Path $env:ProgramFiles "LaptopCtl"
    $DataDir    = Join-Path $env:ProgramData "LaptopCtl"
    $AgentExe   = Join-Path $InstallDir "LaptopCtlAgent.exe"
    $IdentityFile = Join-Path $DataDir "identity.json"
    $ServiceName = "LaptopCtlAgent"

    Write-Host "[1/6] Creating secure directories..." -ForegroundColor Yellow
    [System.IO.Directory]::CreateDirectory($InstallDir) | Out-Null
    [System.IO.Directory]::CreateDirectory($DataDir) | Out-Null

    # Configure NTFS Access Control Lists (Least Privilege)
    # Only SYSTEM and local Administrators have access; remove inheritance and user access
    $acl = Get-Acl $DataDir
    $acl.SetAccessRuleProtection($true, $false) # Disable inheritance, do not keep inherited rules
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "BUILTIN\Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "NT AUTHORITY\SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.AddAccessRule($adminRule)
    $acl.AddAccessRule($systemRule)
    Set-Acl -Path $DataDir -AclObject $acl

    Write-Host "[2/6] Generating cryptographic device identity (RSA-2048)..." -ForegroundColor Yellow
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $pubKeyBytes = $rsa.ExportSubjectPublicKeyInfo()
    $pubKeyBase64 = [Convert]::ToBase64String($pubKeyBytes)
    $privKeyBytes = $rsa.ExportPkcs8PrivateKey()

    # Protect private key using Windows DPAPI (Machine scope)
    $protectedPrivKey = [System.Security.Cryptography.ProtectedData]::Protect(
        $privKeyBytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine
    )
    $privKeyPath = Join-Path $DataDir "device.key"
    [System.IO.File]::WriteAllBytes($privKeyPath, $protectedPrivKey)

    Write-Host "[3/6] Enrolling device with LaptopCtl backend ($Backend)..." -ForegroundColor Yellow
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $computerSystem = Get-CimInstance -ClassName Win32_ComputerSystem

    $enrollPayload = @{
        EnrollmentCode = $EnrollmentCode
        DeviceName     = $env:COMPUTERNAME
        AgentVersion   = "1.0.0"
        OsVersion      = "$($os.Caption) (Build $($os.BuildNumber))"
        OsArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        PublicKeyBase64 = $pubKeyBase64
    } | ConvertTo-Json

    try {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
        $enrollResp = Invoke-RestMethod -Uri "$Backend/api/enrollment" `
            -Method Post `
            -Body $enrollPayload `
            -ContentType "application/json" `
            -TimeoutSec 30
    }
    catch {
        Write-Error "Enrollment failed: $($_.Exception.Message)"
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $respBody = $reader.ReadToEnd()
            Write-Host "Backend response: $respBody" -ForegroundColor Red
        }
        return
    }

    $deviceId    = $enrollResp.deviceId
    $deviceToken = $enrollResp.deviceToken

    # Write identity configuration
    $identityData = @{
        DeviceId    = $deviceId
        DeviceToken = $deviceToken
        BackendUrl  = $Backend
        EnrolledAt  = [DateTimeOffset]::UtcNow.ToString("o")
        IsEnrolled  = $true
    } | ConvertTo-Json -Depth 5

    [System.IO.File]::WriteAllText($IdentityFile, $identityData)
    Write-Host "      ✓ Device enrolled with ID: $deviceId" -ForegroundColor Green

    Write-Host "[4/6] Installing agent binary..." -ForegroundColor Yellow
    if ($AgentBinaryPath -and (Test-Path $AgentBinaryPath)) {
        Copy-Item -Path $AgentBinaryPath -Destination $AgentExe -Force
    }
    else {
        # Check if binary is in source output or download from backend
        $localBuildPath = Join-Path $PSScriptRoot "..\src\LaptopCtl.Agent\bin\Debug\net8.0-windows\win-x64\publish\LaptopCtlAgent.exe"
        if (Test-Path $localBuildPath) {
            Copy-Item -Path $localBuildPath -Destination $AgentExe -Force
        }
        elseif (Test-Path (Join-Path $PSScriptRoot "..\src\LaptopCtl.Agent\bin\Debug\net8.0-windows\win-x64\LaptopCtlAgent.exe")) {
            Copy-Item -Path (Join-Path $PSScriptRoot "..\src\LaptopCtl.Agent\bin\Debug\net8.0-windows\win-x64\LaptopCtlAgent.exe") -Destination $AgentExe -Force
        }
        else {
            Write-Host "      Downloading latest agent binary from $Backend/api/agent/download..." -ForegroundColor Cyan
            try {
                Invoke-WebRequest -Uri "$Backend/api/agent/download" -OutFile $AgentExe -TimeoutSec 120
            }
            catch {
                Write-Warning "Could not download binary directly from $Backend. Place LaptopCtlAgent.exe in $InstallDir manually."
            }
        }
    }

    # Write agent appsettings if not present
    $agentSettingsPath = Join-Path $InstallDir "appsettings.json"
    $agentSettings = @{
        BackendUrl = $Backend
        Logging = @{
            LogLevel = @{
                Default = "Information"
                Microsoft = "Warning"
            }
        }
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($agentSettingsPath, $agentSettings)

    Write-Host "[5/6] Registering Windows Service ($ServiceName)..." -ForegroundColor Yellow
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "      Stopping and removing existing service..." -ForegroundColor Cyan
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    if (Test-Path $AgentExe) {
        $binPathWithQuotes = "`"$AgentExe`""
        & sc.exe create $ServiceName binPath= $binPathWithQuotes start= auto DisplayName= "LaptopCtl Remote Administration Agent" | Out-Null
        & sc.exe description $ServiceName "Maintains secure outbound connection to LaptopCtl backend for remote administration." | Out-Null
        & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

        Write-Host "[6/6] Starting agent service..." -ForegroundColor Yellow
        Start-Service -Name $ServiceName
        Start-Sleep -Seconds 2

        $serviceStatus = Get-Service -Name $ServiceName
        if ($serviceStatus.Status -eq "Running") {
            Write-Host ""
            Write-Host " ====================================================== " -ForegroundColor Green
            Write-Host "   SUCCESS: LaptopCtl Agent installed and running!      " -ForegroundColor White -BackgroundColor DarkGreen
            Write-Host "   Device ID:  $deviceId                                " -ForegroundColor Green
            Write-Host "   Backend:    $Backend                                 " -ForegroundColor Green
            Write-Host "   Service:    $ServiceName (Running)                   " -ForegroundColor Green
            Write-Host " ====================================================== " -ForegroundColor Green
        }
        else {
            Write-Warning "Service created but status is $($serviceStatus.Status). Check event logs."
        }
    }
    else {
        Write-Host "[6/6] Note: Agent binary will start once LaptopCtlAgent.exe is present in $InstallDir." -ForegroundColor Yellow
    }
}

# Run when invoked directly with parameters
if ($EnrollmentCode) {
    Install-LaptopCtl -EnrollmentCode $EnrollmentCode -Backend $Backend -AgentBinaryPath $AgentBinaryPath -Force:$Force
}
