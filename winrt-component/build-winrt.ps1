#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build the Automerge.Windows WinRT Runtime Component.

    Invoked by the top-level build.ps1 after the C++ wrapper and Rust DLL
    have been built.  Can also be run standalone for incremental development.

.PARAMETER Profile
    "debug" or "release" (default: "release").

.PARAMETER VSPath
    Path to the Visual Studio installation root (auto-detected from vswhere
    if omitted).
#>
param(
    [ValidateSet("debug","release")]
    [string]$Profile = "release",
    [string]$VSPath  = "",
    [ValidateSet("x64","arm64")]
    [string]$Arch    = "x64"
)

$ErrorActionPreference = "Stop"

$root    = Split-Path $PSScriptRoot -Parent
$srcDir  = $PSScriptRoot
# Architecture-specific output directory: build-winrt/release or build-winrt-arm64/release
$outBase = if ($Arch -eq "arm64") { "$root\build-winrt-arm64" } else { "$root\build-winrt" }
$outDir  = "$outBase\$Profile"
$intDir  = "$outDir\int"
$genDir  = "$intDir\generated"

# ─── Locate Visual Studio ──────────────────────────────────────────────────────

if (-not $VSPath) {
    # Search across all VS flavours for the one with working vcvarsall.bat.
    # Enterprise is listed first so GitHub Actions windows-2022 is matched
    # before the fallback paths.
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools",
        "C:\Program Files\Microsoft Visual Studio\2022\Community",
        "C:\Program Files\Microsoft Visual Studio\18\Community"
    )
    foreach ($c in $candidates) {
        if (Test-Path "$c\VC\Auxiliary\Build\vcvarsall.bat") { $VSPath = $c; break }
    }
    # Final fallback: use vswhere.exe to find any installed VS
    if (-not $VSPath) {
        $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswhere) {
            $allPaths = & $vswhere -all -products * -property installationPath 2>$null
            foreach ($vp in $allPaths) {
                if (Test-Path "$vp\VC\Auxiliary\Build\vcvarsall.bat") { $VSPath = $vp; break }
            }
        }
    }
}
if (-not $VSPath) { throw "No Visual Studio installation with vcvarsall.bat found." }

# ─── Set up MSVC developer environment via vcvarsall.bat ─────────────────────
# VS 2019 Build Tools is the only installation with a complete MSVC (headers +
# libs + compiler).  VS 2022/2026 Community installs on this machine have cl.exe
# but are missing the include\ directory (partial install), so we always source
# the environment from VS 2019's vcvarsall.bat.

