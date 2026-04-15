# Automerge.Windows

Native Windows bindings for the [Automerge](https://github.com/automerge/automerge)
CRDT library.  Zero WebView, zero JavaScript runtime — pure native ARM64 Windows.

## Architecture

```
C# Convenience Layer          (AutomergeWindows.dll)
         │
WinRT Projection  [WIP]       (Automerge.Windows.winmd / .dll)
         │
C++ RAII Wrapper              (automerge_wrapper.a / static lib)
         │
C ABI — Rust core             (automerge_core.dll / ARM64 Windows)
```

| Layer | Status | Tests |
|---|---|---|
| Rust C ABI | ✅ Complete | 24 |
| C++ RAII wrapper | ✅ Complete | 29 |
| WinRT projection | ⚠️ Source written, not yet compiled | — |
| C# P/Invoke wrapper | ✅ Complete | 29 |

## Quick start

```powershell
# Prerequisites: rustup, LLVM, llvm-mingw, CMake, Ninja, .NET 9 SDK
# See BUILDING.md for installation instructions.

git clone https://github.com/arcadiogarcia/Automerge.Windows.git
cd Automerge.Windows

rustup toolchain install stable-aarch64-pc-windows-gnullvm

.\build.ps1
```

## Usage (C#)

```csharp
using Automerge.Windows;

// Create two peers
using var doc1 = new Document();
using var doc2 = new Document();

// Write some data
doc1.PutJsonRoot("""{"name":"Alice","score":42}""");

// Exchange changes
var changes = doc1.GetChanges();
doc2.ApplyChanges(changes);

// Both docs now have the same value
var json = doc2.GetValue();  // {"name":"Alice","score":42}

// Sync protocol
using var state1 = new SyncState();
using var state2 = new SyncState();

while (true) {
    var msg = state1.GenerateSyncMessage(doc1);
    if (msg.Length == 0) break;
    state2.ReceiveSyncMessage(doc2, msg);
    var reply = state2.GenerateSyncMessage(doc2);
    if (reply.Length > 0) state1.ReceiveSyncMessage(doc1, reply);
}
```

## Usage (C++)

```cpp
#include "automerge.hpp"

automerge::Document doc1;
doc1.put_json_root(R"({"name":"Alice","score":42})");

auto changes = doc1.get_changes();

automerge::Document doc2;
doc2.apply_changes(changes);

std::string json = doc2.get_value();  // {"name":"Alice","score":42}
```

## Documentation

See [BUILDING.md](BUILDING.md) for:
- Complete build instructions and prerequisites
- Toolchain decisions and known constraints
- How to continue building the WinRT component on a full Windows SDK machine
- How to switch from the llvm-mingw toolchain to MSVC

## License

MIT
