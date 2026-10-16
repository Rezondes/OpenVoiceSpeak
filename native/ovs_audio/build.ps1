# Package 172 (A179): builds ovs_audio.dll (win-x64) from this crate with the pinned Rust (rust-toolchain.toml).
# Needs rustup with the MSVC build tools (link.exe) installed; rustup fetches the pinned toolchain itself. Package 178:
# also Python 3 with meson 1.12.1 and ninja 1.13.2 (python -m pip install --user meson==1.12.1 ninja==1.13.2) and git.
# Usage: powershell -File native\ovs_audio\build.ps1 [-PrintHash]
# The result replaces src\OVS.Client\runtimes\win-x64\native\ovs_audio.dll only when its sha256 is $Expected; after an
# intended change (code, Cargo.lock, toolchain) update $Expected and the hash in PLAN.md (the package that changed it).
param([switch]$PrintHash)

$ErrorActionPreference = 'Stop'
$Rust = '1.98.1'
$Expected = 'e83a1d6e3e448060ca512d6f4ef9e65884a80e66bf41645ad548a86267694d5e'
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
# Package 178 (A185): WebRTC's audio processing (AEC3) with MSVC, statically, into target\aec; ovs_audio links it
$AecTag = 'v2.1' # freedesktop webrtc-audio-processing (BSD-3), pulls abseil-cpp 20240722.0 through its meson wrap
$AecCommit = '846fe90a289f58b7c9303a635142aa2c7caa93e5'
$Aec = Join-Path $Here 'target\aec' # short on purpose: MSVC fails on the long object paths of abseil (C1083)
$AecSource = Join-Path $Aec 'src'
if (-not (Test-Path $AecSource)) { Run git clone --depth 1 --branch $AecTag https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing.git $AecSource }
if ((git -C $AecSource rev-parse HEAD) -ne $AecCommit) { throw "$AecSource is not at $AecTag ($AecCommit)" }
$env:NINJA = Join-Path (python -c "import sysconfig; print(sysconfig.get_path('scripts', 'nt_user'))") 'ninja.exe'
$Tools = @{ meson = '1.12.1'; ninja = '1.13.2' }
$Found = @{ meson = python -m mesonbuild.mesonmain --version; ninja = & $env:NINJA --version }
foreach ($Tool in $Tools.Keys) { if (-not "$($Found[$Tool])".StartsWith($Tools[$Tool])) { throw "$Tool $($Found[$Tool]) found, $($Tools[$Tool]) needed" } }
if (-not (Test-Path (Join-Path $Aec 'b\build.ninja'))) {
    Push-Location $AecSource
    try {
        Run python -m mesonbuild.mesonmain setup (Join-Path $Aec 'b') --vsenv --buildtype=release --default-library=static -Db_vscrt=md -Dcpp_std=c++20 '-Dabseil-cpp:warning_level=0'
    } finally { Pop-Location }
}
Run python -m mesonbuild.mesonmain compile -C (Join-Path $Aec 'b')
$env:OVS_AEC_BUILD = $Aec

Push-Location $Here # rustup picks the toolchain of rust-toolchain.toml in this folder
try {
    $Found = (& rustc --version) -split ' ' | Select-Object -Index 1
    if ($Found -ne $Rust) { throw "rustc $Found found, $Rust needed (rust-toolchain.toml)" }
    # the same bytes on every machine: no absolute paths of this checkout or of the cargo home in the DLL
    $CargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    # the encoded form keeps paths with spaces whole (its separator is 0x1F)
    $env:CARGO_ENCODED_RUSTFLAGS = @("--remap-path-prefix=$Here=ovs_audio", "--remap-path-prefix=$CargoHome=cargo", '-Clink-arg=/Brepro') -join [char]0x1F
    Run cargo build --release --locked --target x86_64-pc-windows-msvc --features aec
} finally { Pop-Location; Remove-Item Env:CARGO_ENCODED_RUSTFLAGS -ErrorAction SilentlyContinue }

$Built = Join-Path $Here 'target\x86_64-pc-windows-msvc\release\ovs_audio.dll'
$Hash = (Get-FileHash $Built -Algorithm SHA256).Hash.ToLowerInvariant()
if ($PrintHash) { return $Hash }
if ($Hash -ne $Expected) { throw "built sha256 $Hash, expected $Expected; the checked-in DLL is unchanged" }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
Copy-Item $Built $Out
$Hash
