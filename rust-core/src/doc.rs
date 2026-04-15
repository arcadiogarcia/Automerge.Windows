use automerge::{transaction::Transactable, AutoCommit, ChangeHash, ScalarValue, ROOT};
use serde_json::Value as JsonValue;
use std::os::raw::c_char;

use crate::error::set_last_error;

/// Opaque handle wrapping an `AutoCommit` document.
pub struct AMdoc(pub(crate) AutoCommit);

pub const AM_OK: i32 = 0;
pub const AM_ERR: i32 = 1;

macro_rules! check_ptr {
    ($ptr:expr) => {
        if $ptr.is_null() {
            set_last_error("null pointer argument");
            return AM_ERR;
        }
    };
}

// Document lifecycle

/// Create a new empty Automerge document.
/// Returns a heap-allocated handle; caller must free with `AMdestroy_doc`.
#[no_mangle]
pub extern "C" fn AMcreate_doc() -> *mut AMdoc {
    Box::into_raw(Box::new(AMdoc(AutoCommit::new())))
}

/// Destroy a document and free its memory.
/// # Safety
/// `doc` must be a valid non-null pointer returned by `AMcreate_doc` or `AMload`.
#[no_mangle]
pub unsafe extern "C" fn AMdestroy_doc(doc: *mut AMdoc) {
    if !doc.is_null() {
        drop(Box::from_raw(doc));
    }
}

// Persistence

/// Serialize the document to bytes.
/// On success returns 0 and writes `*out_bytes` and `*out_len`.
/// Caller frees with `AMfree_bytes`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMsave(
    doc: *mut AMdoc,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_bytes);
    check_ptr!(out_len);
    let bytes = (*doc).0.save();
    *out_len = bytes.len();
    *out_bytes = alloc_bytes(bytes);
    AM_OK
}

/// Load a document from bytes produced by `AMsave`.
/// On success returns 0 and writes `*out_doc`. Caller frees with `AMdestroy_doc`.
/// # Safety
/// All pointer arguments must satisfy their documented constraints.
#[no_mangle]
pub unsafe extern "C" fn AMload(
    data: *const u8,
    len: usize,
    out_doc: *mut *mut AMdoc,
) -> i32 {
    check_ptr!(out_doc);
    if data.is_null() && len > 0 {
        set_last_error("null data pointer with non-zero length");
        return AM_ERR;
    }
    let bytes: &[u8] = if len == 0 { &[] } else { std::slice::from_raw_parts(data, len) };
    match AutoCommit::load(bytes) {
        Ok(doc) => {
            *out_doc = Box::into_raw(Box::new(AMdoc(doc)));
            AM_OK
        }
        Err(e) => {
            set_last_error(e.to_string());
            AM_ERR
        }
    }
}

// Heads

/// Get the current heads as packed 32-byte hashes. `*out_len` = n_heads * 32.
/// Caller frees with `AMfree_bytes`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget_heads(
    doc: *mut AMdoc,
    out_heads: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_heads);
    check_ptr!(out_len);
    let heads: Vec<ChangeHash> = (*doc).0.get_heads();
    let mut bytes: Vec<u8> = Vec::with_capacity(heads.len() * 32);
    for h in &heads {
        bytes.extend_from_slice(&h.0);
    }
    *out_len = bytes.len();
    *out_heads = alloc_bytes(bytes);
    AM_OK
}

// Changes

/// Get changes produced since `heads` (NULL/0 = all changes from genesis).
/// Output: concatenated raw automerge change bytes.
/// Caller frees with `AMfree_bytes`.
/// # Safety
/// Pointer arguments must satisfy their documented constraints.
#[no_mangle]
pub unsafe extern "C" fn AMget_changes(
    doc: *mut AMdoc,
    heads: *const u8,
    heads_len: usize,
    out_changes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_changes);
    check_ptr!(out_len);
    let parsed_heads = parse_heads(heads, heads_len);
    let changes = (*doc).0.get_changes(&parsed_heads);
    let mut output: Vec<u8> = Vec::new();
    for change in changes {
        output.extend_from_slice(change.raw_bytes());
    }
    *out_len = output.len();
    *out_changes = alloc_bytes(output);
    AM_OK
}

/// Apply a binary changes buffer to the document.
/// `changes` is the concatenated raw bytes produced by `AMget_changes`.
/// Returns 0 on success.
/// # Safety
/// All pointer arguments must be valid.
#[no_mangle]
pub unsafe extern "C" fn AMapply_changes(
    doc: *mut AMdoc,
    changes: *const u8,
    len: usize,
) -> i32 {
    check_ptr!(doc);
    if changes.is_null() && len > 0 {
        set_last_error("null changes pointer with non-zero length");
        return AM_ERR;
    }
    let bytes: &[u8] = if len == 0 { &[] } else { std::slice::from_raw_parts(changes, len) };
    match (*doc).0.load_incremental(bytes) {
        Ok(_) => AM_OK,
        Err(e) => {
            set_last_error(e.to_string());
            AM_ERR
        }
    }
}

// Merge

/// Merge all changes from `src` into `dest`. Returns 0 on success.
/// # Safety
/// Both pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMmerge(dest: *mut AMdoc, src: *mut AMdoc) -> i32 {
    check_ptr!(dest);
    check_ptr!(src);
    match (*dest).0.merge(&mut (*src).0) {
        Ok(_) => AM_OK,
        Err(e) => {
            set_last_error(e.to_string());
            AM_ERR
        }
    }
}

// Read