$vcvarsAll = "$VSPath\VC\Auxiliary\Build\vcvarsall.bat"
Write-Host "Sourcing MSVC env from: $vcvarsAll" -ForegroundColor Gray
# x64_arm64 sets up the ARM64 cross-compiler/linker in PATH (HostX64\arm64\)
$vcArgs = if ($Arch -eq "arm64") { "x64_arm64" } else { "x64" }
$envDump = cmd /c "`"$vcvarsAll`" $vcArgs > NUL 2>&1 && set"
foreach ($line in $envDump) {
    if ($line -match "^([^=]+)=(.*)$") {
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2])
        Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] -ErrorAction SilentlyContinue
    }
}

# ─── Locate MSVC and Windows SDK tools ────────────────────────────────────────

$msvcToolsBase = "$VSPath\VC\Tools\MSVC"
$msvcVer = Get-ChildItem $msvcToolsBase | Sort-Object Name | Select-Object -Last 1 -ExpandProperty Name
# x64 host → x64 output: HostX64\x64\cl.exe
# x64 host → arm64 output (cross): HostX64\arm64\cl.exe
$msvcHostTarget = if ($Arch -eq "arm64") { "arm64" } else { "x64" }
$msvcBin = "$msvcToolsBase\$msvcVer\bin\Hostx64\$msvcHostTarget"
$msvcLib = if (Test-Path "$msvcToolsBase\$msvcVer\lib\$msvcHostTarget\msvcrt.lib") {
    "$msvcToolsBase\$msvcVer\lib\$msvcHostTarget"
} else {
    "$msvcToolsBase\$msvcVer\lib\onecore\$msvcHostTarget"
}
$msvcInc = "$msvcToolsBase\$msvcVer\include"

$sdkRoot = "C:\Program Files (x86)\Windows Kits\10"
# Prefer SDK 10.0.22621.0 for compilation: its cppwinrt headers are compatible
# with MSVC 14.29 (VS 2019).  SDK 10.0.26100.0's winrt/base.h triggers a C1001
# internal compiler error in MSVC 14.29 due to newer C++20 template constructs.
$sdkVer = if (Test-Path "$sdkRoot\Include\10.0.22621.0\cppwinrt") {
    "10.0.22621.0"
} else {
    Get-ChildItem "$sdkRoot\Include" | Where-Object { $_.Name -match "^\d" } |
        Sort-Object Name | Select-Object -Last 1 -ExpandProperty Name
}

# vcvarsall.bat points INCLUDE/LIB at whatever the latest SDK is (10.0.26100.0).
# Override those env vars now to use our chosen SDK version instead so the
# compiler never accidentally picks up an incompatible winrt/base.h.
$latestSdkVer = Get-ChildItem "$sdkRoot\Include" |
    Where-Object { $_.Name -match "^\d" } |
    Sort-Object Name | Select-Object -Last 1 -ExpandProperty Name
if ($latestSdkVer -ne $sdkVer) {
    Write-Host "  Overriding INCLUDE/LIB from SDK $latestSdkVer -> $sdkVer" -ForegroundColor Gray
    $env:INCLUDE = $env:INCLUDE -replace [regex]::Escape($latestSdkVer), $sdkVer
    $env:LIB     = $env:LIB     -replace [regex]::Escape($latestSdkVer), $sdkVer
    $env:LIBPATH = $env:LIBPATH -replace [regex]::Escape($latestSdkVer), $sdkVer
}
$sdkBin       = "$sdkRoot\bin\$sdkVer\x64"
$sdkIncWinrt  = "$sdkRoot\Include\$sdkVer\winrt"
$sdkIncCppWinrt = "$sdkRoot\Include\$sdkVer\cppwinrt"
$sdkIncUm     = "$sdkRoot\Include\$sdkVer\um"
$sdkIncShared = "$sdkRoot\Include\$sdkVer\shared"
$sdkIncUcrt   = "$sdkRoot\Include\$sdkVer\ucrt"
$sdkRef       = "$sdkRoot\UnionMetadata\$sdkVer"  # Contains Windows.winmd
# SDK libs: pick arm64 or x64 depending on target
$sdkLibArch   = if ($Arch -eq "arm64") { "arm64" } else { "x64" }
$sdkLibUm     = "$sdkRoot\Lib\$sdkVer\um\$sdkLibArch"
$sdkLibUcrt   = "$sdkRoot\Lib\$sdkVer\ucrt\$sdkLibArch"

$midl     = "$sdkBin\midl.exe"
$cppwinrt = "$sdkBin\cppwinrt.exe"
$cl       = "$msvcBin\cl.exe"
$link     = "$msvcBin\link.exe"

foreach ($tool in @($midl, $cppwinrt, $cl, $link)) {
    if (-not (Test-Path $tool)) { throw "Tool not found: $tool" }
}
Write-Host "SDK $sdkVer   MSVC $msvcVer" -ForegroundColor Gray

# ─── Input paths ──────────────────────────────────────────────────────────────

$cargoProfile = if ($Profile -eq "release") { "release" } else { "debug" }
# ARM64 cargo output is under target/aarch64-pc-windows-msvc/<profile>/
$rustOutDir   = if ($Arch -eq "arm64") {
    "$root\rust-core\target\aarch64-pc-windows-msvc\$cargoProfile"
} else {
    "$root\rust-core\target\$cargoProfile"
}
$cppBuildDir  = if ($Arch -eq "arm64") { "$root\build-arm64" } else { "$root\build" }

# Try both MSVC (.dll.lib) and MinGW (.dll.a) import library names
$coreImportLib = if (Test-Path "$rustOutDir\automerge_core.dll.lib") {
    "$rustOutDir\automerge_core.dll.lib"
} elseif (Test-Path "$rustOutDir\automerge_core.lib") {
    "$rustOutDir\automerge_core.lib"
} else {
    throw "automerge_core import library not found in $rustOutDir"
}

# Static lib name differs by toolchain: MSVC = automerge_wrapper.lib, MinGW = libautomerge_wrapper.a
$wrapperLib = if (Test-Path "$cppBuildDir\automerge_wrapper.lib") {
    "$cppBuildDir\automerge_wrapper.lib"
} elseif (Test-Path "$cppBuildDir\libautomerge_wrapper.a") {
    "$cppBuildDir\libautomerge_wrapper.a"
} else {
    throw "automerge_wrapper static library not found in $cppBuildDir. Run the C++ build first."
}

Write-Host "Rust import lib : $coreImportLib" -ForegroundColor Gray
Write-Host "C++ wrapper lib : $wrapperLib"     -ForegroundColor Gray

# ─── Create output directories ────────────────────────────────────────────────

@($outDir, $intDir, $genDir) | ForEach-Object { New-Item -ItemType Directory -Force $_ | Out-Null }

# ─── Step 1 : Compile IDL → .winmd ───────────────────────────────────────────

Write-Host "`nStep 1: MIDL -> Automerge.Windows.winmd" -ForegroundColor Cyan

