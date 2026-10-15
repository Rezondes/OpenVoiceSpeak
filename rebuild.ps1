# Rebuilds and restarts the local setup: the server container from this checkout, then the Windows client.
# Usage (from anywhere): powershell -ExecutionPolicy Bypass -File rebuild.ps1 [-Pull] [-SkipServer] [-SkipClient] [-NoStart]
#   -Pull        take the published image from ghcr.io instead of building it from the checkout
#   -SkipServer  leave the container alone
#   -SkipClient  leave the client alone
#   -NoStart     build the client but do not start it
#   -ClientArgs  passed to OVS.Client.exe, e.g. -ClientArgs '--debug-api','5005'
# Data and certificate stay in the volume ovs-data; the compose project name is the folder name, keep it.
param(
    [switch]$Pull,
    [switch]$SkipServer,
    [switch]$SkipClient,
    [switch]$NoStart,
    [string[]]$ClientArgs = @()
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$ClientDir = Join-Path $Root 'publish\client'
$ClientExe = Join-Path $ClientDir 'OVS.Client.exe'

function Run { & $args[0] $args[1..($args.Count - 1)]; if ($LASTEXITCODE) { throw "failed: $args" } }

function Stop-Client {
    # the running exe is locked, so publish would fail; ask it to close first, then end it
    $Running = Get-Process -Name 'OVS.Client' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $ClientExe }
    foreach ($Process in $Running) {
        Write-Host "Stopping client (PID $($Process.Id))"
        [void]$Process.CloseMainWindow()
        if (-not $Process.WaitForExit(5000)) { Stop-Process -Id $Process.Id -Force; $Process.WaitForExit() }
    }
}

Push-Location $Root
try {
    if (-not $SkipServer) {
        docker info --format '{{.ServerVersion}}' | Out-Null
        if ($LASTEXITCODE) { throw 'Docker is not running; start Docker Desktop and try again' }
        if ($Pull) {
            Write-Host 'Server: pulling the image from ghcr.io and restarting'
            Run docker compose pull
            Run docker compose up -d
        } else {
            Write-Host 'Server: building the image from the checkout and restarting'
            Run docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
        }
        # admin token and certificate fingerprint are in the start output
        Run docker compose logs --tail 20 ovs-server
    }

    if (-not $SkipClient) {
        Stop-Client
        Write-Host 'Client: publishing'
        Run dotnet publish src/OVS.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $ClientDir
        if (-not $NoStart) {
            Write-Host "Client: starting $ClientExe"
            if ($ClientArgs.Count) { Start-Process $ClientExe -ArgumentList $ClientArgs -WorkingDirectory $ClientDir }
            else { Start-Process $ClientExe -WorkingDirectory $ClientDir }
        }
    }
} finally { Pop-Location }
