# Building Automerge.Windows

Complete developer guide for building, testing, and continuing development.

---

## Table of Contents

1. [Repository structure](#1-repository-structure)
2. [Architecture overview](#2-architecture-overview)
3. [Current state of completion](#3-current-state-of-completion)
4. [Prerequisites](#4-prerequisites)
5. [Quickstart — building everything](#5-quickstart--building-everything)
6. [Layer-by-layer build guide](#6-layer-by-layer-build-guide)
7. [Toolchain decisions and known constraints](#7-toolchain-decisions-and-known-constraints)
8. [Test strategy](#8-test-strategy)

---

## 1. Repository structure

```
Automerge.Windows/
├── build.ps1                    # Top-level build + test script
├── CMakeLists.txt               # C++ wrapper + GoogleTest
├── rust-core/                   # Layer 1 — Rust → C ABI (automerge_core.dll)
│   ├── Cargo.toml
│   ├── rust-toolchain.toml      # Pins stable-x86_64-pc-windows-msvc
│   ├── include/automerge_core.h # Hand-maintained C header (ABI contract)
│   └── src/
├── cpp-wrapper/                 # Layer 2 — C++ RAII wrapper (automerge_wrapper.lib)
│   ├── CMakeLists.txt
│   ├── include/automerge/
│   │   ├── Document.hpp
│   │   ├── SyncState.hpp
│   │   └── Error.hpp
│   └── src/
├── winrt-component/             # Layer 3 — WinRT projection (Automerge.Windows.dll)
│   ├── Automerge.Windows.idl    # WinRT interface definitions
│   ├── Document.h / Document.cpp
│   ├── SyncState.h / SyncState.cpp
│   ├── Helpers.hpp              # IBuffer ↔ vector/span helpers
│   ├── dll_exports.cpp          # DllGetActivationFactory / DllCanUnloadNow
│   ├── pch.h                    # Precompiled header root
│   └── build-winrt.ps1         # Standalone WinRT build script
├── csharp-wrapper/              # Layer 4 — C# P/Invoke wrapper
│   ├── AutomergeWindows.csproj
│   ├── NativeMethods.cs
│   ├── Document.cs / SyncState.cs / Extensions.cs
└── tests/
    ├── cpp/                     # GoogleTest suite (29 tests)
    ├── csharp/                  # xUnit P/Invoke suite (29 tests)
    └── winrt/                   # xUnit WinRT projection suite (13 tests)
        ├── AutomergeWinRTTests.csproj
        ├── WinRTDocumentTests.cs
        └── WinRTSyncTests.cs
```

---

## 2. Architecture overview

```
┌─────────────────────────────────────────────┐
│  WinRT Test Suite (C# via CsWinRT)          │  tests/winrt/  (13 tests)
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│  WinRT Projection Layer                     │  Automerge.Windows.dll + .winmd
│  (C++/WinRT; IBuffer ↔ Automerge ops)       │
└──────────────────┬──────────────────────────┘
                   │ (static link)
┌──────────────────▼──────────────────────────┐
│  C++ RAII Wrapper (automerge_wrapper.lib)   │  cpp-wrapper/
│  automerge::Document / SyncState            │
└──────────────────┬──────────────────────────┘
                   │ (import lib)
┌──────────────────▼──────────────────────────┐
│  C ABI — Rust (automerge_core.dll)          │  rust-core/
│  AMcreate_doc, AMsave, AMmerge …            │
└─────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│  C# P/Invoke wrapper (AutomergeWindows.dll) │  csharp-wrapper/ (29 tests)
│  calls automerge_core.dll directly          │
└──────────────────────────────────────────────┘
```

The C ABI (`automerge_core.h`) is the **stable ABI contract**.  Everything above
it is a language-level projection over the same Rust implementation.

---

## 3. Current state of completion

| Layer | Files | Status | Tests |
|---|---|---|---|
| Rust C ABI (`automerge_core.dll`) | `rust-core/` | ✅ Complete | 24 passing |
| C++ RAII wrapper | `cpp-wrapper/` | ✅ Complete | 29 passing |
| WinRT projection | `winrt-component/` | ✅ Complete | 13 passing |
| C# P/Invoke wrapper | `csharp-wrapper/` | ✅ Complete | 29 passing |

**Total: 95 tests passing.**

---

## 4. Prerequisites

### All machines

| Tool | Version | How to install |
|---|---|---|
| Git | any | — |
| Rust (via rustup) | stable | `winget install Rustlang.Rustup` then `rustup show` |
| CMake | 3.25+ | `winget install Kitware.CMake` |
| Ninja | 1.12+ | `winget install Ninja-build.Ninja` |
| .NET SDK | 9.0+ | `winget install Microsoft.DotNet.SDK.9` |
| **VS 2019 Build Tools** | 14.29.x | Required — has complete MSVC (headers + libs) |

> **IMPORTANT**: The WinRT component uses **Windows SDK 10.0.22621.0** for compilation
> (to avoid a C1001 internal compiler error in MSVC 14.29 when compiling against
> the newer SDK's cppwinrt headers).  Both SDK versions must be installed.

### VS 2019 Build Tools (required for WinRT component)

Download [VS 2019 Build Tools](https://visualstudio.microsoft.com/vs/older-downloads/)
and install the **"C++ build tools"** workload.  This provides:
- `vcvarsall.bat` at `C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\`
- Full MSVC headers (`include\`) and libraries (`lib\x64\`)
- `cl.exe`, `link.exe`

> **Note on VS 2022/2026**: Community editions of VS 2022 and VS 2026 installed
> via the normal installer on this machine do NOT include the MSVC `include\`
> directory (only `bin\` and `lib\` are present).  Only VS 2019 Build Tools has
> a complete installation.  `build-winrt.ps1` always uses VS 2019 via
> `vcvarsall.bat`.

### Windows SDK versions needed

- **10.0.22621.0** — used for WinRT compilation (cppwinrt headers, um, ucrt, winrt)
- **10.0.26100.0** (or any newer) — used for `midl.exe` and `cppwinrt.exe` tools

Both SDKs are installed by default when you install VS Build Tools.

---

## 5. Quickstart — building everything

```powershell
# From repo root:
.\build.ps1
```

What the script does:
1. Locate `vcvarsall.bat` (VS 2019 Build Tools → VS 2022 → VS 2026)
2. Build `automerge_core.dll` via `cargo build --release`
3. Run 24 Rust C-API tests
4. Configure C++ wrapper + tests with CMake + Ninja
5. Run 29 C++ GoogleTest tests
6. Build C# P/Invoke wrapper; run 29 xUnit tests
7. Build WinRT component (calls `winrt-component\build-winrt.ps1`)
8. Run 13 WinRT xUnit tests

### build.ps1 options

```powershell
.\build.ps1                  # Full build + test (release profile)
.\build.ps1 -Profile debug   # Debug build
.\build.ps1 -SkipCpp         # Skip CMake / C++ steps
.\build.ps1 -SkipCsharp      # Skip dotnet / C# steps
.\build.ps1 -TestOnly        # Just run tests (requires pre-built artifacts)
```

### Standalone WinRT build

```powershell
.\winrt-component\build-winrt.ps1 -Profile release
```

Output:
- `build-winrt\release\Automerge.Windows.dll`  — WinRT component DLL
- `build-winrt\release\Automerge.Windows.lib`  — import library
- `build-winrt\release\Automerge.Windows.winmd` — WinRT metadata

---

## 6. Layer-by-layer build guide

### Layer 1 — Rust C ABI

```powershell
cd rust-core
cargo build --release
cargo test
```

**Output:** `target/release/automerge_core.dll` + `target/release/automerge_core.dll.lib`

The C header `rust-core/include/automerge_core.h` is **hand-maintained** (no
cbindgen) so the ABI contract is stable and version-controlled independently.

The toolchain is pinned in `rust-core/rust-toolchain.toml`:
```toml
[toolchain]
channel = "stable-x86_64-pc-windows-msvc"
```

### Layer 2 — C++ RAII wrapper

```powershell
# First: set up the MSVC environment
& "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64

# Then from repo root:
cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Release -DAUTOMERGE_BUILD_TESTS=ON
cmake --build build
cd build && ctest --output-on-failure
```

**Output:** `build\automerge_wrapper.lib`

### Layer 3 — WinRT projection

```powershell
.\winrt-component\build-winrt.ps1 -Profile release
```

The script runs these steps:
1. **MIDL** → `Automerge.Windows.winmd` (from `Automerge.Windows.idl`)
2. **cppwinrt** → generated C++/WinRT projection headers + `module.g.cpp`
3. **cl.exe** → compile `Document.cpp`, `SyncState.cpp`, `dll_exports.cpp`, `module.g.cpp`
4. **link.exe** → `Automerge.Windows.dll` (linking `automerge_wrapper.lib` + `automerge_core.dll.lib`)
5. Copy `.winmd` to output

**Known toolchain constraint:** MSVC 14.29 (VS 2019) has a C1001 internal compiler
error when compiling Windows SDK 10.0.26100.0 cppwinrt headers (`winrt/base.h`).
`build-winrt.ps1` works around this by:
1. Calling `vcvarsall.bat x64` (which sets up INCLUDE/LIB for the latest SDK)
2. Replacing the SDK version in `INCLUDE` and `LIB` from `10.0.26100.0` to `10.0.22621.0`

This ensures the compiler uses the older (compatible) cppwinrt headers.

### Layer 4 — C# P/Invoke wrapper

```powershell
cd tests/csharp
dotnet test -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

### WinRT tests

```powershell
cd tests/winrt
dotnet test -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

---

## 7. Toolchain decisions and known constraints

### Architecture: x64 MSVC

This project targets **x64 (AMD64) Windows** with the MSVC toolchain.

- `rust-toolchain.toml` pins `stable-x86_64-pc-windows-msvc`
- C++ is compiled with `cl.exe` from VS 2019 Build Tools
- MSVC import libraries use `.dll.lib` extension (not MinGW's `.dll.a`)

### MSVC 14.29 + SDK 10.0.26100.0 incompatibility

Windows SDK 10.0.26100.0's `winrt/base.h` uses C++20 template features that
trigger `fatal error C1001: Internal compiler error` (ICE) in MSVC 14.29 at
`winrt/base.h(5069)`.  The older SDK 10.0.22621.0 does not have this problem.

**Workaround in `build-winrt.ps1`:**
```powershell
# After vcvarsall.bat sets INCLUDE/LIB to 10.0.26100.0:
$env:INCLUDE = $env:INCLUDE -replace "10\.0\.26100\.0", "10.0.22621.0"
$env:LIB     = $env:LIB     -replace "10\.0\.26100\.0", "10.0.22621.0"
```

If VS 2022/2026 is ever installed with complete MSVC headers (`include\` directory),
update `build-winrt.ps1` to prefer that compiler with the latest SDK.

### WinRT namespace lookup

Inside `namespace winrt::Automerge::Windows::implementation`, the name `Windows`
resolves to `winrt::Automerge::Windows` (the parent namespace), NOT `winrt::Windows`.
All references to `Windows::Storage::Streams::IBuffer` therefore require the full
`winrt::Windows::Storage::Streams::IBuffer` prefix in headers and `.cpp` files.

### DLL exports

The WinRT component uses C++/WinRT 2.0 which generates `WINRT_GetActivationFactory`
and `WINRT_CanUnloadNow` (with C linkage, from `extern "C"` in `winrt/base.h`).
`dll_exports.cpp` provides the standard COM names `DllGetActivationFactory` and
`DllCanUnloadNow` as forwarding wrappers.

### Module.g.cpp include resolution

`module.g.cpp` (generated by cppwinrt) includes `"Document.h"` and `"SyncState.h"`.
Because it lives in the `generated/` subdirectory, the compiler searches `generated/`
first for those includes — which would find the generated **stubs** with
`static_assert(false, ...)`.  `build-winrt.ps1` deletes the generated stubs after
running cppwinrt so the compiler falls back to the INCLUDE environment, which has
the `winrt-component/` source directory first.

---

## 8. Test strategy

### Rust (24 tests) — `rust-core/tests/c_api_tests.rs`

Tests the raw C ABI directly in Rust using `extern "C"` declarations.  Covers:
create/destroy, save/load, heads, changes (full + incremental), apply, merge,
sync (one-way + bidirectional), error handling.

### C++ (29 tests) — `tests/cpp/`

GoogleTest suite against `automerge_wrapper.lib`.  Tests RAII lifecycle, move
semantics, persistence, heads, changes, merge, values, sync.

### C# P/Invoke (29 tests) — `tests/csharp/`

xUnit suite via `AutomergeWindows.dll` (P/Invoke).  Covers the same operations
plus `IDisposable` safety, double-dispose.

### WinRT projection (13 tests) — `tests/winrt/`

xUnit suite using **CsWinRT**-generated projections of `Automerge.Windows.winmd`.
Tests the WinRT activation factory, `Document` and `SyncState` runtime classes
via the WinRT ABI (IBuffer, hstring).

CsWinRT generates C# projection code at build time from the `.winmd`.  The
`dotnet test` command copies `Automerge.Windows.dll` and `automerge_core.dll`
to the test output directory so the WinRT activation factory can load them
at runtime without COM registration.