$midlEnv = if ($Arch -eq "arm64") { "arm64" } else { "x64" }
& $midl /W1 /WX /char signed /env $midlEnv /nologo /error all `
    /winrt `
    /metadata_dir $sdkRef `
    /h  "$intDir\Automerge.Windows_midl.h" `
    /dlldata NUL /iid NUL /proxy NUL `
    /I $sdkIncWinrt `
    /out $intDir `
    "$srcDir\Automerge.Windows.idl"

if ($LASTEXITCODE -ne 0) { throw "MIDL compilation failed." }
Write-Host "  -> $intDir\Automerge.Windows.winmd" -ForegroundColor Gray

# ─── Step 2 : cppwinrt.exe → C++/WinRT projection headers ───────────────────

Write-Host "`nStep 2: cppwinrt -> projection headers + module.g.cpp" -ForegroundColor Cyan

# -in  : the component's own winmd
# -ref : platform Windows winmd references (explicit path to UnionMetadata)
# -out : where to write projected headers (winrt/Automerge.Windows.h)
# -comp: where to write implementation boilerplate (*.g.h, module.g.cpp)
$windowsWinmd = "$sdkRoot\UnionMetadata\$sdkVer\Windows.winmd"
& $cppwinrt `
    -in  "$intDir\Automerge.Windows.winmd" `
    -ref $windowsWinmd `
    -out $genDir `
    -comp $genDir

if ($LASTEXITCODE -ne 0) { throw "cppwinrt failed." }
Write-Host "  -> $genDir\Automerge.Windows.g.h + module.g.cpp" -ForegroundColor Gray

# Remove cppwinrt-generated stub implementation files.  module.g.cpp includes
# "Document.h" / "SyncState.h" and searches its own directory (genDir) first,
# which would find these stubs (containing static_assert(false)).  We provide
# our own implementations in $srcDir, so delete the generated stubs now.
Remove-Item "$genDir\Document.h"   -ErrorAction SilentlyContinue
Remove-Item "$genDir\Document.cpp" -ErrorAction SilentlyContinue
Remove-Item "$genDir\SyncState.h"   -ErrorAction SilentlyContinue
Remove-Item "$genDir\SyncState.cpp" -ErrorAction SilentlyContinue

