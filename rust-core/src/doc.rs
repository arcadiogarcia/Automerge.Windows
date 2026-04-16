use automerge::{
    marks::ExpandMark,
    transaction::{CommitOptions, Transactable},
    ActorId, AutoCommit, ChangeHash, Cursor, ObjId, ObjType, Patch, PatchAction, Prop, ReadDoc,
    ScalarValue, ROOT,
};
use serde_json::{json, Value as JsonValue};
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

// ─── Actor ───────────────────────────────────────────────────────────────────

/// Get the actor ID of this document as raw bytes.
/// Caller frees with `AMfree_bytes`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget_actor(
    doc: *const AMdoc,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_bytes);
    check_ptr!(out_len);
    let actor = (*doc).0.get_actor();
    let bytes: Vec<u8> = actor.to_bytes().to_vec();
    *out_len = bytes.len();
    *out_bytes = alloc_bytes(bytes);
    AM_OK
}

/// Set the actor ID for this document from raw bytes.
/// # Safety
/// All pointer arguments must be valid.
#[no_mangle]
pub unsafe extern "C" fn AMset_actor(
    doc: *mut AMdoc,
    bytes: *const u8,
    len: usize,
) -> i32 {
    check_ptr!(doc);
    if bytes.is_null() && len > 0 {
        set_last_error("null bytes pointer with non-zero length");
        return AM_ERR;
    }
    let slice: &[u8] = if len == 0 { &[] } else { std::slice::from_raw_parts(bytes, len) };
    (*doc).0.set_actor(ActorId::from(slice.to_vec()));
    AM_OK
}

// ─── Fine-grained read ───────────────────────────────────────────────────────

