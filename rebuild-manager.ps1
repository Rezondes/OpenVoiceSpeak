# Rebuilds and restarts the local manager setup: the server image the manager runs, the manager with its socket proxy,
# then the Windows client.
# Usage (from anywhere): powershell -ExecutionPolicy Bypass -File rebuild-manager.ps1 [-SkipServerImage] [-SkipManager] [-SkipClient] [-NoStart]
#   -SkipServerImage  do not build openvoicespeak/server:local (new servers of a dev manager use the newest local server image)
#   -SkipManager      leave the manager alone
#   -SkipClient       leave the client alone
#   -NoStart          build the client but do not start it
#   -ClientArgs       passed to OVS.Client.exe, e.g. -ClientArgs '--debug-api','5005'
# The server image is only built, not started: the managed servers run from it, and the single server of rebuild.ps1
# would take port 7000 from them. The manager's data stays in its volume manager-data.
param(
    [switch]$SkipServerImage,
    [switch]$SkipManager,
    [switch]$SkipClient,
    [switch]$NoStart,
    [string[]]$ClientArgs = @()
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$ManagerDir = Join-Path $Root 'deploy\manager'
$EnvFile = Join-Path $ManagerDir '.env'

function Run { & $args[0] $args[1..($args.Count - 1)]; if ($LASTEXITCODE) { throw "failed: $args" } }

Push-Location $Root
try {
    if (-not ($SkipServerImage -and $SkipManager)) {
        docker info --format '{{.ServerVersion}}' | Out-Null
        if ($LASTEXITCODE) { throw 'Docker is not running; start Docker Desktop and try again' }
    }

    if (-not $SkipServerImage) {
        Write-Host 'Server image: building openvoicespeak/server:local from the checkout'
        Run docker compose -f docker-compose.yml -f docker-compose.build.yml build
    }

    if (-not $SkipManager) {
        if (-not (Test-Path $EnvFile)) {
            # Docker Desktop's socket belongs to group 0; the local server image is openvoicespeak/server (README-MANAGER.md)
            Write-Host "Manager: writing $EnvFile for Docker Desktop and the local server image"
            Set-Content -Path $EnvFile -Encoding ascii -Value @('DOCKER_SOCKET_GID=0', 'OVS_MANAGER_SERVER_IMAGE=openvoicespeak/server')
        }
        Write-Host 'Manager: building the image from the checkout and restarting'
        Push-Location $ManagerDir
        try {
            Run docker compose up -d --build
            Run docker compose logs --tail 10 ovs-manager
        } finally { Pop-Location }
        Write-Host 'Manager: http://localhost:6999 (Ctrl+F5 in the browser after a rebuild)'
    }

    if (-not $SkipClient) {
        $ClientParameters = @{ SkipServer = $true; NoStart = $NoStart; ClientArgs = $ClientArgs }
        & (Join-Path $Root 'rebuild.ps1') @ClientParameters
    }
} finally { Pop-Location }