/// Get a value from the document by JSON path.
/// `path_json` is a UTF-8 JSON array e.g. `["contacts",0,"name"]`.
/// Pass NULL or `"[]"` to serialise the entire root object.
/// On success: `*out_json` = NUL-terminated JSON, `*out_len` = byte count excl NUL.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// `doc`, `out_json`, and `out_len` must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget_value(
    doc: *const AMdoc,
    path_json: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(out_json);
    check_ptr!(out_len);
    if doc.is_null() {
        set_last_error("null doc pointer");
        return AM_ERR;
    }
    let inner = &(*doc).0;
    let full_json = match serde_json::to_value(automerge::AutoSerde::from(inner)) {
        Ok(v) => v,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let result = if path_json.is_null() {
        full_json
    } else {
        let path_str = match std::ffi::CStr::from_ptr(path_json).to_str() {
            Ok(s) => s,
            Err(_) => { set_last_error("invalid UTF-8 in path_json"); return AM_ERR; }
        };
        match navigate_path(&full_json, path_str) {
            Ok(v) => v,
            Err(e) => { set_last_error(e); return AM_ERR; }
        }
    };
    let mut json_bytes = match serde_json::to_vec(&result) {
        Ok(b) => b,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    *out_len = json_bytes.len();
    json_bytes.push(0); // NUL-terminate
    json_bytes.shrink_to_fit();
    let ptr = json_bytes.as_mut_ptr();
    std::mem::forget(json_bytes);
    *out_json = ptr;
    AM_OK
}

// Write

/// Write scalar key-value pairs from a JSON object into the document root.
/// `json_obj` must be a UTF-8 JSON object e.g. `{"name":"Alice","age":30}`.
/// Returns 0 on success.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMput_json_root(doc: *mut AMdoc, json_obj: *const c_char) -> i32 {
    check_ptr!(doc);
    if json_obj.is_null() {
        set_last_error("null json_obj pointer");
        return AM_ERR;
    }
    let json_str = match std::ffi::CStr::from_ptr(json_obj).to_str() {
        Ok(s) => s,
        Err(_) => { set_last_error("invalid UTF-8 in json_obj"); return AM_ERR; }
    };
    let obj: serde_json::Map<String, JsonValue> = match serde_json::from_str(json_str) {
        Ok(JsonValue::Object(m)) => m,
        Ok(_) => { set_last_error("json_obj must be a JSON object"); return AM_ERR; }
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let doc_ref = &mut (*doc).0;
    for (key, val) in &obj {
        match json_val_to_scalar(val) {
            Ok(sv) => {
                if let Err(e) = doc_ref.put(ROOT, key.as_str(), sv) {
                    set_last_error(e.to_string());
                    return AM_ERR;
                }
            }
            Err(e) => { set_last_error(e); return AM_ERR; }
        }
    }
    doc_ref.commit();
    AM_OK
}

// Memory management

/// Free a byte buffer previously returned by a `AM*` function.
/// # Safety
/// `data` must be a valid pointer; `len` must match `*out_len`.
#[no_mangle]
pub unsafe extern "C" fn AMfree_bytes(data: *mut u8, len: usize) {
    if !data.is_null() && len > 0 {
        drop(Vec::from_raw_parts(data, len, len));
    }
}

// Helpers

/// Move a `Vec<u8>` to the heap; return raw pointer.
/// Capacity is shrunk to `len` so `AMfree_bytes(ptr, len)` is safe.
pub(crate) fn alloc_bytes(mut v: Vec<u8>) -> *mut u8 {
    if v.is_empty() {
        return std::ptr::NonNull::dangling().as_ptr();
    }
    v.shrink_to_fit();
    let ptr = v.as_mut_ptr();
    std::mem::forget(v);
    ptr
}

/// Parse a packed 32-byte-per-hash heads buffer.
pub(crate) unsafe fn parse_heads(heads: *const u8, heads_len: usize) -> Vec<ChangeHash> {
    if heads.is_null() || heads_len < 32 {
        return Vec::new();
    }
    let bytes = std::slice::from_raw_parts(heads, heads_len);
    bytes
        .chunks_exact(32)
        .filter_map(|chunk| {
            let arr: [u8; 32] = chunk.try_into().ok()?;
            Some(ChangeHash(arr))
        })
        .collect()
}

/// Navigate a `serde_json::Value` by a JSON-array path string.
fn navigate_path(root: &JsonValue, path_str: &str) -> Result<JsonValue, String> {
    let path_str = path_str.trim();
    if path_str.is_empty() || path_str == "[]" {
        return Ok(root.clone());
    }
    let segments: Vec<JsonValue> =
        serde_json::from_str(path_str).map_err(|e| format!("invalid path JSON: {}", e))?;
    let mut cur = root;
    for seg in &segments {
        cur = if let Some(key) = seg.as_str() {
            cur.get(key).ok_or_else(|| format!("key '{}' not found", key))?
        } else if let Some(idx) = seg.as_u64() {
            cur.get(idx as usize).ok_or_else(|| format!("index {} out of range", idx))?
        } else {
            return Err(format!("invalid path segment: {}", seg));
        };
    }
    Ok(cur.clone())
}

/// Convert a `serde_json::Value` (scalar only) to `automerge::ScalarValue`.
fn json_val_to_scalar(v: &JsonValue) -> Result<ScalarValue, String> {
    match v {
        JsonValue::Null => Ok(ScalarValue::Null),
        JsonValue::Bool(b) => Ok(ScalarValue::Boolean(*b)),
        JsonValue::Number(n) => {
            if let Some(i) = n.as_i64() { Ok(ScalarValue::Int(i)) }
            else if let Some(u) = n.as_u64() { Ok(ScalarValue::Uint(u)) }
            else if let Some(f) = n.as_f64() { Ok(ScalarValue::F64(f)) }
            else { Err(format!("unsupported number: {}", n)) }
        }
        JsonValue::String(s) => Ok(ScalarValue::Str(s.as_str().into())),
        _ => Err("nested objects/arrays require AMput_object".to_string()),
    }
}
