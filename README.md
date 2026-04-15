# Automerge.Windows

Native Windows bindings for the [Automerge](https://github.com/automerge/automerge)
CRDT library. Zero WebView, zero JavaScript runtime — pure native x64 Windows.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  WinRT Projection  (Automerge.Windows.winmd / .dll)         │ ← WinUI 3 / Windows App SDK
│  C# P/Invoke Wrapper  (AutomergeWindows.dll)                │ ← .NET / console / server
└──────────────────────────────┬──────────────────────────────┘
                               │ static link
┌──────────────────────────────▼──────────────────────────────┐
│  C++ RAII Wrapper  (automerge_wrapper.lib)                   │ ← Native C++ apps
└──────────────────────────────┬──────────────────────────────┘
                               │ import lib
┌──────────────────────────────▼──────────────────────────────┐
│  C ABI — Rust core  (automerge_core.dll)                     │ ← Any FFI consumer
└─────────────────────────────────────────────────────────────┘
```

| Layer | Output | Tests | Use when |
|---|---|---|---|
| Rust C ABI | `automerge_core.dll` | 33 ✅ | FFI from any language |
| C++ RAII wrapper | `automerge_wrapper.lib` | 39 ✅ | Native C++17/20 apps |
| C# P/Invoke wrapper | `AutomergeWindows.dll` | 38 ✅ | .NET 9+ apps and services |
| WinRT projection | `Automerge.Windows.winmd` + `.dll` | 18 ✅ | WinUI 3 / Windows App SDK |

---

## Quick start

```powershell
git clone https://github.com/arcadiogarcia/Automerge.Windows.git
cd Automerge.Windows
.\build.ps1          # builds all layers and runs all 128 tests
```

See [BUILDING.md](BUILDING.md) for prerequisites and toolchain details.

---

## Path syntax

All layers share the same JSON-array path syntax for `GetValue` / `get_value` / `AMget_value`:

| Path string | Meaning |
|---|---|
| `[]` or `""` | Entire root object as JSON |
| `["name"]` | Top-level key `name` |
| `["contacts",0,"email"]` | `contacts[0].email` |

Values are returned as JSON strings: strings are quoted (`"Alice"`), numbers are unquoted (`42`), booleans are `true`/`false`, null is `null`.

---

## Usage — C# P/Invoke wrapper

Target: **.NET 9+** console, ASP.NET, or desktop apps that don't require WinRT activation.

**NuGet / project reference:** Add `csharp-wrapper/AutomergeWindows.csproj` to your solution, or copy the built `AutomergeWindows.dll` and `automerge_core.dll` to your output directory.

### Basic read / write

```csharp
using Automerge.Windows;

using var doc = new Document();

// Write a flat JSON object into the root.
// Only scalar values (string, number, bool, null) are supported.
doc.PutJsonRoot("""{"name":"Alice","score":42,"active":true}""");

// Read individual values by path
string name   = doc.GetValue("""["name"]""");   // → "\"Alice\""
string score  = doc.GetValue("""["score"]""");  // → "42"
string root   = doc.GetValue("[]");             // → {"name":"Alice","score":42,"active":true}

// Extension method: deserialise directly to a .NET type
int s = doc.GetValue<int>("""["score"]""");     // → 42
```

### Persistence

```csharp
// Save to bytes (e.g. write to a file or database)
byte[] bytes = doc.Save();

// Restore
using var doc2 = Document.Load(bytes);
```

### Changes-based replication

Efficient for one-way push where one side always sends and the other applies:

```csharp
using var sender   = new Document();
using var receiver = new Document();

sender.PutJsonRoot("""{"event":"hello"}""");

// All changes from genesis
byte[] allChanges = sender.GetChanges();
receiver.ApplyChanges(allChanges);

// Only changes since a known checkpoint
byte[] checkpoint = sender.GetHeads();
sender.PutJsonRoot("""{"event":"world"}""");
byte[] delta = sender.GetChanges(checkpoint);   // just the new change
receiver.ApplyChanges(delta);
```

### Merge

Best for local in-process merging of two independently-modified documents:

```csharp
// Both peers start from the same saved state
byte[] origin = new Document().Also(d => d.PutJsonRoot("""{"shared":1}""")).Save();

using var peer1 = Document.Load(origin);
using var peer2 = Document.Load(origin);

peer1.PutJsonRoot("""{"from_peer1":"hello"}""");
peer2.PutJsonRoot("""{"from_peer2":"world"}""");

peer1.Merge(peer2);  // peer1 now has all three keys
```

### Sync protocol (bidirectional, transport-agnostic)

The sync protocol is incremental — peers only exchange what the other doesn't have yet. Use one `SyncState` per remote peer per connection session.

```csharp
using var docLocal  = new Document();
using var docRemote = new Document();   // in practice, lives on another machine

docLocal.PutJsonRoot("""{"device":"phone","ts":1}""");

using var stateLocal  = new SyncState();
using var stateRemote = new SyncState();

// Exchange messages until both sides are done
while (true)
{
    byte[] msg = stateLocal.GenerateSyncMessage(docLocal);
    if (msg.Length == 0) break;                             // local is done
    stateRemote.ReceiveSyncMessage(docRemote, msg);

    byte[] reply = stateRemote.GenerateSyncMessage(docRemote);
    if (reply.Length > 0)
        stateLocal.ReceiveSyncMessage(docLocal, reply);
}

// docRemote now matches docLocal
```

**Persisting sync state** (for reconnecting peers):

```csharp
// Before disconnect — save the SyncState so the next session is incremental
byte[] savedState = stateLocal.Save();

// On reconnect — restore and continue; only new changes are exchanged
using var resumed = SyncState.Load(savedState);
```

**Convenience helper** (for in-process / test use):

```csharp
// Syncs docA and docB in-memory until both converge
AutomergeExtensions.SyncInMemory(docA, docB);
```

### Error handling

All failures throw `AutomergeNativeException`. Common causes:
- `Document.Load` / `SyncState.Load` with invalid bytes
- `ApplyChanges` with corrupted change data
- `GetValue` with a path that doesn't exist in the document

---

## Usage — WinRT projection

Target: **WinUI 3 / Windows App SDK** apps, or any language with WinRT projection support (C#, C++/WinRT, Rust/WinRT).

**Reference:** Add `Automerge.Windows.winmd` and `Automerge.Windows.dll` to your project. For C#, also add the `Microsoft.Windows.CsWinRT` NuGet package and set:

```xml
<CsWinRTInputs Include="path\to\Automerge.Windows.winmd" />
<CsWinRTIncludes>Automerge.Windows</CsWinRTIncludes>
```

Binary data (`Ibuffer`) is exchanged via `Windows.Storage.Streams.IBuffer`. Use `DataWriter` / `DataReader` to convert to/from `byte[]`.

### Basic read / write

```csharp
using Automerge.Windows;

var doc = new Document();
doc.PutJsonRoot("""{"name":"Alice","score":42}""");

string name  = doc.GetValue("""["name"]""");    // → "\"Alice\""
string score = doc.GetValue("""["score"]""");   // → "42"
```

### Persistence

```csharp
// Save returns an IBuffer
Windows.Storage.Streams.IBuffer buf = doc.Save();

// Restore
var doc2 = Document.Load(buf);
```

### Changes and merge

```csharp
var origin = new Document();
origin.PutJsonRoot("""{"shared":1}""");
var snap = origin.Save();

var peer1 = Document.Load(snap);
var peer2 = Document.Load(snap);
peer1.PutJsonRoot("""{"p1":"hello"}""");
peer2.PutJsonRoot("""{"p2":"world"}""");

peer1.Merge(peer2);
```

### Sync protocol

```csharp
var stateA = new SyncState();
var stateB = new SyncState();   // one per remote peer

for (int i = 0; i < 20; i++)
{
    var msgAB = stateA.GenerateSyncMessage(docA);
    if (msgAB.Length > 0) stateB.ReceiveSyncMessage(docB, msgAB);

    var msgBA = stateB.GenerateSyncMessage(docB);
    if (msgBA.Length > 0) stateA.ReceiveSyncMessage(docA, msgBA);

    // Stop when neither side has anything new to say
    if (msgAB.Length == 0 && msgBA.Length == 0) break;
}
```

**Persisting sync state:**

```csharp
IBuffer saved = stateA.Save();
var resumed   = SyncState.Load(saved);
```

### Error handling

Errors from the native layer are surfaced as `COMException` (HRESULT `E_FAIL`). The error message is propagated from the Rust layer.

---

## Usage — C++ RAII wrapper

Target: Native C++17/20 applications. Link `automerge_wrapper.lib` and put `automerge_core.dll` next to the executable.

```cpp
#include <automerge/Document.hpp>
#include <automerge/SyncState.hpp>
#include <automerge/Error.hpp>

using namespace automerge;
```

### Basic read / write

```cpp
Document doc;
doc.put_json_root(R"({"name":"Alice","score":42})");

std::string name  = doc.get_value(R"(["name"])");   // → "\"Alice\""
std::string score = doc.get_value(R"(["score"])");  // → "42"
std::string root  = doc.get_value("[]");            // → {"name":"Alice", ...}
```

### Persistence

```cpp
std::vector<uint8_t> bytes = doc.save();
Document doc2 = Document::load(bytes);
```

### Changes-based replication

```cpp
Document sender, receiver;
sender.put_json_root(R"({"msg":"hello"})");

// All changes
std::vector<uint8_t> all = sender.get_changes();
receiver.apply_changes(all);

// Incremental — only changes since a checkpoint
auto heads = sender.get_heads();
sender.put_json_root(R"({"msg":"world"})");
auto delta = sender.get_changes(heads);
receiver.apply_changes(delta);
```

### Merge

```cpp
Document peer1, peer2;
peer1.put_json_root(R"({"a":"from_peer1"})");
peer2.put_json_root(R"({"b":"from_peer2"})");
peer1.merge(peer2);   // peer1 now has both keys
```

### Sync protocol

```cpp
SyncState ss_a, ss_b;   // one SyncState per peer per connection

for (int i = 0; i < 20; ++i) {
    auto msg_ab = ss_a.generate_sync_message(doc_a);
    if (!msg_ab.empty()) ss_b.receive_sync_message(doc_b, msg_ab);

    auto msg_ba = ss_b.generate_sync_message(doc_b);
    if (!msg_ba.empty()) ss_a.receive_sync_message(doc_a, msg_ba);

    if (msg_ab.empty() && msg_ba.empty()) break;   // converged
}
```

**Persisting sync state:**

```cpp
auto saved   = ss_a.save();
auto resumed = SyncState::load(saved);
```

### Error handling

All errors throw `automerge::AutomergeError` (derived from `std::runtime_error`). Move semantics are supported; copying is deleted.

---

## Usage — C ABI

Target: Any language with C FFI (Rust, Python, Go, Zig, …). Include `rust-core/include/automerge_core.h` and link `automerge_core.dll` / `automerge_core.dll.lib`.

### Ownership rules

- Functions that write `*out_bytes` / `*out_doc` / `*out_state` transfer ownership to the caller.
- Free byte buffers with `AMfree_bytes(ptr, len)`.
- **Exception:** NUL-terminated strings returned by `AMget_value` must be freed with `AMfree_bytes(ptr, len + 1)` (one extra byte for the NUL).
- Free documents with `AMdestroy_doc`.
- Free sync states with `AMfree_sync_state`.
- Check return value: `AM_OK` (0) = success, `AM_ERR` (1) = failure.
- On failure, call `AMget_last_error` to retrieve the error message.

### Basic example

```c
#include "automerge_core.h"
#include <stdio.h>

AMdoc* doc = AMcreate_doc();

// Write
AMput_json_root(doc, "{\"name\":\"Alice\",\"score\":42}");

// Read
uint8_t* json = NULL;
size_t   len  = 0;
if (AMget_value(doc, "[\"name\"]", &json, &len) == AM_OK) {
    printf("name = %.*s\n", (int)len, json);  // → name = "Alice"
    AMfree_bytes(json, len + 1);
}

// Save / load
uint8_t* bytes = NULL;
size_t   bytes_len = 0;
AMsave(doc, &bytes, &bytes_len);

AMdoc* doc2 = NULL;
AMload(bytes, bytes_len, &doc2);
AMfree_bytes(bytes, bytes_len);

AMdestroy_doc(doc);
AMdestroy_doc(doc2);
```

### Sync protocol

```c
AMsync_state* ss_a = AMcreate_sync_state();
AMsync_state* ss_b = AMcreate_sync_state();

for (int i = 0; i < 20; ++i) {
    uint8_t* msg = NULL;  size_t msg_len = 0;
    AMgenerate_sync_message(doc_a, ss_a, &msg, &msg_len);
    if (msg && msg_len > 0) {
        AMreceive_sync_message(doc_b, ss_b, msg, msg_len);
        AMfree_bytes(msg, msg_len);
    }

    uint8_t* reply = NULL;  size_t reply_len = 0;
    AMgenerate_sync_message(doc_b, ss_b, &reply, &reply_len);
    if (reply && reply_len > 0) {
        AMreceive_sync_message(doc_a, ss_a, reply, reply_len);
        AMfree_bytes(reply, reply_len);
    }
}

AMfree_sync_state(ss_a);
AMfree_sync_state(ss_b);
```

### Current limitations

`AMput_json_root` accepts only **scalar** values (string, number, bool, null). Nested objects and arrays require the full Automerge object-graph API (`AMput_object`) which is not yet exposed in this C ABI. Setting a nested value returns `AM_ERR`.

---

## License

MIT
