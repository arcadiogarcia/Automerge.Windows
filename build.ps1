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

.PARAMETER TestOnly
    Run tests without rebuilding.
#>
param(
    [ValidateSet("debug", "release")]
    [string]$Profile = "release",
    [switch]$SkipCpp,
    [switch]$SkipCsharp,
    [switch]$TestOnly
)

$root = $PSScriptRoot

function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function Ok($msg)    { Write-Host "  OK  $msg" -ForegroundColor Green }
function Err($msg)   { Write-Host "  ERR $msg" -ForegroundColor Red; exit 1 }
function Run {
    param([string]$Exe, [string[]]$ArgList)
    & $Exe @ArgList
    if ($LASTEXITCODE -ne 0) { Err "$Exe failed with exit code $LASTEXITCODE" }
}

# Configure PATH
$env:PATH = "C:\Users\arcad\.cargo\bin;C:\Program Files\CMake\bin;C:\Program Files\LLVM\bin;C:\llvm-mingw\llvm-mingw-20260407-ucrt-aarch64\bin;C:\Users\arcad\AppData\Local\ninja;" + $env:PATH

$gnullvm = "stable-aarch64-pc-windows-gnullvm"

# 1. Rust C ABI
if (-not $TestOnly) {
    Step "Building Rust C ABI (automerge_core)"
    Push-Location "$root/rust-core"
    $cargoArgs = @("+$gnullvm", "build")
    if ($Profile -eq "release") { $cargoArgs += "--release" }
    Run "cargo" $cargoArgs
    Pop-Location
    Ok "Rust build succeeded"
}

$rustOutDir = "$root/rust-core/target/$Profile"
$coreDll = "$rustOutDir/automerge_core.dll"
if (-not (Test-Path $coreDll)) { Err "automerge_core.dll not found at $coreDll" }
Ok "Found: $coreDll"

# 2. Rust tests
Step "Running Rust tests"
Push-Location "$root/rust-core"
Run "cargo" @("+$gnullvm", "test")
Pop-Location
Ok "Rust tests passed"

# 3. C++ wrapper + tests
if (-not $SkipCpp) {
    if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
        Write-Host "  cmake not found - skipping C++ build." -ForegroundColor Yellow
    } else {
        Step "Building C++ wrapper and tests"
        $buildDir = "$root/build"
        if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory $buildDir | Out-Null }
        $cmakeType = if ($Profile -eq "release") { "Release" } else { "Debug" }
        Push-Location $buildDir
        Run "cmake" @("..", "-G", "Ninja", "-DCMAKE_TOOLCHAIN_FILE=../cmake/toolchain-arm64-mingw.cmake", "-DCMAKE_BUILD_TYPE=$cmakeType", "-DAUTOMERGE_BUILD_TESTS=ON")
        Run "cmake" @("--build", ".")
        Step "Running C++ tests"
        Run "ctest" @("--output-on-failure")
        Pop-Location
        Ok "C++ tests passed"
    }
}

# 4. C# wrapper + tests
if (-not $SkipCsharp) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "  dotnet not found - skipping C# build." -ForegroundColor Yellow
    } else {
        Step "Running C# tests"
        Push-Location "$root/tests/csharp"
        Run "dotnet" @("test", "-r", "win-arm64", "--logger", "console;verbosity=normal")
        Pop-Location
        Ok "C# tests passed"
    }
}

Step "All steps completed"
Ok "Build complete."