# ─── Common compiler flags ────────────────────────────────────────────────────

$opt = if ($Profile -eq "release") { "/O2" } else { "/Od /Zi" }
$rtLib = if ($Profile -eq "debug") { "/MDd" } else { "/MD" }

# Add project include dirs to INCLUDE env so they are always searched.
# Using INCLUDE is more reliable than /I flags with embedded quotes in PowerShell.
$env:INCLUDE = "$srcDir;$genDir;$($root)\cpp-wrapper\include;$($root)\rust-core\include;" + $env:INCLUDE

$clFlags = @(
    "/nologo", "/W3", "/WX-", "/std:c++20", "/EHsc", $rtLib, $opt,
    "/DNOMINMAX", "/DWIN32_LEAN_AND_MEAN", "/DWINRT_LEAN_AND_MEAN"
)

$linkLibPaths = @(
    "/LIBPATH:`"$msvcLib`"",
    "/LIBPATH:`"$sdkLibUm`"",
    "/LIBPATH:`"$sdkLibUcrt`""
)
# (No precompiled headers; /Yc//Yu PCH matching is fragile with /FI redirection
#  and doesn't buy much for a three-file project.)

Write-Host "`nStep 3: Compile source files" -ForegroundColor Cyan

foreach ($src in @("Document.cpp","SyncState.cpp","dll_exports.cpp")) {
    $obj = "$intDir\$([IO.Path]::GetFileNameWithoutExtension($src)).obj"
    Write-Host "  cl $src" -ForegroundColor Gray
    & $cl @clFlags /Fo"$obj" /c "$srcDir\$src"
    if ($LASTEXITCODE -ne 0) { throw "Compilation of $src failed." }
}

Write-Host "  cl module.g.cpp" -ForegroundColor Gray
& $cl @clFlags /Fo"$intDir\module.g.obj" /c "$genDir\module.g.cpp"
if ($LASTEXITCODE -ne 0) { throw "Compilation of module.g.cpp failed." }

# ─── Step 5 : Link DLL ────────────────────────────────────────────────────────

Write-Host "`nStep 5: Link Automerge.Windows.dll" -ForegroundColor Cyan

$objs = @(
    "$intDir\Document.obj",
    "$intDir\SyncState.obj",
    "$intDir\dll_exports.obj",
    "$intDir\module.g.obj"
)

$debugFlags  = if ($Profile -eq "debug") { @("/DEBUG") } else { @() }
$machineFlag = if ($Arch -eq "arm64") { "/MACHINE:ARM64" } else { "/MACHINE:X64" }

# Note: MSVC/SDK lib dirs are already in $env:LIB (set by vcvarsall + our SDKVer
# override above), so no explicit /LIBPATH: flags are needed.
# Project paths (wrapperLib, coreImportLib, objs) have no spaces so need no quoting.
& $link /nologo /DLL /SUBSYSTEM:WINDOWS `
    @debugFlags `
    $machineFlag `
    /OUT:$outDir\Automerge.Windows.dll `
    /IMPLIB:$outDir\Automerge.Windows.lib `
    @objs `
    $wrapperLib `
    $coreImportLib `
    RuntimeObject.lib ole32.lib oleaut32.lib

if ($LASTEXITCODE -ne 0) { throw "Linking failed." }

# ─── Step 6 : Copy .winmd to output ──────────────────────────────────────────

Copy-Item "$intDir\Automerge.Windows.winmd" "$outDir\Automerge.Windows.winmd" -Force

# ─── Done ─────────────────────────────────────────────────────────────────────

Write-Host "`nWinRT component built successfully!" -ForegroundColor Green
Write-Host "  DLL  : $outDir\Automerge.Windows.dll"  -ForegroundColor Gray
Write-Host "  WINMD: $outDir\Automerge.Windows.winmd" -ForegroundColor Gray
