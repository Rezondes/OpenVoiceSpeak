# Package 172 (A179): builds ovs_audio.dll (win-x64) from this crate with the pinned Rust (rust-toolchain.toml).
# Needs rustup with the MSVC build tools (link.exe) installed; rustup fetches the pinned toolchain itself.
# Usage: powershell -File native\ovs_audio\build.ps1 [-PrintHash]
# The result replaces src\OVS.Client\runtimes\win-x64\native\ovs_audio.dll only when its sha256 is $Expected; after an
# intended change (code, Cargo.lock, toolchain) update $Expected and the hash in PLAN.md (the package that changed it).
param([switch]$PrintHash)

$ErrorActionPreference = 'Stop'
$Rust = '1.98.1'
$Expected = '6eebf2ea9cb773a69797ddbdb8f85f5ede3fa9a17981c28feace94bf4d7c7eb2'
$Here = $PSScriptRoot
$Root = (Resolve-Path (Join-Path $Here '..\..')).Path
$Out = Join-Path $Root 'src\OVS.Client\runtimes\win-x64\native\ovs_audio.dll'

function Run { & $args[0] $args[1..($args.Count - 1)]; if ($LASTEXITCODE) { throw "failed: $args" } }

$env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH"
# Package 175: libopus builds with CMake; without one on PATH take the one of the Visual Studio build tools
if (-not $env:CMAKE -and -not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $env:CMAKE = & $VsWhere -latest -products * -find 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' | Select-Object -First 1
    if (-not $env:CMAKE) { throw 'CMake not found: install it or the Visual Studio build tools with "C++ CMake tools"' }
}
Push-Location $Here # rustup picks the toolchain of rust-toolchain.toml in this folder
try {
    $Found = (& rustc --version) -split ' ' | Select-Object -Index 1
    if ($Found -ne $Rust) { throw "rustc $Found found, $Rust needed (rust-toolchain.toml)" }
    # the same bytes on every machine: no absolute paths of this checkout or of the cargo home in the DLL
    $CargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    # the encoded form keeps paths with spaces whole (its separator is 0x1F)
    $env:CARGO_ENCODED_RUSTFLAGS = @("--remap-path-prefix=$Here=ovs_audio", "--remap-path-prefix=$CargoHome=cargo", '-Clink-arg=/Brepro') -join [char]0x1F
    Run cargo build --release --locked --target x86_64-pc-windows-msvc
} finally { Pop-Location; Remove-Item Env:CARGO_ENCODED_RUSTFLAGS -ErrorAction SilentlyContinue }

$Built = Join-Path $Here 'target\x86_64-pc-windows-msvc\release\ovs_audio.dll'
$Hash = (Get-FileHash $Built -Algorithm SHA256).Hash.ToLowerInvariant()
if ($PrintHash) { return $Hash }
if ($Hash -ne $Expected) { throw "built sha256 $Hash, expected $Expected; the checked-in DLL is unchanged" }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
Copy-Item $Built $Out
$Hash
