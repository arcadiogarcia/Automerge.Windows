# Building Automerge.Windows

This document is the **complete developer guide** for building, testing, and
continuing development of this repository.  It documents every decision made
during the initial implementation, every environment constraint we hit, and how
to finish the remaining work (primarily the WinRT component) on a machine with
the full Windows SDK.

---

## Table of Contents

1. [Repository structure](#1-repository-structure)
2. [Architecture overview](#2-architecture-overview)
3. [Current state of completion](#3-current-state-of-completion)
4. [Prerequisites](#4-prerequisites)
5. [Quickstart — building everything](#5-quickstart--building-everything)
6. [Layer-by-layer build guide](#6-layer-by-layer-build-guide)
7. [Toolchain decisions and known constraints](#7-toolchain-decisions-and-known-constraints)
8. [Continuing on a full Windows SDK machine](#8-continuing-on-a-full-windows-sdk-machine)
9. [Test strategy](#9-test-strategy)
10. [WinRT component — what remains](#10-winrt-component--what-remains)

---

## 1. Repository structure

```
Automerge.Windows/
├── build.ps1                    # Top-level build + test script
├── CMakeLists.txt               # Root CMake project (C++ wrapper + tests)
├── cmake/
│   └── toolchain-arm64-mingw.cmake  # CMake toolchain for ARM64 llvm-mingw
│
├── rust-core/                   # Layer 1 — Rust → C ABI (automerge_core.dll)
│   ├── Cargo.toml
│   ├── rust-toolchain.toml      # Pins toolchain to stable-aarch64-pc-windows-gnullvm
│   ├── build.rs                 # Minimal build script (no cbindgen)
│   ├── include/
│   │   └── automerge_core.h     # Hand-maintained C header (C ABI contract)
│   └── src/
│       ├── lib.rs
│       ├── doc.rs               # AMcreate_doc, AMsave, AMload, AMget_changes, …
│       ├── sync_state.rs        # AMcreate_sync_state, AMgenerate_sync_message, …
│       └── error.rs             # Thread-local AMget_last_error
│
├── cpp-wrapper/                 # Layer 2 — C++ RAII wrapper (automerge_wrapper.a)
│   ├── CMakeLists.txt
│   ├── include/automerge/
│   │   ├── Document.hpp         # automerge::Document class
│   │   ├── SyncState.hpp        # automerge::SyncState class
│   │   └── Error.hpp            # AutomergeError exception type
│   └── src/
│       ├── Document.cpp
│       └── SyncState.cpp
│
├── winrt-component/             # Layer 3 — C++/WinRT projection (NOT YET BUILT)
│   ├── Automerge.Windows.idl    # WinRT interface definitions
│   ├── Document.h / Document.cpp
│   ├── SyncState.h / SyncState.cpp
│   ├── Helpers.hpp              # IBuffer ↔ vector<uint8_t> conversions
│   └── pch.h / pch.cpp          # Precompiled header
│
├── csharp-wrapper/              # Layer 4 — C# P/Invoke convenience layer
│   ├── AutomergeWindows.csproj
│   ├── NativeMethods.cs         # P/Invoke declarations for automerge_core.dll
│   ├── Document.cs              # Managed Document class (IDisposable)
│   ├── SyncState.cs             # Managed SyncState class (IDisposable)
│   └── Extensions.cs            # Helper extensions + AutomergeNativeException
│
└── tests/
    ├── cpp/                     # GoogleTest suite (29 tests)
    │   ├── test_document.cpp
    │   ├── test_sync.cpp
    │   └── test_changes.cpp
    └── csharp/                  # xUnit suite (29 tests)
        ├── DocumentTests.cs
        ├── SyncTests.cs
        └── ChangesAndMergeTests.cs
```

---

## 2. Architecture overview

```
┌─────────────────────────────────────┐
│  C# Convenience Layer               │  AutomergeWindows.dll
│  (P/Invoke → C ABI directly)        │  net9.0-windows10.0.19041.0
└──────────────────┬──────────────────┘
                   │  (future: via WinRT projection)
┌──────────────────▼──────────────────┐
│  WinRT Projection Layer    [TODO]   │  Automerge.Windows.winmd + .dll
│  (C++/WinRT runtime class)          │  Builds with Windows App SDK
└──────────────────┬──────────────────┘
                   │
┌──────────────────▼──────────────────┐
│  C++ RAII Wrapper                   │  automerge_wrapper.a (static)
│  automerge::Document / SyncState    │  C++20, span<>, vector<>
└──────────────────┬──────────────────┘
                   │
┌──────────────────▼──────────────────┐
│  C ABI (Rust)                       │  automerge_core.dll + .dll.a
│  AMcreate_doc / AMsave / AMmerge …  │  ARM64 Windows
└─────────────────────────────────────┘
```

The **C ABI is the canonical ABI contract**.  Everything else is a
language-level projection.  The C# wrapper currently calls the C ABI directly
via P/Invoke; when the WinRT component is complete it will be an alternative
higher-level entry point for WinUI 3 / UWP consumers.

---

## 3. Current state of completion

| Layer | Files | Status | Tests |
|---|---|---|---|
| Rust C ABI (`automerge_core.dll`) | `rust-core/` | ✅ Complete | 24 passing |
| C++ RAII wrapper | `cpp-wrapper/` | ✅ Complete | 29 passing |
| WinRT projection | `winrt-component/` | ⚠️ Source written, not yet compiled | none |
| C# P/Invoke wrapper | `csharp-wrapper/` | ✅ Complete | 29 passing |

**Total: 82 tests passing.**

The WinRT component compiles as a **Windows Runtime Component** project which
requires the Windows App SDK NuGet packages (`Microsoft.WindowsAppSDK`,
`Microsoft.Windows.CsWinRT`, the WinRT IDL compiler `midl.exe`, and
`cppwinrt.exe`).  Those tools are not available without the full Windows SDK
and Visual Studio C++ workload.  All source code is written and ready; it only
needs a proper project file and those tools.  See
[§10](#10-winrt-component--what-remains) for the continuation plan.

---

## 4. Prerequisites

### All machines (Rust + C++ + C#)

| Tool | Version | Notes |
|---|---|---|
| Git | any | — |
| Rust (via `rustup`) | stable | installs `stable-aarch64-pc-windows-gnullvm` automatically from `rust-toolchain.toml` |
| rustup | any | needed to install gnullvm toolchain |
| LLVM / Clang (ARM64) | 22+ | `winget install LLVM.LLVM` — provides `lld-link.exe`, `llvm-ar.exe` |
| llvm-mingw (ARM64/UCRT) | 20260407+ | download from https://github.com/mstorsjo/llvm-mingw/releases — extract to `C:\llvm-mingw\` |
| CMake | 3.25+ | `winget install Kitware.CMake` |
| Ninja | 1.12+ | download single binary from https://github.com/ninja-build/ninja/releases — place in `C:\Users\<you>\AppData\Local\ninja\` |
| .NET SDK | 9.0+ | `winget install Microsoft.DotNet.SDK.9` |

### For the WinRT component (additional)

| Tool | Notes |
|---|---|
| Visual Studio 2022 (C++ workload) | Provides `cl.exe`, `link.exe`, `midl.exe` |
| Windows App SDK | NuGet: `Microsoft.WindowsAppSDK` ≥ 1.5 |
| C++/WinRT (`cppwinrt.exe`) | NuGet: `Microsoft.Windows.CsWinRT` or standalone |
| Windows SDK 10.0.19041+ | Included in VS C++ workload |

---

## 5. Quickstart — building everything

```powershell
# Clone
git clone https://github.com/arcadiogarcia/Automerge.Windows.git
cd Automerge.Windows

# Install Rust gnullvm toolchain (one-time)
rustup toolchain install stable-aarch64-pc-windows-gnullvm

# Build + test all layers
.\build.ps1
```

The script will:
1. Build `automerge_core.dll` (Rust, ARM64)
2. Run 24 Rust C-API tests
3. Configure + build C++ wrapper and GoogleTest suite
4. Run 29 C++ tests via ctest
5. Build C# wrapper + run 29 xUnit tests

### build.ps1 options

```powershell
.\build.ps1                  # Full build + test (release profile)
.\build.ps1 -Profile debug   # Debug build
.\build.ps1 -TestOnly        # Skip builds, just run tests (requires artifacts)
.\build.ps1 -SkipCpp         # Skip CMake / C++ steps
.\build.ps1 -SkipCsharp      # Skip dotnet / C# steps
```

---

## 6. Layer-by-layer build guide

### Layer 1 — Rust C ABI

```powershell
cd rust-core
cargo +stable-aarch64-pc-windows-gnullvm build --release
cargo +stable-aarch64-pc-windows-gnullvm test
```

**Output:** `target/release/automerge_core.dll` + `target/release/libautomerge_core.dll.a`

The C header is in `rust-core/include/automerge_core.h`.  It is hand-maintained
(cbindgen is not used) so that the ABI contract is stable and version-controlled
independently of the Rust implementation.

### Layer 2 — C++ RAII wrapper

```powershell
# From repo root (build/ directory is created automatically):
cmake -B build -G Ninja `
  -DCMAKE_TOOLCHAIN_FILE=cmake/toolchain-arm64-mingw.cmake `
  -DCMAKE_BUILD_TYPE=Release `
  -DAUTOMERGE_BUILD_TESTS=ON
cmake --build build
```

**Output:** `build/libautomerge_wrapper.a` + `build/tests/cpp/automerge_cpp_tests.exe`

Run tests:

```powershell
cd build
ctest --output-on-failure
```

### Layer 3 — WinRT projection (see §10)

Not yet buildable from the command line.  Needs Visual Studio + Windows App SDK.

### Layer 4 — C# wrapper

```powershell
cd tests/csharp
dotnet test -r win-arm64 --logger "console;verbosity=normal"
```

The C# wrapper calls `automerge_core.dll` via P/Invoke directly.  The test
project copies the native DLL to the output directory automatically
(via the `<Content>` item in `AutomergeTests.csproj`).

---

## 7. Toolchain decisions and known constraints

### Why `aarch64-pc-windows-gnullvm` instead of MSVC?

This project was initially developed on an **ARM64 Windows device without the
full Visual Studio / Windows SDK toolchain installed**.  The host Rust
toolchain was `stable-aarch64-pc-windows-msvc`, which required `link.exe` from
the MSVC linker to build build-script executables (proc-macro crates like
`serde_derive`, `tracing-attributes`).

We created custom ARM64 MSVC CRT stubs (`C:\Users\arcad\AppData\Local\arm64-msvc-stubs\`)
and a wrapper script `lld-link-arm64.cmd` to replace `link.exe`.  This worked
for linking DLLs but the MSVC target's `rustc.exe` crashed with
`STATUS_ACCESS_VIOLATION` when compiling `automerge 0.5.12` — a complex,
heavily proc-macro-dependent crate.  Investigation showed this was a TLS
initialisation issue specific to the MSVC target's runtime stubs.

The fix was to switch to **`aarch64-pc-windows-gnullvm`**, which:
- Uses LLVM's MinGW-style toolchain (no dependency on `link.exe`)
- Links against Windows UCRT (so the DLL is still a standard Windows DLL)
- Works identically at the C ABI level (the DLL exports are the same)
- Produces `.dll` + `.dll.a` import libraries consumed by clang++ and .NET

`rust-toolchain.toml` in `rust-core/` pins this toolchain so `cargo` picks it
up automatically.

### Why llvm-mingw for C++?

The C++ wrapper and tests are compiled with
`aarch64-w64-mingw32-clang++` from `llvm-mingw`.  This matches the Rust
gnullvm ABI exactly — both use Windows UCRT and the same calling convention.
The CMake toolchain file at `cmake/toolchain-arm64-mingw.cmake` configures this.

On a machine with Visual Studio, you can alternatively use MSVC (`cl.exe`) for
the C++ layer as long as you also rebuild the Rust DLL with an MSVC-compatible
import library.  See §8 for guidance.

### C ABI is the stable boundary

The `.dll` exports in `automerge_core.h` are the **only ABI that crosses
toolchain boundaries**.  C has a fixed, platform-standard ABI on Windows
(x64/ARM64 Microsoft ABI).  The C++ wrapper, WinRT component, and C# layer all
consume the C ABI — they do not link against each other except through the DLL.

### llvm-mingw path assumptions

`build.ps1` and `cmake/toolchain-arm64-mingw.cmake` hard-code the path:

```
C:\llvm-mingw\llvm-mingw-20260407-ucrt-aarch64\
```

If you install a newer version or to a different path, update:
- `cmake/toolchain-arm64-mingw.cmake` — `LLVM_MINGW_ROOT`
- `rust-core/.cargo/config.toml` — `linker` for `aarch64-pc-windows-gnullvm`
- `build.ps1` — `$env:PATH` at the top

---

## 8. Continuing on a full Windows SDK machine

A machine with **Visual Studio 2022** (Desktop C++ workload) and/or
**Windows App SDK** can build everything, including the WinRT component.

### Option A — keep llvm-mingw (simplest)

Install llvm-mingw as described in §4.  Everything builds unchanged.
You only need VS if you want to build the WinRT component (see §10).

### Option B — switch Rust to MSVC target

If you prefer the MSVC Rust target (`aarch64-pc-windows-msvc`):

1. Edit `rust-core/rust-toolchain.toml`:
   ```toml
   [toolchain]
   channel = "stable-aarch64-pc-windows-msvc"
   ```
2. Remove `rust-core/.cargo/config.toml` (or update the linker entry — `link.exe`
   is found automatically when MSVC is in PATH).
3. Rebuild Rust: `cargo build --release`
   - Output will be `target/release/automerge_core.dll` + `target/release/automerge_core.lib`
4. Update `CMakeLists.txt` — change `AUTOMERGE_CORE_IMPLIB` to point to the
   MSVC-style `.lib` instead of `.dll.a`:
   ```cmake
   set(AUTOMERGE_CORE_IMPLIB "${RUST_OUT_DIR}/automerge_core.lib")
   ```
5. For the C++ layer, you can switch to the MSVC toolchain by removing
   `-DCMAKE_TOOLCHAIN_FILE=...` from the cmake invocation (CMake will auto-detect
   the MSVC compiler from the VS Developer Prompt).

### Verifying the switch

```powershell
.\build.ps1
```

All 82+ tests should still pass.

---

## 9. Test strategy

### Rust (24 tests) — `rust-core/tests/c_api_tests.rs`

Tests the raw C ABI functions directly in Rust using the `extern "C"` declarations.
Covers: create/destroy, save/load round-trip, heads, changes (full and incremental),
apply changes, merge, error handling, sync (one-way and bidirectional), value read/write.

### C++ (29 tests) — `tests/cpp/`

GoogleTest suite compiled against `automerge_wrapper` static library.  Tests
the RAII `Document` and `SyncState` classes.  Covers: lifecycle (RAII, move
semantics), persistence, heads, changes, merge, values, sync (one-way,
bidirectional, three-peer).

### C# (29 tests) — `tests/csharp/`

xUnit suite.  Tests the managed `Document` and `SyncState` classes.  Covers
the same semantic operations as C++ tests plus `IDisposable` safety, double-dispose,
multi-instance creation.

---

## 10. WinRT component — what remains

The WinRT component source (`winrt-component/`) is complete but needs a proper
**Windows Runtime Component** build project and the associated tooling.

### What's already done

- `Automerge.Windows.idl` — full IDL with `Document` and `SyncState` runtime classes
- `Document.h` / `Document.cpp` — C++/WinRT implementation wrapping `::automerge::Document`
- `SyncState.h` / `SyncState.cpp` — C++/WinRT implementation wrapping `::automerge::SyncState`
- `Helpers.hpp` — `IBuffer` ↔ `vector<uint8_t>` + `hstring` + error helpers

### What still needs to be done

#### 1. Generate C++/WinRT projection headers

Run `cppwinrt.exe` against the compiled `.winmd` to generate the `winrt/` headers:

```powershell
# After building with midl.exe:
cppwinrt.exe -in Automerge.Windows.winmd -out winrt-component/
```

Or use the `Microsoft.Windows.CsWinRT` NuGet package which runs this automatically.

#### 2. Create a Visual Studio project (`.vcxproj`)

The WinRT component needs to be a **Windows Runtime Component** project type.
Create it in Visual Studio:

- File → New → Project → "Windows Runtime Component (C++/WinRT)"
- Add the existing `.idl`, `.h`, `.cpp` files
- Add a project reference to `cpp-wrapper` (or link the static lib directly)
- Set the Rust DLL as content to copy to the output directory

NuGet packages needed:
```
Microsoft.WindowsAppSDK    >= 1.5
Microsoft.Windows.CsWinRT  >= 2.0   (for CsWinRT projections)
```

#### 3. Fix generated stubs

After `cppwinrt.exe` runs, it generates `Automerge.Windows.g.h` (implementation
stubs).  The existing `Document.h` already uses the `DocumentT<Document>` CRTP
pattern from C++/WinRT.  Minor adjustments may be needed depending on the exact
version of `cppwinrt.exe` used.

#### 4. Threading model decision

The IDL does not yet declare the threading model.  Add one of:

```idl
// In Document runtime class declaration:
[threading(both)]      // MTA / agile (recommended for most scenarios)
// or
[threading(sta)]       // STA only
```

The C++ implementation is **not internally thread-safe** (no locks), so callers
must serialize concurrent access.  `[threading(both)]` with caller-provided
locking is the right default for a low-level binding.

#### 5. Wire up the C# wrapper to use WinRT (optional)

The current C# wrapper calls the C ABI directly via P/Invoke and is fully
functional.  Using the WinRT component from C# is optional — it gives a more
idiomatic WinRT API surface (e.g., `IBuffer` instead of `byte[]`) but adds a
layer.  The WinRT path makes sense primarily for **WinUI 3 / UWP** consumers
who already work with `IBuffer`.

To wire it up, uncomment the `CsWinRT` section in
`csharp-wrapper/AutomergeWindows.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.*" />
</ItemGroup>
<ItemGroup>
  <CsWinRTInputs Include="..\winrt-component\bin\Release\Automerge.Windows.winmd" />
</ItemGroup>
```

---

## Appendix — llvm-mingw installation

```powershell
# Download llvm-mingw ARM64/UCRT build
$url = "https://github.com/mstorsjo/llvm-mingw/releases/download/20260407/llvm-mingw-20260407-ucrt-aarch64.zip"
$zip = "$env:TEMP\llvm-mingw.zip"
Invoke-WebRequest $url -OutFile $zip
Expand-Archive $zip -DestinationPath C:\llvm-mingw -Force
# $PATH entry: C:\llvm-mingw\llvm-mingw-20260407-ucrt-aarch64\bin
```

## Appendix — ninja installation

```powershell
$url = "https://github.com/ninja-build/ninja/releases/download/v1.12.1/ninja-win.zip"
$zip = "$env:TEMP\ninja.zip"
Invoke-WebRequest $url -OutFile $zip
Expand-Archive $zip -DestinationPath "$env:LOCALAPPDATA\ninja" -Force
# $PATH entry: C:\Users\<you>\AppData\Local\ninja
```
