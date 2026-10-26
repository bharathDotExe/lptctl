<#
.SYNOPSIS
    Builds and publishes production binaries for LaptopCtl Agent and Controller CLI.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing LaptopCtl components ($Configuration)..." -ForegroundColor Cyan

$AgentProj      = "$PSScriptRoot\..\src\LaptopCtl.Agent\LaptopCtl.Agent.csproj"
$ControllerProj = "$PSScriptRoot\..\src\LaptopCtl.Controller\LaptopCtl.Controller.csproj"
$BackendProj    = "$PSScriptRoot\..\src\LaptopCtl.Backend\LaptopCtl.Backend.csproj"

$AgentOut      = "$OutputDir\agent"
$ControllerOut = "$OutputDir\controller"
$BackendOut    = "$OutputDir\backend"

[System.IO.Directory]::CreateDirectory($AgentOut) | Out-Null
[System.IO.Directory]::CreateDirectory($ControllerOut) | Out-Null
[System.IO.Directory]::CreateDirectory($BackendOut) | Out-Null

Write-Host "1. Publishing Agent (win-x64, self-contained single file)..." -ForegroundColor Yellow
dotnet publish $AgentProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $AgentOut

Write-Host "2. Publishing Controller CLI (win-x64, self-contained single file)..." -ForegroundColor Yellow
dotnet publish $ControllerProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $ControllerOut

Write-Host "3. Publishing Backend API..." -ForegroundColor Yellow
dotnet publish $BackendProj `
    -c $Configuration `
    -o $BackendOut

Write-Host ""
Write-Host "All components published successfully to: $OutputDir" -ForegroundColor Green
Write-Host "  Agent:      $AgentOut\LaptopCtlAgent.exe" -ForegroundColor Green
Write-Host "  Controller: $ControllerOut\laptopctl.exe" -ForegroundColor Green
Write-Host "  Backend:    $BackendOut\LaptopCtl.Backend.dll" -ForegroundColor Green
