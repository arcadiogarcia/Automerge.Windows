use automerge::sync::{self, SyncDoc};

use crate::doc::{alloc_bytes, AMdoc, AM_ERR, AM_OK};
use crate::error::set_last_error;

/// Opaque handle wrapping an automerge `sync::State`.
#[allow(non_camel_case_types)]
pub struct AMsync_state(pub(crate) sync::State);

// Sync state lifecycle

/// Create a new sync state. Caller frees with `AMfree_sync_state`.
#[no_mangle]
pub extern "C" fn AMcreate_sync_state() -> *mut AMsync_state {
    Box::into_raw(Box::new(AMsync_state(sync::State::default())))
}

/// Free a sync state handle.
/// # Safety
/// `state` must be a valid pointer returned by `AMcreate_sync_state`.
#[no_mangle]
pub unsafe extern "C" fn AMfree_sync_state(state: *mut AMsync_state) {
    if !state.is_null() {
        drop(Box::from_raw(state));
    }
}

// Sync protocol

/// Generate the next sync message to send to a remote peer.
///
/// If there is a message to send, returns 0 and writes `*out_msg` /
/// `*out_len`.  If there is nothing to send (sync is complete), returns 0
/// with `*out_len == 0` and `*out_msg == NULL`.
///
/// Caller frees a non-NULL `*out_msg` with `AMfree_bytes`.
///
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMgenerate_sync_message(
    doc: *mut AMdoc,
    state: *mut AMsync_state,
    out_msg: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if doc.is_null() || state.is_null() || out_msg.is_null() || out_len.is_null() {
        set_last_error("null pointer argument");
        return AM_ERR;
    }

    let msg_opt = (*doc).0.sync().generate_sync_message(&mut (*state).0);

    match msg_opt {
        None => {
            *out_msg = std::ptr::null_mut();
            *out_len = 0;
            AM_OK
        }
        Some(msg) => {
            let encoded = msg.encode();
            *out_len = encoded.len();
            *out_msg = alloc_bytes(encoded);
            AM_OK
        }
    }
}

/// Process an incoming sync message from a remote peer.
///
/// `msg` and `len` describe the byte buffer received from the peer (encoded
/// with `AMgenerate_sync_message` on the other side).
///
/// Returns 0 on success.
///
/// # Safety
/// All pointer arguments must be valid; `msg` must be `len` bytes long.
#[no_mangle]
pub unsafe extern "C" fn AMreceive_sync_message(
    doc: *mut AMdoc,
    state: *mut AMsync_state,
    msg: *const u8,
    len: usize,
) -> i32 {
    if doc.is_null() || state.is_null() {
        set_last_error("null pointer argument");
        return AM_ERR;
    }
    if msg.is_null() && len > 0 {
        set_last_error("null msg pointer with non-zero length");
        return AM_ERR;
    }

    let bytes: &[u8] = if len == 0 { &[] } else { std::slice::from_raw_parts(msg, len) };

    let decoded = match sync::Message::decode(bytes) {
        Ok(m) => m,
        Err(e) => {
            set_last_error(format!("sync message decode error: {:?}", e));
            return AM_ERR;
        }
    };

    match (*doc).0.sync().receive_sync_message(&mut (*state).0, decoded) {
        Ok(_) => AM_OK,
        Err(e) => {
            set_last_error(e.to_string());
            AM_ERR
        }
    }
}

/// Encode the sync state to bytes for optional persistence.
/// Caller frees with `AMfree_bytes`.
/// # Safety
/// All pointer arguments must be valid and non-null.
#[no_mangle]
pub unsafe extern "C" fn AMsave_sync_state(
    state: *const AMsync_state,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if state.is_null() || out_bytes.is_null() || out_len.is_null() {
        set_last_error("null pointer argument");
        return AM_ERR;
    }
    let encoded = (*state).0.encode();
    *out_len = encoded.len();
    *out_bytes = alloc_bytes(encoded);
    AM_OK
}

/// Restore a sync state from bytes produced by `AMsave_sync_state`.
/// Caller frees with `AMfree_sync_state`.
/// # Safety
/// All pointer arguments must satisfy their documented constraints.
#[no_mangle]
pub unsafe extern "C" fn AMload_sync_state(
    data: *const u8,
    len: usize,
    out_state: *mut *mut AMsync_state,
) -> i32 {
    if out_state.is_null() {
        set_last_error("null out_state pointer");
        return AM_ERR;
    }
    if data.is_null() && len > 0 {
        set_last_error("null data pointer with non-zero length");
        return AM_ERR;
    }
    let bytes: &[u8] = if len == 0 { &[] } else { std::slice::from_raw_parts(data, len) };
    match sync::State::decode(bytes) {
        Ok(s) => {
            *out_state = Box::into_raw(Box::new(AMsync_state(s)));
            AM_OK
        }
        Err(e) => {
            set_last_error(format!("sync state decode error: {:?}", e));
            AM_ERR
        }
    }
}