/// Get a single value from a map object by key.
/// `obj_id` is NULL/"" for ROOT, or a string like "5@hexactor" returned by AMput_object.
/// Returns NUL-terminated JSON. Scalars → raw JSON value. Nested objects →
/// `{"_obj_id":"...","_obj_type":"map|list|text"}`.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget(
    doc: *const AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.get(&obj, key_str) {
        Ok(Some((val, vid))) => write_json_out(value_to_json(&val, &vid), out_json, out_len),
        Ok(None) => { set_last_error(format!("key '{}' not found", key_str)); AM_ERR }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get a single value from a list/text object by index.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget_idx(
    doc: *const AMdoc,
    obj_id: *const c_char,
    index: usize,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.get(&obj, index) {
        Ok(Some((val, vid))) => write_json_out(value_to_json(&val, &vid), out_json, out_len),
        Ok(None) => { set_last_error(format!("index {} out of range", index)); AM_ERR }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get the keys of a map object as a JSON array of strings.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMkeys(
    doc: *const AMdoc,
    obj_id: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let keys: Vec<String> = (*doc).0.keys(&obj).map(|k| k.to_string()).collect();
    let json_val = JsonValue::Array(keys.into_iter().map(|k| JsonValue::String(k)).collect());
    write_json_out(json_val, out_json, out_len)
}

/// Get the length (number of elements/keys) of an object.
/// Works for maps, lists, and text objects.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMlength(
    doc: *const AMdoc,
    obj_id: *const c_char,
    out_n: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_n);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    *out_n = (*doc).0.length(&obj);
    AM_OK
}

/// Get the text content of a text object as a UTF-8 string.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget_text(
    doc: *const AMdoc,
    obj_id: *const c_char,
    out_text: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_text);
    check_ptr!(out_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.text(&obj) {
        Ok(text) => {
            let mut bytes = text.into_bytes();
            *out_len = bytes.len();
            bytes.push(0);
            bytes.shrink_to_fit();
            *out_text = bytes.as_mut_ptr();
            std::mem::forget(bytes);
            AM_OK
        }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

// ─── Fine-grained write ──────────────────────────────────────────────────────

/// Set a scalar value in a map object by key.
/// `scalar_json` must be a JSON scalar: `"hello"`, `42`, `true`, `null`.
/// Returns 0 on success.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMput(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
    scalar_json: *const c_char,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let json_str = match c_str_to_str(scalar_json) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let json_val: JsonValue = match serde_json::from_str(json_str) {
        Ok(v) => v,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let sv = match json_val_to_scalar(&json_val) {
        Ok(v) => v,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    if let Err(e) = (*doc).0.put(&obj, key_str, sv) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

/// Set a scalar value in a list object by index (overwrites existing item).
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMput_idx(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    index: usize,
    scalar_json: *const c_char,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let json_str = match c_str_to_str(scalar_json) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let json_val: JsonValue = match serde_json::from_str(json_str) {
        Ok(v) => v,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let sv = match json_val_to_scalar(&json_val) {
        Ok(v) => v,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    if let Err(e) = (*doc).0.put(&obj, index, sv) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

/// Create a nested object (map, list, or text) at a map key.
/// On success, `*out_new_obj_id` is a NUL-terminated string representing the new
/// object's ID. Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMput_object(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
    obj_type: *const c_char,
    out_new_obj_id: *mut *mut u8,
    out_new_obj_id_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_new_obj_id);
    check_ptr!(out_new_obj_id_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let ot = match parse_obj_type(obj_type) {
        Ok(t) => t,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let new_id = match (*doc).0.put_object(&obj, key_str, ot) {
        Ok(id) => id,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    (*doc).0.commit();
    write_cstring_out(exid_to_string(&new_id), out_new_obj_id, out_new_obj_id_len)
}

/// Delete a key from a map object.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMdelete(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    if let Err(e) = (*doc).0.delete(&obj, key_str) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

// ─── List operations ─────────────────────────────────────────────────────────

/// Insert a scalar value into a list object at `index`.
/// Use `index = usize::MAX` to append to the end.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMinsert(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    index: usize,
    scalar_json: *const c_char,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let json_str = match c_str_to_str(scalar_json) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let json_val: JsonValue = match serde_json::from_str(json_str) {
        Ok(v) => v,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let sv = match json_val_to_scalar(&json_val) {
        Ok(v) => v,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let actual_index = if index == usize::MAX { (*doc).0.length(&obj) } else { index };
    if let Err(e) = (*doc).0.insert(&obj, actual_index, sv) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

/// Insert a nested object (map/list/text) into a list at `index`.
/// On success, returns the new object's ID as a NUL-terminated string.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMinsert_object(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    index: usize,
    obj_type: *const c_char,
    out_new_obj_id: *mut *mut u8,
    out_new_obj_id_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_new_obj_id);
    check_ptr!(out_new_obj_id_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let ot = match parse_obj_type(obj_type) {
        Ok(t) => t,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let actual_index = if index == usize::MAX { (*doc).0.length(&obj) } else { index };
    let new_id = match (*doc).0.insert_object(&obj, actual_index, ot) {
        Ok(id) => id,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    (*doc).0.commit();
    write_cstring_out(exid_to_string(&new_id), out_new_obj_id, out_new_obj_id_len)
}

/// Delete the element at `index` from a list object.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMdelete_at(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    index: usize,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    if let Err(e) = (*doc).0.delete(&obj, index) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

// ─── Counter ─────────────────────────────────────────────────────────────────

/// Create a counter scalar at `key` in a map object with the given initial value.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMput_counter(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
    initial: i64,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    if let Err(e) = (*doc).0.put(&obj, key_str, ScalarValue::counter(initial)) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

/// Increment (or decrement) a counter at `key` in a map object by `delta`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMincrement(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
    delta: i64,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    if let Err(e) = (*doc).0.increment(&obj, key_str, delta) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

// ─── Text ────────────────────────────────────────────────────────────────────

/// Insert/delete characters in a text object.
/// `start` is the character index; `delete_count` is the number of UTF-16 code units
/// to delete (negative is not valid; use 0 to insert only); `text` is UTF-8 to insert.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMsplice_text(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    start: usize,
    delete_count: isize,
    text: *const c_char,
) -> i32 {
    check_ptr!(doc);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let insert_text = if text.is_null() {
        ""
    } else {
        match c_str_to_str(text) {
            Ok(s) => s,
            Err(e) => { set_last_error(e); return AM_ERR; }
        }
    };
    if let Err(e) = (*doc).0.splice_text(&obj, start, delete_count, insert_text) {
        set_last_error(e.to_string());
        return AM_ERR;
    }
    (*doc).0.commit();
    AM_OK
}

// ─── Fork and incremental save ───────────────────────────────────────────────

/// Fork (clone) the document, producing an independent copy with the same state.
/// Caller frees the result with `AMdestroy_doc`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMfork(
    doc: *mut AMdoc,
    out_doc: *mut *mut AMdoc,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_doc);
    let forked = (*doc).0.fork();
    *out_doc = Box::into_raw(Box::new(AMdoc(forked)));
    AM_OK
}

/// Save only the changes that have not yet been saved (incremental save).
/// Much faster than `AMsave` when most of the document is unchanged.
/// Caller frees with `AMfree_bytes`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMsave_incremental(
    doc: *mut AMdoc,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_bytes);
    check_ptr!(out_len);
    let bytes = (*doc).0.save_incremental();
    *out_len = bytes.len();
    *out_bytes = alloc_bytes(bytes);
    AM_OK
}

// ─── Commit with metadata ────────────────────────────────────────────────────

/// Commit pending changes with an optional message and/or timestamp.
/// `message` may be NULL (no message). `timestamp` is Unix seconds; 0 = no timestamp.
/// # Safety
/// All pointer arguments must be valid.
#[no_mangle]
pub unsafe extern "C" fn AMcommit(
    doc: *mut AMdoc,
    message: *const c_char,
    timestamp: i64,
) -> i32 {
    check_ptr!(doc);
    let mut options = CommitOptions::default();
    if !message.is_null() {
        match c_str_to_str(message) {
            Ok(s) if !s.is_empty() => { options = options.with_message(s.to_owned()); }
            Ok(_) => {}
            Err(e) => { set_last_error(e); return AM_ERR; }
        }
    }
    if timestamp != 0 {
        options = options.with_time(timestamp);
    }
    (*doc).0.commit_with(options);
    AM_OK
}

// ─── Conflict detection ──────────────────────────────────────────────────────

/// Get all concurrent values for a key in a map object (for conflict resolution).
/// Returns a JSON array. Each element is the same format as `AMget`.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMget_all(
    doc: *const AMdoc,
    obj_id: *const c_char,
    key: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let obj = match read_obj_id(obj_id) {
        Ok(o) => o,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let key_str = match c_str_to_str(key) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let all: Vec<JsonValue> = match (*doc).0.get_all(&obj, key_str) {
        Ok(iter) => iter.into_iter().map(|(val, id)| value_to_json(&val, &id)).collect(),
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    write_json_out(JsonValue::Array(all), out_json, out_len)
}

// ─── Diff / patches ──────────────────────────────────────────────────────────

/// Get patches describing all changes since the last call to `AMdiff_incremental`.
/// Returns a JSON array of patch objects. See documentation for the patch format.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMdiff_incremental(
    doc: *mut AMdoc,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let patches = (*doc).0.diff_incremental();
    let patch_jsons: Vec<JsonValue> = patches.iter().map(patch_to_json).collect();
    write_json_out(JsonValue::Array(patch_jsons), out_json, out_len)
}

// ─── New APIs: close gap with JS @automerge/automerge ─────────────────────── NEW

/// Get ALL changes in the document (equivalent to getChanges with empty heads, or JS getAllChanges).
/// Output: concatenated raw change bytes. Caller frees with `AMfree_bytes`.
#[no_mangle]
pub unsafe extern "C" fn AMget_all_changes(
    doc: *mut AMdoc,
    out_changes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_changes);
    check_ptr!(out_len);
    let changes = (*doc).0.get_changes(&[]);
    let mut output: Vec<u8> = Vec::new();
    for change in changes {
        output.extend_from_slice(change.raw_bytes());
    }
    *out_len = output.len();
    *out_changes = alloc_bytes(output);
    AM_OK
}

/// Get the binary representation of the last locally-made change.
/// Returns empty (len=0) if there is no local change. Caller frees with `AMfree_bytes`.
#[no_mangle]
pub unsafe extern "C" fn AMget_last_local_change(
    doc: *mut AMdoc,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_bytes);
    check_ptr!(out_len);
    match (*doc).0.get_last_local_change() {
        Some(change) => {
            let raw = change.raw_bytes().to_vec();
            *out_len = raw.len();
            *out_bytes = alloc_bytes(raw);
        }
        None => {
            *out_len = 0;
            *out_bytes = std::ptr::NonNull::dangling().as_ptr();
        }
    }
    AM_OK
}

/// Get changes that are missing to reach `heads`.
/// Returns packed 32-byte hashes. Caller frees with `AMfree_bytes`.
#[no_mangle]
pub unsafe extern "C" fn AMget_missing_deps(
    doc: *mut AMdoc,
    heads: *const u8,
    heads_len: usize,
    out_heads: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_heads);
    check_ptr!(out_len);
    let parsed = parse_heads(heads, heads_len);
    let missing = (*doc).0.get_missing_deps(&parsed);
    let mut bytes: Vec<u8> = Vec::with_capacity(missing.len() * 32);
    for h in &missing {
        bytes.extend_from_slice(&h.0);
    }
    *out_len = bytes.len();
    *out_heads = alloc_bytes(bytes);
    AM_OK
}

/// Create an empty change (no ops) with optional commit options.
/// Equivalent to JS `emptyChange()`.
#[no_mangle]
pub unsafe extern "C" fn AMempty_change(
    doc: *mut AMdoc,
    message: *const c_char,
    timestamp: i64,
) -> i32 {
    check_ptr!(doc);
    let mut opts = CommitOptions::default();
    if !message.is_null() {
        match c_str_to_str(message) {
            Ok(s) if !s.is_empty() => { opts.set_message(s.to_string()); }
            _ => {}
        }
    }
    if timestamp != 0 {
        opts.set_time(timestamp);
    }
    (*doc).0.empty_change(opts);
    AM_OK
}

/// Save only changes since `heads`. Equivalent to JS `saveSince(heads)`.
/// Caller frees with `AMfree_bytes`.
#[no_mangle]
pub unsafe extern "C" fn AMsave_after(
    doc: *mut AMdoc,
    heads: *const u8,
    heads_len: usize,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_bytes);
    check_ptr!(out_len);
    let parsed = parse_heads(heads, heads_len);
    let bytes = (*doc).0.save_after(&parsed);
    *out_len = bytes.len();
    *out_bytes = alloc_bytes(bytes);
    AM_OK
}

/// Load incremental changes (the result of `save_incremental`, `save_after`, etc.)
/// into an existing document. Equivalent to JS `loadIncremental(doc, data)`.
#[no_mangle]
pub unsafe extern "C" fn AMload_incremental(
    doc: *mut AMdoc,
    data: *const u8,
    len: usize,
) -> i32 {
    check_ptr!(doc);
    if data.is_null() && len > 0 {
        set_last_error("null data with non-zero length");
        return AM_ERR;
    }
    let bytes: &[u8] = if len == 0 { &[] } else { std::slice::from_raw_parts(data, len) };
    match (*doc).0.load_incremental(bytes) {
        Ok(_) => AM_OK,
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Fork the document at specific heads (immutable snapshot). Equivalent to JS `view(heads)`.
/// On success, writes new doc handle to `*out_doc`. Caller frees with `AMdestroy_doc`.
#[no_mangle]
pub unsafe extern "C" fn AMfork_at(
    doc: *mut AMdoc,
    heads: *const u8,
    heads_len: usize,
    out_doc: *mut *mut AMdoc,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_doc);
    let parsed = parse_heads(heads, heads_len);
    match (*doc).0.fork_at(&parsed) {
        Ok(forked) => {
            *out_doc = Box::into_raw(Box::new(AMdoc(forked)));
            AM_OK
        }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get the object type. Returns JSON: `"map"`, `"list"`, `"text"`, or error.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMobject_type(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.object_type(&oid) {
        Ok(ot) => write_json_out(json!(obj_type_str(&ot)), out_json, out_len),
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Diff between two sets of heads. Returns JSON patch array.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMdiff(
    doc: *mut AMdoc,
    before_heads: *const u8,
    before_heads_len: usize,
    after_heads: *const u8,
    after_heads_len: usize,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let before = parse_heads(before_heads, before_heads_len);
    let after = parse_heads(after_heads, after_heads_len);
    let patches = (*doc).0.diff(&before, &after);
    let patch_jsons: Vec<JsonValue> = patches.iter().map(patch_to_json).collect();
    write_json_out(JsonValue::Array(patch_jsons), out_json, out_len)
}

/// Update a text object by diffing old vs new text. Equivalent to JS `updateText`.
#[no_mangle]
pub unsafe extern "C" fn AMupdate_text(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    new_text: *const c_char,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(new_text);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let text = match c_str_to_str(new_text) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.update_text(&oid, text) {
        Ok(_) => AM_OK,
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Add a mark (rich text annotation) to a text object.
/// `name` and `value_json` describe the mark (e.g., name="bold", value="true").
/// `expand` controls how the mark grows: 0=none, 1=before, 2=after, 3=both.
#[no_mangle]
pub unsafe extern "C" fn AMmark(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    start: usize,
    end: usize,
    name: *const c_char,
    value_json: *const c_char,
    expand: u8,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(name);
    check_ptr!(value_json);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let mark_name = match c_str_to_str(name) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let val_str = match c_str_to_str(value_json) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let jv: JsonValue = match serde_json::from_str(val_str) {
        Ok(v) => v,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let scalar = match json_val_to_scalar(&jv) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let expand_mark = match expand {
        0 => ExpandMark::None,
        1 => ExpandMark::Before,
        2 => ExpandMark::After,
        3 => ExpandMark::Both,
        _ => { set_last_error("expand must be 0-3 (none/before/after/both)"); return AM_ERR; }
    };
    use automerge::marks::Mark;
    let mark = Mark::new(mark_name.to_string(), scalar, start, end);
    match (*doc).0.mark(&oid, mark, expand_mark) {
        Ok(_) => AM_OK,
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Remove a mark from a range of a text object.
/// `expand`: 0=none, 1=before, 2=after, 3=both.
#[no_mangle]
pub unsafe extern "C" fn AMunmark(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    name: *const c_char,
    start: usize,
    end: usize,
    expand: u8,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(name);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let mark_name = match c_str_to_str(name) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let expand_mark = match expand {
        0 => ExpandMark::None,
        1 => ExpandMark::Before,
        2 => ExpandMark::After,
        3 => ExpandMark::Both,
        _ => { set_last_error("expand must be 0-3"); return AM_ERR; }
    };
    match (*doc).0.unmark(&oid, mark_name, start, end, expand_mark) {
        Ok(_) => AM_OK,
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get all marks on a text object. Returns JSON array of mark objects:
/// `[{"name":"bold","value":true,"start":0,"end":5}, ...]`
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMmarks(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.marks(&oid) {
        Ok(marks) => {
            let arr: Vec<JsonValue> = marks.into_iter().map(|m| json!({
                "name": m.name(),
                "value": scalar_to_json_value(m.value()),
                "start": m.start,
                "end": m.end,
            })).collect();
            write_json_out(JsonValue::Array(arr), out_json, out_len)
        }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get marks at specific heads. Returns JSON array like AMmarks.
#[no_mangle]
pub unsafe extern "C" fn AMmarks_at(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    heads: *const u8,
    heads_len: usize,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let parsed = parse_heads(heads, heads_len);
    match (*doc).0.marks_at(&oid, &parsed) {
        Ok(marks) => {
            let arr: Vec<JsonValue> = marks.into_iter().map(|m| json!({
                "name": m.name(),
                "value": scalar_to_json_value(m.value()),
                "start": m.start,
                "end": m.end,
            })).collect();
            write_json_out(JsonValue::Array(arr), out_json, out_len)
        }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get a cursor for a position in a text object. Returns cursor as a string.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMget_cursor(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    position: usize,
    heads: *const u8,
    heads_len: usize,
    out_cursor: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_cursor);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let h = if heads.is_null() || heads_len == 0 {
        None
    } else {
        Some(parse_heads(heads, heads_len))
    };
    match (*doc).0.get_cursor(&oid, position, h.as_deref()) {
        Ok(cursor) => write_cstring_out(cursor.to_string(), out_cursor, out_len),
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Resolve a cursor string to a position. Returns position as usize.
#[no_mangle]
pub unsafe extern "C" fn AMget_cursor_position(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    cursor_str: *const c_char,
    heads: *const u8,
    heads_len: usize,
    out_position: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(cursor_str);
    check_ptr!(out_position);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let cs = match c_str_to_str(cursor_str) {
        Ok(s) => s,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let cursor: Cursor = match cs.try_into() {
        Ok(c) => c,
        Err(e) => { set_last_error(format!("invalid cursor: {}", e)); return AM_ERR; }
    };
    let h = if heads.is_null() || heads_len == 0 {
        None
    } else {
        Some(parse_heads(heads, heads_len))
    };
    match (*doc).0.get_cursor_position(&oid, &cursor, h.as_deref()) {
        Ok(pos) => { *out_position = pos; AM_OK }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get rich text spans for a text object. Returns JSON array:
/// `[{"type":"text","value":"hello"},{"type":"block","value":{...}},...]`
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMspans(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    match (*doc).0.spans(&oid) {
        Ok(spans) => {
            let arr: Vec<JsonValue> = spans.map(|span| {
                match span {
                    automerge::iter::Span::Text(text, _) => json!({
                        "type": "text",
                        "value": text,
                    }),
                    automerge::iter::Span::Block(block_map) => json!({
                        "type": "block",
                        "value": format!("{:?}", block_map),
                    }),
                    _ => json!({
                        "type": "other",
                    }),
                }
            }).collect();
            write_json_out(JsonValue::Array(arr), out_json, out_len)
        }
        Err(e) => { set_last_error(e.to_string()); AM_ERR }
    }
}

/// Get document statistics. Returns JSON object.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMstats(
    doc: *mut AMdoc,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    // stats() returns a Stats struct; serialize its fields
    // The struct has undetermined fields; let's use Debug format as JSON string
    let stats = (*doc).0.stats();
    write_json_out(json!(format!("{:?}", stats)), out_json, out_len)
}

/// Get entries from a map in a key range. start/end are C strings (NUL or empty = unbounded).
/// Returns JSON array of `[key, value_json]` pairs. Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMmap_range(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    start: *const c_char,
    end: *const c_char,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let start_key = if start.is_null() { "" } else { match c_str_to_str(start) { Ok(s) => s, Err(e) => { set_last_error(e); return AM_ERR; }} };
    let end_key   = if end.is_null()   { "" } else { match c_str_to_str(end)   { Ok(s) => s, Err(e) => { set_last_error(e); return AM_ERR; }} };
    let entries: Vec<JsonValue> = if start_key.is_empty() && end_key.is_empty() {
        (*doc).0.map_range(&oid, ..).map(|item| json!([item.key, value_to_json(&item.value, &item.id)])).collect()
    } else if start_key.is_empty() {
        (*doc).0.map_range(&oid, ..end_key.to_string()).map(|item| json!([item.key, value_to_json(&item.value, &item.id)])).collect()
    } else if end_key.is_empty() {
        (*doc).0.map_range(&oid, start_key.to_string()..).map(|item| json!([item.key, value_to_json(&item.value, &item.id)])).collect()
    } else {
        (*doc).0.map_range(&oid, start_key.to_string()..end_key.to_string()).map(|item| json!([item.key, value_to_json(&item.value, &item.id)])).collect()
    };
    write_json_out(JsonValue::Array(entries), out_json, out_len)
}

/// Get entries from a list in an index range.
/// Returns JSON array of values. Caller frees with `AMfree_bytes(ptr, len + 1)`.
#[no_mangle]
pub unsafe extern "C" fn AMlist_range(
    doc: *mut AMdoc,
    obj_id: *const c_char,
    start: usize,
    end: usize,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    check_ptr!(doc);
    check_ptr!(out_json);
    check_ptr!(out_len);
    let oid = match read_obj_id(obj_id) {
        Ok(id) => id,
        Err(e) => { set_last_error(e); return AM_ERR; }
    };
    let entries: Vec<JsonValue> = (*doc).0.list_range(&oid, start..end)
        .map(|item| value_to_json(&item.value, &item.id))
        .collect();
    write_json_out(JsonValue::Array(entries), out_json, out_len)
}


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

// ─── New helpers ─────────────────────────────────────────────────────────────

/// Convert an `ObjId` to its canonical string representation.
/// ROOT → `"_root"`; other objects → `"counter@hexactor"`.
fn exid_to_string(id: &ObjId) -> String {
    format!("{}", id)
}

/// Parse a string back to an `ObjId`. NULL or "" or "_root" → ROOT.
unsafe fn read_obj_id(ptr: *const c_char) -> Result<ObjId, String> {
    if ptr.is_null() {
        return Ok(ROOT);
    }
    let s = c_str_to_str(ptr)?;
    parse_obj_id(s)
}

fn parse_obj_id(s: &str) -> Result<ObjId, String> {
    if s.is_empty() || s == "_root" {
        return Ok(ROOT);
    }
    let at = s.find('@').ok_or_else(|| format!("invalid obj_id '{}': missing '@'", s))?;
    let counter: u64 = s[..at]
        .parse()
        .map_err(|_| format!("invalid obj_id counter '{}'", &s[..at]))?;
    let actor_bytes =
        hex_decode(&s[at + 1..]).map_err(|e| format!("invalid obj_id actor hex: {}", e))?;
    let actor = ActorId::from(actor_bytes);
    Ok(ObjId::Id(counter, actor, 0))
}

fn parse_obj_type(ptr: *const c_char) -> Result<ObjType, String> {
    let s = unsafe { c_str_to_str(ptr)? };
    match s {
        "map" => Ok(ObjType::Map),
        "list" => Ok(ObjType::List),
        "text" => Ok(ObjType::Text),
        other => Err(format!("unknown obj_type '{}': use 'map', 'list', or 'text'", other)),
    }
}

/// Convert `Value` + its ExId to a `serde_json::Value`.
/// Scalars → raw JSON. Objects → `{"_obj_id":"...","_obj_type":"map|list|text"}`.
fn value_to_json<'a>(val: &automerge::Value<'a>, obj_id: &ObjId) -> JsonValue {
    match val {
        automerge::Value::Scalar(sv) => scalar_to_json_value(sv),
        automerge::Value::Object(ot) => json!({
            "_obj_id":   exid_to_string(obj_id),
            "_obj_type": obj_type_str(ot),
        }),
    }
}

fn scalar_to_json_value(sv: &ScalarValue) -> JsonValue {
    match sv {
        ScalarValue::Null => JsonValue::Null,
        ScalarValue::Boolean(b) => json!(*b),
        ScalarValue::Int(i) => json!(*i),
        ScalarValue::Uint(u) => json!(*u),
        ScalarValue::F64(f) => json!(*f),
        ScalarValue::Str(s) => json!(s.as_str()),
        ScalarValue::Bytes(b) => JsonValue::Array(b.iter().map(|n| json!(*n)).collect()),
        ScalarValue::Counter(c) => json!(i64::from(c)),
        ScalarValue::Timestamp(t) => json!(*t),
        ScalarValue::Unknown { type_code, bytes } => {
            json!({ "_unknown_type": type_code, "_bytes": bytes })
        }
    }
}

fn obj_type_str(ot: &ObjType) -> &'static str {
    match ot {
        ObjType::Map | ObjType::Table => "map",
        ObjType::List => "list",
        ObjType::Text => "text",
    }
}

/// Serialize `value` to JSON, write NUL-terminated bytes to out, return AM_OK.
unsafe fn write_json_out(
    value: JsonValue,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    let s = match serde_json::to_string(&value) {
        Ok(s) => s,
        Err(e) => { set_last_error(e.to_string()); return AM_ERR; }
    };
    let mut bytes = s.into_bytes();
    *out_len = bytes.len();
    bytes.push(0);
    bytes.shrink_to_fit();
    *out_json = bytes.as_mut_ptr();
    std::mem::forget(bytes);
    AM_OK
}

/// Write a Rust String as a NUL-terminated C-string to the out pointers.
/// Caller frees with `AMfree_bytes(ptr, len + 1)`.
/// `*out_len` = string length WITHOUT the NUL byte.
unsafe fn write_cstring_out(
    s: String,
    out_ptr: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    let mut bytes = s.into_bytes();
    *out_len = bytes.len();
    bytes.push(0);
    bytes.shrink_to_fit();
    *out_ptr = bytes.as_mut_ptr();
    std::mem::forget(bytes);
    AM_OK
}

/// Read a NUL-terminated C string to a Rust `&str`.
unsafe fn c_str_to_str<'a>(ptr: *const c_char) -> Result<&'a str, String> {
    if ptr.is_null() {
        return Err("null string pointer".to_string());
    }
    std::ffi::CStr::from_ptr(ptr)
        .to_str()
        .map_err(|_| "invalid UTF-8 in C string".to_string())
}

/// Simple hex decode — avoids adding the `hex` crate as a direct dependency.
fn hex_decode(s: &str) -> Result<Vec<u8>, String> {
    if s.len() % 2 != 0 {
        return Err("odd-length hex string".to_string());
    }
    (0..s.len())
        .step_by(2)
        .map(|i| {
            u8::from_str_radix(&s[i..i + 2], 16)
                .map_err(|_| format!("invalid hex byte at position {}", i))
        })
        .collect()
}

/// Serialize an automerge `Patch` to a serde_json object.
fn patch_to_json(patch: &Patch) -> JsonValue {
    let obj = exid_to_string(&patch.obj);
    let path: Vec<JsonValue> = patch
        .path
        .iter()
        .map(|(id, prop)| json!([exid_to_string(id), prop_to_json(prop)]))
        .collect();
    let action = match &patch.action {
        PatchAction::PutMap { key, value: (val, vid), conflict } => json!({
            "op": "put_map",
            "key": key.as_str(),
            "value": value_to_json(val, vid),
            "conflict": conflict,
        }),
        PatchAction::PutSeq { index, value: (val, vid), conflict } => json!({
            "op": "put_seq",
            "index": index,
            "value": value_to_json(val, vid),
            "conflict": conflict,
        }),
        PatchAction::Insert { index, values, .. } => {
            let vals: Vec<JsonValue> =
                values.iter().map(|(v, id, _)| value_to_json(v, id)).collect();
            json!({
                "op": "insert",
                "index": index,
                "values": vals,
            })
        }
        PatchAction::SpliceText { index, value, .. } => json!({
            "op": "splice_text",
            "index": index,
            "value": value.make_string(),
        }),
        PatchAction::Increment { prop, value } => json!({
            "op": "increment",
            "prop": prop_to_json(prop),
            "value": value,
        }),
        PatchAction::Conflict { prop } => json!({
            "op": "conflict",
            "prop": prop_to_json(prop),
        }),
        PatchAction::DeleteMap { key } => json!({
            "op": "delete_map",
            "key": key.as_str(),
        }),
        PatchAction::DeleteSeq { index, length } => json!({
            "op": "delete_seq",
            "index": index,
            "length": length,
        }),
        PatchAction::Mark { .. } => json!({ "op": "mark" }),
    };
    json!({ "obj": obj, "path": path, "action": action })
}

fn prop_to_json(prop: &Prop) -> JsonValue {
    match prop {
        Prop::Map(k) => json!(k.as_str()),
        Prop::Seq(i) => json!(*i),
    }
}

