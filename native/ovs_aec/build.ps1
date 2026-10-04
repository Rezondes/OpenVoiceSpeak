# Package 140: builds ovs_aec.dll (win-x64) from webrtc-audio-processing and the C wrapper ovs_aec.cpp.
# Needs only Python 3 and git; the compiler is zig's clang from PyPI, so no Visual Studio is required:
#   python -m pip install --user ziglang==0.16.0 meson==1.12.1 ninja==1.13.2
# Usage: powershell -File native\ovs_aec\build.ps1 -Work <empty scratch folder>
# The result replaces src\OVS.Client\runtimes\win-x64\native\ovs_aec.dll only when its sha256 is $Expected; after an
# intended change (wrapper, versions) update $Expected and the hash in PLAN.md (Package 140).
param([Parameter(Mandatory)][string]$Work)

$ErrorActionPreference = 'Stop'
$Tag = 'v2.1' # freedesktop webrtc-audio-processing (BSD-3), pulls abseil-cpp 20240722.0 through its meson wrap
$Commit = '846fe90a289f58b7c9303a635142aa2c7caa93e5' # what v2.1 points to
$Versions = @{ zig = '0.16.0'; meson = '1.12.1'; ninja = '1.13.2' }
$Expected = '09055b6a599b67a6c5f88103fce50ff159644668912ff8335287266e5abcae2f'
$Here = $PSScriptRoot
$Out = Join-Path $Here '..\..\src\OVS.Client\runtimes\win-x64\native\ovs_aec.dll'

function Run { & $args[0] $args[1..($args.Count - 1)]; if ($LASTEXITCODE) { throw "failed: $args" } }

New-Item -ItemType Directory -Force $Work | Out-Null
$Src = Join-Path $Work 'src'
$Build = Join-Path $Work 'build'
if (-not (Test-Path $Src)) { Run git clone --depth 1 --branch $Tag https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing.git $Src }
$Head = git -C $Src rev-parse HEAD
if ($Head -ne $Commit) { throw "$Src is at $Head, not $Tag ($Commit)" } # also an existing checkout

# meson finds ninja through NINJA; pip puts it into the user's Scripts folder, which is often not on PATH
$env:NINJA = (Join-Path (python -c "import sysconfig; print(sysconfig.get_path('scripts', 'nt_user'))") 'ninja.exe')
$Found = @{ zig = python -m ziglang version; meson = python -m mesonbuild.mesonmain --version; ninja = & $env:NINJA --version }
foreach ($Tool in $Versions.Keys) {
    if (-not "$($Found[$Tool])".StartsWith($Versions[$Tool])) { throw "$Tool $($Found[$Tool]) found, $($Versions[$Tool]) needed" }
}

# static libraries only; the cross file sets zig's clang for x86_64-windows-gnu
Push-Location $Src
try {
    Run python -m mesonbuild.mesonmain setup $Build --wipe --cross-file (Join-Path $Here 'zig-x86_64-windows-gnu.ini') `
        --buildtype=release --default-library=static '-Dabseil-cpp:warning_level=0'
    Run python -m mesonbuild.mesonmain compile -C $Build
} finally { Pop-Location }

# the DLL: wrapper plus the static libraries, libc++ and the MinGW runtime linked in (imports only Windows DLLs), stripped
$Absl = Join-Path $Build 'subprojects\abseil-cpp-20240722.0'
$Libs = @(Join-Path $Build 'webrtc\modules\audio_processing\libwebrtc-audio-processing-2.a') +
    (Get-ChildItem $Absl -Filter 'libabsl_*.a' | ForEach-Object FullName)
Run python -m ziglang c++ -target x86_64-windows-gnu -shared -O2 -std=c++17 '-DNDEBUG' '-DNTDDI_VERSION=0x0A000000' `
    -Wno-nullability-completeness -I $Src -I (Join-Path $Src 'webrtc') -I (Join-Path $Src 'subprojects\abseil-cpp-20240722.0') `
    (Join-Path $Here 'ovs_aec.cpp') '-Wl,--start-group' @Libs '-Wl,--end-group' -lwinmm -s -o (Join-Path $Work 'ovs_aec.dll')
$Hash = (Get-FileHash (Join-Path $Work 'ovs_aec.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Hash -ne $Expected) { throw "built sha256 $Hash, expected $Expected; the checked-in DLL is unchanged" }
Copy-Item (Join-Path $Work 'ovs_aec.dll') $Out
$Hash
