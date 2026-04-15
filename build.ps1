#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build the entire Automerge Windows native projection stack.

.PARAMETER Profile
    Cargo profile: "debug" or "release" (default: "release").

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
    # Search for vcvarsall.bat across all VS installations, including Build Tools.
    # VS 2019 Build Tools, VS 2022 Community, VS 2026 Community are all checked.
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $searchPaths = @(
        # Enterprise/Professional first — matches GitHub Actions windows-2022 runner
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat",
        # Build Tools and Community
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvarsall.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat"
    )
    foreach ($p in $searchPaths) { if (Test-Path $p) { return $p } }
    # Fallback: use vswhere to find any VS with vcvarsall.bat
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

# Set up MSVC x64 environment variables (equivalent to vcvarsall.bat x64)
function Setup-VCEnv {
    $envDump = cmd /c "`"$vcvarsAll`" x64 > NUL 2>&1 && set"
    foreach ($line in $envDump) {
        if ($line -match "^([^=]+)=(.*)$") {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2])
            Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] -ErrorAction SilentlyContinue
        }
    }
}

Setup-VCEnv
Ok "MSVC x64 environment configured"

# Ensure cargo is in PATH
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Users\$env:USERNAME\.cargo\bin;" + $env:PATH
}
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { Err "cargo not found. Install Rust via rustup." }

# Ensure cmake/ninja are in PATH
foreach ($p in @("C:\Program Files\CMake\bin")) {
    if (Test-Path $p) { $env:PATH = "$p;" + $env:PATH }
}

# ─── 1. Rust C ABI ────────────────────────────────────────────────────────────

if (-not $TestOnly) {
    Step "Building Rust C ABI (automerge_core)"
    Push-Location "$root/rust-core"
    $cargoArgs = @("build")
    if ($Profile -eq "release") { $cargoArgs += "--release" }
    Run "cargo" $cargoArgs
    Pop-Location
    Ok "Rust build succeeded"
}

$rustOutDir = "$root/rust-core/target/$Profile"
$coreDll = "$rustOutDir/automerge_core.dll"
if (-not (Test-Path $coreDll)) { Err "automerge_core.dll not found at $coreDll" }
Ok "Found: $coreDll"

# ─── 2. Rust tests ────────────────────────────────────────────────────────────

Step "Running Rust tests"
Push-Location "$root/rust-core"
Run "cargo" @("test")
Pop-Location
Ok "Rust tests passed"

# ─── 3. C++ wrapper + tests ───────────────────────────────────────────────────

if (-not $SkipCpp) {
    if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
        Write-Host "  cmake not found - skipping C++ build." -ForegroundColor Yellow
    } else {
        Step "Building C++ wrapper and tests (MSVC x64)"
        $buildDir = "$root/build"
        if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory $buildDir | Out-Null }
        $cmakeType = if ($Profile -eq "release") { "Release" } else { "Debug" }
        Push-Location $buildDir
        # Use Ninja + MSVC; no custom toolchain file needed (MSVC auto-detected via vcvarsall)
        Run "cmake" @("..", "-G", "Ninja", "-DCMAKE_BUILD_TYPE=$cmakeType", "-DAUTOMERGE_BUILD_TESTS=ON")
        Run "cmake" @("--build", ".")
        Step "Running C++ tests"
        Run "ctest" @("--output-on-failure")
        Pop-Location
        Ok "C++ tests passed"
    }
}

# ─── 4. WinRT component ───────────────────────────────────────────────────────

if (-not $SkipWinRT) {
    Step "Building WinRT component (Automerge.Windows.dll + .winmd)"
    $winrtBuildScript = "$root/winrt-component/build-winrt.ps1"
    if (Test-Path $winrtBuildScript) {
        & $winrtBuildScript -Profile $Profile -VSPath $vsPath
        if ($LASTEXITCODE -ne 0) { Err "WinRT build failed" }
        Ok "WinRT component built"
    } else {
        Write-Host "  WinRT build script not found — skipping." -ForegroundColor Yellow
    }
}

# ─── 5. C# wrapper + tests ────────────────────────────────────────────────────

if (-not $SkipCsharp) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "  dotnet not found - skipping C# build." -ForegroundColor Yellow
    } else {
        Step "Running C# tests (x64)"
        Push-Location "$root/tests/csharp"
        Run "dotnet" @("test", "-r", "win-x64", "--logger", "console;verbosity=normal")
        Pop-Location
        Ok "C# tests passed"
    }
}

Step "All steps completed"
Ok "Build complete."
