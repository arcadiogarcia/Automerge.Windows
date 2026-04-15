#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build the entire Automerge Windows native projection stack.

.PARAMETER Profile
    Cargo profile: "debug" or "release" (default: "release").

.PARAMETER Arch
    Target architecture: "x64" (default), "arm64", or "all".
    "arm64"/"all" cross-compiles from the x64 host using MSVC x64_arm64 tools.
    ARM64 test execution is skipped (ARM64 binaries can't run on an x64 host).

.PARAMETER SkipCpp
    Skip C++ CMake build.

.PARAMETER SkipCsharp
    Skip C# dotnet build.

.PARAMETER SkipWinRT
    Skip WinRT component build.

.PARAMETER TestOnly
    Run tests without rebuilding.
#>
param(
    [ValidateSet("debug", "release")]
    [string]$Profile = "release",
    [ValidateSet("x64", "arm64", "all")]
    [string]$Arch = "x64",
    [switch]$SkipCpp,
    [switch]$SkipCsharp,
    [switch]$SkipWinRT,
    [switch]$TestOnly
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function Ok($msg)    { Write-Host "  OK  $msg" -ForegroundColor Green }
function Err($msg)   { Write-Host "  ERR $msg" -ForegroundColor Red; exit 1 }
function Run {
    param([string]$Exe, [string[]]$ArgList)
    & $Exe @ArgList
    if ($LASTEXITCODE -ne 0) { Err "$Exe failed with exit code $LASTEXITCODE" }
}

# ─── Locate Visual Studio ──────────────────────────────────────────────────────

function Find-VCVarsAll {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $searchPaths = @(
        # Enterprise/Professional first — matches GitHub Actions windows-2022 runner
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvarsall.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat"
    )
    foreach ($p in $searchPaths) { if (Test-Path $p) { return $p } }
    if (Test-Path $vswhere) {
        $allPaths = & $vswhere -all -products * -property installationPath 2>$null
        foreach ($vp in $allPaths) {
            $candidate = "$vp\VC\Auxiliary\Build\vcvarsall.bat"
            if (Test-Path $candidate) { return $candidate }
        }
    }
    return $null
}

$vcvarsAll = Find-VCVarsAll
if (-not $vcvarsAll) { Err "vcvarsall.bat not found. Ensure VS 2019 Build Tools or a full VS installation is present." }
# vcvarsall.bat lives at VS_ROOT\VC\Auxiliary\Build\vcvarsall.bat  (4 levels deep)
$vsPath = Split-Path (Split-Path (Split-Path (Split-Path $vcvarsAll)))
Ok "Using toolchain from: $vsPath"

# Apply a vcvarsall.bat environment to the current process
function Setup-VCEnv([string]$vcArgs) {
    $envDump = cmd /c "`"$vcvarsAll`" $vcArgs > NUL 2>&1 && set"
    foreach ($line in $envDump) {
        if ($line -match "^([^=]+)=(.*)$") {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2])
            Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] -ErrorAction SilentlyContinue
        }
    }
}

# Ensure cargo is in PATH
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Users\$env:USERNAME\.cargo\bin;" + $env:PATH
}
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { Err "cargo not found. Install Rust via rustup." }

# Ensure cmake/ninja are in PATH
foreach ($p in @("C:\Program Files\CMake\bin")) {
    if (Test-Path $p) { $env:PATH = "$p;" + $env:PATH }
}

$cmakeType = if ($Profile -eq "release") { "Release" } else { "Debug" }

# ─────────────────────────────────────────────────────────────────────────────
# Helper: build one architecture
# ─────────────────────────────────────────────────────────────────────────────

function Build-ForArch([string]$arch) {
    $isArm64   = ($arch -eq "arm64")
    $vcEnvArg  = if ($isArm64) { "x64_arm64" } else { "x64" }
    $cargoTarget = if ($isArm64) { "aarch64-pc-windows-msvc" } else { $null }
    $rustOutDir  = if ($isArm64) {
        "$root/rust-core/target/aarch64-pc-windows-msvc/$Profile"
    } else {
        "$root/rust-core/target/$Profile"
    }
    $cppBuildDir = if ($isArm64) { "$root/build-arm64" } else { "$root/build" }

    # ── Set up MSVC environment ─────────────────────────────────────────────
    Step "Setting up MSVC $vcEnvArg environment"
    Setup-VCEnv $vcEnvArg
    Ok "MSVC $vcEnvArg environment configured"

    # For ARM64 cross-compilation Rust selects its linker from VCINSTALLDIR,
    # which defaults to HostX64\x64\link.exe even after vcvarsall x64_arm64.
    # Override explicitly with CARGO_TARGET_AARCH64_PC_WINDOWS_MSVC_LINKER.
    if ($isArm64) {
        # VCToolsInstallDir is set by vcvarsall (has a trailing backslash)
        $arm64Link = "${env:VCToolsInstallDir}bin\HostX64\arm64\link.exe"
        if (-not (Test-Path $arm64Link)) {
            # Fallback: derive from vsPath directly
            $msvcBase = "$vsPath\VC\Tools\MSVC"
            $msvcVer  = Get-ChildItem $msvcBase -ErrorAction SilentlyContinue |
                        Sort-Object Name | Select-Object -Last 1 -ExpandProperty Name
            if ($msvcVer) { $arm64Link = "$msvcBase\$msvcVer\bin\HostX64\arm64\link.exe" }
        }
        if (Test-Path $arm64Link) {
            $env:CARGO_TARGET_AARCH64_PC_WINDOWS_MSVC_LINKER = $arm64Link
            Ok "ARM64 linker: $arm64Link"
        } else {
            # Last resort: try lld-link (LLVM) which is pre-installed on GitHub Actions runners
            $lldLink = Get-Command lld-link.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
            if ($lldLink) {
                $env:CARGO_TARGET_AARCH64_PC_WINDOWS_MSVC_LINKER = $lldLink
                Ok "ARM64 linker (lld-link fallback): $lldLink"
            } else {
                Err "ARM64 cross-linker not found. Install the 'C++ ARM64 build tools' component in Visual Studio (Modify > Individual components > VC++ 2022 latest ARM64 tools), or install LLVM (winget install LLVM.LLVM)."
            }
        }
    }

    # ── 1. Rust build ──────────────────────────────────────────────────────
    if (-not $TestOnly) {
        Step "Building Rust C ABI — $arch"
        Push-Location "$root/rust-core"
        $cargoArgs = @("build")
        if ($Profile -eq "release") { $cargoArgs += "--release" }
        if ($cargoTarget)           { $cargoArgs += "--target", $cargoTarget }
        Run "cargo" $cargoArgs
        Pop-Location
        Ok "Rust $arch build succeeded"
    }

    $coreDll = "$rustOutDir/automerge_core.dll"
    if (-not (Test-Path $coreDll)) { Err "automerge_core.dll not found at $coreDll" }
    Ok "Found: $coreDll"

    # ── 2. Rust tests (x64 only — ARM64 binaries can't run on x64 host) ────
    if (-not $isArm64) {
        Step "Running Rust tests"
        Push-Location "$root/rust-core"
        Run "cargo" @("test")
        Pop-Location
        Ok "Rust tests passed"
    } else {
        Write-Host "  Skipping Rust test execution (ARM64 on x64 host)" -ForegroundColor Yellow
    }

    # ── 3. C++ wrapper ─────────────────────────────────────────────────────
    if (-not $SkipCpp) {
        if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
            Write-Host "  cmake not found — skipping C++ build." -ForegroundColor Yellow
        } else {
            Step "Building C++ wrapper — $arch"
            if (-not (Test-Path $cppBuildDir)) { New-Item -ItemType Directory $cppBuildDir | Out-Null }
            Push-Location $cppBuildDir
            $cmakeArch = if ($isArm64) { "arm64" } else { "x64" }
            Run "cmake" @("..", "-G", "Ninja",
                "-DCMAKE_BUILD_TYPE=$cmakeType",
                "-DAUTOMERGE_BUILD_TESTS=$(if($isArm64){'OFF'}else{'ON'})",
                "-DAUTOMERGE_TARGET_ARCH=$cmakeArch",
                "-DAUTOMERGE_RUST_PROFILE=$Profile")
            Run "cmake" @("--build", ".")
            if (-not $isArm64) {
                Step "Running C++ tests"
                Run "ctest" @("--output-on-failure")
                Ok "C++ tests passed"
            } else {
                Write-Host "  Skipping C++ test execution (ARM64 on x64 host)" -ForegroundColor Yellow
            }
            Pop-Location
            Ok "C++ $arch build succeeded"
        }
    }

    # ── 4. WinRT component ─────────────────────────────────────────────────
    if (-not $SkipWinRT) {
        Step "Building WinRT component — $arch"
        $winrtBuildScript = "$root/winrt-component/build-winrt.ps1"
        if (Test-Path $winrtBuildScript) {
            & $winrtBuildScript -Profile $Profile -VSPath $vsPath -Arch $arch
            if ($LASTEXITCODE -ne 0) { Err "WinRT build failed for $arch" }
            Ok "WinRT $arch component built"
        } else {
            Write-Host "  WinRT build script not found — skipping." -ForegroundColor Yellow
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Main: run requested arch(es)
# ─────────────────────────────────────────────────────────────────────────────

$archList = if ($Arch -eq "all") { @("x64","arm64") } else { @($Arch) }
foreach ($a in $archList) { Build-ForArch $a }

# ─── C# wrapper + tests (host x64 only, after all native arches are built) ───
# VSCMD_ARG_TGT_ARCH is set to 'arm64' by vcvarsall x64_arm64 and causes dotnet
# to reject '-r win-x64'.  Clear it before invoking dotnet (managed code runs
# on the host x64 process regardless of which native DLL is being targeted).

if (-not $SkipCsharp) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "  dotnet not found - skipping C# build." -ForegroundColor Yellow
    } else {
        Remove-Item "env:VSCMD_ARG_TGT_ARCH" -ErrorAction SilentlyContinue
        [System.Environment]::SetEnvironmentVariable("VSCMD_ARG_TGT_ARCH", $null)
        Step "Running C# tests (x64)"
        Push-Location "$root/tests/csharp"
        Run "dotnet" @("test", "-r", "win-x64", "--logger", "console;verbosity=normal")
        Pop-Location
        Ok "C# tests passed"
    }
}

Step "All steps completed"
Ok "Build complete ($($archList -join ' + '))."

