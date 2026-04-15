//! Integration tests for the C ABI layer.
//! These tests use the safe Rust-level wrappers around the unsafe C functions
//! so they can be run with `cargo test`.

use std::ffi::CString;

// Re-export the library symbols under test
use automerge_core::*;

// --- Test helpers -------------------------------------------------------------

unsafe fn get_last_error() -> String {
    let mut buf = vec![0u8; 1024];
    let n = AMget_last_error(buf.as_mut_ptr() as *mut _, buf.len());
    let s = std::str::from_utf8(&buf[..n.saturating_sub(1)]).unwrap_or("");
    s.to_string()
}

unsafe fn save_doc(doc: *mut AMdoc) -> Vec<u8> {
    let mut ptr: *mut u8 = std::ptr::null_mut();
    let mut len: usize = 0;
    assert_eq!(AMsave(doc, &mut ptr, &mut len), 0, "AMsave failed");
    let v = std::slice::from_raw_parts(ptr, len).to_vec();
    AMfree_bytes(ptr, len);
    v
}

unsafe fn get_heads_bytes(doc: *mut AMdoc) -> Vec<u8> {
    let mut ptr: *mut u8 = std::ptr::null_mut();
    let mut len: usize = 0;
    assert_eq!(AMget_heads(doc, &mut ptr, &mut len), 0, "AMget_heads failed");
    let v = std::slice::from_raw_parts(ptr, len).to_vec();
    AMfree_bytes(ptr, len);
    v
}

unsafe fn get_value_str(doc: *mut AMdoc, path: &str) -> String {
    let path_c = CString::new(path).unwrap();
    let mut ptr: *mut u8 = std::ptr::null_mut();
    let mut len: usize = 0;
    let rc = AMget_value(doc as *const _, path_c.as_ptr(), &mut ptr, &mut len);
    assert_eq!(rc, 0, "AMget_value failed: {}", get_last_error());
    let s = std::str::from_utf8(std::slice::from_raw_parts(ptr, len))
        .unwrap()
        .to_string();
    AMfree_bytes(ptr, len + 1);
    s
}

unsafe fn put_json_root(doc: *mut AMdoc, json: &str) {
    let cjson = CString::new(json).unwrap();
    let rc = AMput_json_root(doc, cjson.as_ptr());
    assert_eq!(rc, 0, "AMput_json_root failed: {}", get_last_error());
}

// --- Document lifecycle tests -------------------------------------------------

#[test]
fn test_create_and_destroy() {
    unsafe {
        let doc = AMcreate_doc();
        assert!(!doc.is_null());
        AMdestroy_doc(doc);
    }
}

#[test]
fn test_multiple_create_destroy() {
    unsafe {
        let docs: Vec<_> = (0..10).map(|_| AMcreate_doc()).collect();
        for d in docs {
            AMdestroy_doc(d);
        }
    }
}

// --- Persistence tests --------------------------------------------------------

#[test]
fn test_save_empty_doc() {
    unsafe {
        let doc = AMcreate_doc();
        let bytes = save_doc(doc);
        assert!(!bytes.is_empty(), "saved bytes should not be empty");
        AMdestroy_doc(doc);
    }
}

#[test]
fn test_save_load_roundtrip() {
    unsafe {
        let doc1 = AMcreate_doc();
        put_json_root(doc1, r#"{"name":"Alice","age":30}"#);

        let bytes = save_doc(doc1);
        assert!(!bytes.is_empty());

        let mut doc2: *mut AMdoc = std::ptr::null_mut();
        let rc = AMload(bytes.as_ptr(), bytes.len(), &mut doc2);
        assert_eq!(rc, 0, "AMload failed: {}", get_last_error());
        assert!(!doc2.is_null());

        let val = get_value_str(doc2, r#"["name"]"#);
        assert_eq!(val, r#""Alice""#);

        let val2 = get_value_str(doc2, r#"["age"]"#);
        assert_eq!(val2, "30");

        AMdestroy_doc(doc1);
        AMdestroy_doc(doc2);
    }
}

// --- Heads tests --------------------------------------------------------------

#[test]
fn test_get_heads_empty_doc() {
    unsafe {
        let doc = AMcreate_doc();
        let h = get_heads_bytes(doc);
        // A fresh doc has no changes yet, so no heads
        assert_eq!(h.len() % 32, 0);
        AMdestroy_doc(doc);
    }
}

#[test]
fn test_heads_change_after_put() {
    unsafe {
        let doc = AMcreate_doc();
        let h0 = get_heads_bytes(doc);

        put_json_root(doc, r#"{"key":"value"}"#);

        let h1 = get_heads_bytes(doc);
        assert!(h1.len() >= 32, "should have at least one head after a put");
        assert_ne!(h0, h1);

        AMdestroy_doc(doc);
    }
}

#[test]
fn test_heads_are_32_byte_aligned() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"x":1}"#);
        let h = get_heads_bytes(doc);
        assert_eq!(h.len() % 32, 0, "head bytes must be multiple of 32");
        AMdestroy_doc(doc);
    }
}

// --- Changes tests ------------------------------------------------------------

#[test]
fn test_get_all_changes() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"a":1}"#);

        let mut ptr: *mut u8 = std::ptr::null_mut();
        let mut len: usize = 0;
        let rc = AMget_changes(doc, std::ptr::null(), 0, &mut ptr, &mut len);
        assert_eq!(rc, 0, "AMget_changes failed");
        assert!(len > 0, "should have changes after put");
        AMfree_bytes(ptr, len);

        AMdestroy_doc(doc);
    }
}

#[test]
fn test_apply_changes_to_new_doc() {
    unsafe {
        let doc1 = AMcreate_doc();
        put_json_root(doc1, r#"{"greeting":"hello"}"#);

        // Get all changes from doc1
        let mut ch_ptr: *mut u8 = std::ptr::null_mut();
        let mut ch_len: usize = 0;
        assert_eq!(
            AMget_changes(doc1, std::ptr::null(), 0, &mut ch_ptr, &mut ch_len),
            0
        );

        // Create doc2 and apply those changes
        let doc2 = AMcreate_doc();
        assert_eq!(AMapply_changes(doc2, ch_ptr, ch_len), 0, "apply failed");
        AMfree_bytes(ch_ptr, ch_len);

        // doc2 should now have the same value
        let val = get_value_str(doc2, r#"["greeting"]"#);
        assert_eq!(val, r#""hello""#);

        AMdestroy_doc(doc1);
        AMdestroy_doc(doc2);
    }
}

#[test]
fn test_get_changes_since_heads() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"first":1}"#);
        let heads_after_first = get_heads_bytes(doc);

        put_json_root(doc, r#"{"second":2}"#);

        // Changes since first heads should include only the second change
        let mut ptr: *mut u8 = std::ptr::null_mut();
        let mut len: usize = 0;
        let rc = AMget_changes(
            doc,
            heads_after_first.as_ptr(),
            heads_after_first.len(),
            &mut ptr,
            &mut len,
        );
        assert_eq!(rc, 0);
        // The delta should be non-empty
        assert!(len > 0, "expected incremental changes");
        AMfree_bytes(ptr, len);

        AMdestroy_doc(doc);
    }
}

// --- Merge tests --------------------------------------------------------------

#[test]
fn test_merge_two_docs() {
    unsafe {
        // doc1 sets "a", doc2 sets "b"
        let doc1 = AMcreate_doc();
        put_json_root(doc1, r#"{"a":"from1"}"#);

        let doc2 = AMcreate_doc();
        put_json_root(doc2, r#"{"b":"from2"}"#);

        // Merge doc2 into doc1
        assert_eq!(AMmerge(doc1, doc2), 0, "AMmerge failed");

        let v_a = get_value_str(doc1, r#"["a"]"#);
        let v_b = get_value_str(doc1, r#"["b"]"#);
        assert_eq!(v_a, r#""from1""#);
        assert_eq!(v_b, r#""from2""#);

        AMdestroy_doc(doc1);
        AMdestroy_doc(doc2);
    }
}

#[test]
fn test_merge_idempotent() {
    unsafe {
        let doc1 = AMcreate_doc();
        put_json_root(doc1, r#"{"k":"v"}"#);

        let doc2 = AMcreate_doc();
        put_json_root(doc2, r#"{"k":"v"}"#);

        // Merge twice should not error
        assert_eq!(AMmerge(doc1, doc2), 0);
        assert_eq!(AMmerge(doc1, doc2), 0);

        AMdestroy_doc(doc1);
        AMdestroy_doc(doc2);
    }
}

// --- get_value tests ----------------------------------------------------------

#[test]
fn test_get_root_value() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"x":42,"y":"hello"}"#);

        // Empty path ? full root
        let root = get_value_str(doc, "[]");
        let parsed: serde_json::Value = serde_json::from_str(&root).unwrap();
        assert_eq!(parsed["x"], serde_json::json!(42));
        assert_eq!(parsed["y"], serde_json::json!("hello"));

        AMdestroy_doc(doc);
    }
}

#[test]
fn test_get_nested_value() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"score":99}"#);

        let val = get_value_str(doc, r#"["score"]"#);
        assert_eq!(val, "99");

        AMdestroy_doc(doc);
    }
}

#[test]
fn test_get_value_null_path() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"n":null}"#);

        let mut ptr: *mut u8 = std::ptr::null_mut();
        let mut len: usize = 0;
        // NULL path ? serialise root
        let rc = AMget_value(doc as *const _, std::ptr::null(), &mut ptr, &mut len);
        assert_eq!(rc, 0);
        assert!(len > 0);
        AMfree_bytes(ptr, len + 1);

        AMdestroy_doc(doc);
    }
}

#[test]
fn test_get_value_invalid_key_returns_error() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"real":1}"#);

        let path = CString::new(r#"["missing"]"#).unwrap();
        let mut ptr: *mut u8 = std::ptr::null_mut();
        let mut len: usize = 0;
        let rc = AMget_value(doc as *const _, path.as_ptr(), &mut ptr, &mut len);
        assert_eq!(rc, 1, "should fail for missing key");

        let err = get_last_error();
        assert!(err.contains("not found"), "error should mention 'not found': {}", err);

        AMdestroy_doc(doc);
    }
}

// --- Sync tests ---------------------------------------------------------------

#[test]
fn test_sync_two_peers_converge() {
    unsafe {
        // Peer A
        let doc_a = AMcreate_doc();
        put_json_root(doc_a, r#"{"from_a":"data_a"}"#);

        // Peer B (starts empty)
        let doc_b = AMcreate_doc();

        let ss_a = AMcreate_sync_state();
        let ss_b = AMcreate_sync_state();

        // Run the sync protocol exchange until both settle
        let max_rounds = 10;
        for _ in 0..max_rounds {
            // A ? B
            let mut msg_ptr: *mut u8 = std::ptr::null_mut();
            let mut msg_len: usize = 0;
            assert_eq!(AMgenerate_sync_message(doc_a, ss_a, &mut msg_ptr, &mut msg_len), 0);
            if !msg_ptr.is_null() && msg_len > 0 {
                assert_eq!(AMreceive_sync_message(doc_b, ss_b, msg_ptr, msg_len), 0);
                AMfree_bytes(msg_ptr, msg_len);
            }

            // B ? A
            let mut msg_ptr: *mut u8 = std::ptr::null_mut();
            let mut msg_len: usize = 0;
            assert_eq!(AMgenerate_sync_message(doc_b, ss_b, &mut msg_ptr, &mut msg_len), 0);
            if !msg_ptr.is_null() && msg_len > 0 {
                assert_eq!(AMreceive_sync_message(doc_a, ss_a, msg_ptr, msg_len), 0);
                AMfree_bytes(msg_ptr, msg_len);
            }

            // Check convergence: heads match
            let ha = get_heads_bytes(doc_a);
            let hb = get_heads_bytes(doc_b);
            if ha == hb && !ha.is_empty() {
                break;
            }
        }

        // Both should have A's value
        let val_b = get_value_str(doc_b, r#"["from_a"]"#);
        assert_eq!(val_b, r#""data_a""#);

        AMfree_sync_state(ss_a);
        AMfree_sync_state(ss_b);
        AMdestroy_doc(doc_a);
        AMdestroy_doc(doc_b);
    }
}

#[test]
fn test_sync_bidirectional() {
    unsafe {
        let doc_a = AMcreate_doc();
        put_json_root(doc_a, r#"{"written_by":"alice"}"#);

        let doc_b = AMcreate_doc();
        put_json_root(doc_b, r#"{"written_by_b":"bob"}"#);

        let ss_a = AMcreate_sync_state();
        let ss_b = AMcreate_sync_state();

        for _ in 0..10 {
            let mut mp: *mut u8 = std::ptr::null_mut();
            let mut ml: usize = 0;
            assert_eq!(AMgenerate_sync_message(doc_a, ss_a, &mut mp, &mut ml), 0);
            if !mp.is_null() && ml > 0 {
                assert_eq!(AMreceive_sync_message(doc_b, ss_b, mp, ml), 0);
                AMfree_bytes(mp, ml);
            }
            let mut mp: *mut u8 = std::ptr::null_mut();
            let mut ml: usize = 0;
            assert_eq!(AMgenerate_sync_message(doc_b, ss_b, &mut mp, &mut ml), 0);
            if !mp.is_null() && ml > 0 {
                assert_eq!(AMreceive_sync_message(doc_a, ss_a, mp, ml), 0);
                AMfree_bytes(mp, ml);
            }
            let ha = get_heads_bytes(doc_a);
            let hb = get_heads_bytes(doc_b);
            if ha == hb && !ha.is_empty() { break; }
        }

        let va = get_value_str(doc_a, r#"["written_by_b"]"#);
        let vb = get_value_str(doc_b, r#"["written_by"]"#);
        assert_eq!(va, r#""bob""#);
        assert_eq!(vb, r#""alice""#);

        AMfree_sync_state(ss_a);
        AMfree_sync_state(ss_b);
        AMdestroy_doc(doc_a);
        AMdestroy_doc(doc_b);
    }
}

// --- Error handling tests -----------------------------------------------------

#[test]
fn test_load_invalid_bytes_returns_error() {
    unsafe {
        let garbage = b"this is not valid automerge data";
        let mut doc: *mut AMdoc = std::ptr::null_mut();
        let rc = AMload(garbage.as_ptr(), garbage.len(), &mut doc);
        assert_eq!(rc, 1);
        assert!(doc.is_null());
        let err = get_last_error();
        assert!(!err.is_empty(), "error message should be set");
    }
}

#[test]
fn test_apply_invalid_changes_returns_error() {
    unsafe {
        let doc = AMcreate_doc();
        let junk = b"invalid change bytes!!";
        let rc = AMapply_changes(doc, junk.as_ptr(), junk.len());
        assert_eq!(rc, 1);
        let err = get_last_error();
        assert!(!err.is_empty());
        AMdestroy_doc(doc);
    }
}

#[test]
fn test_null_doc_returns_error() {
    unsafe {
        let mut ptr: *mut u8 = std::ptr::null_mut();
        let mut len: usize = 0;
        let rc = AMsave(std::ptr::null_mut(), &mut ptr, &mut len);
        assert_eq!(rc, 1);
    }
}

// --- Sync state persistence tests --------------------------------------------

#[test]
fn test_sync_state_save_load() {
    unsafe {
        let ss = AMcreate_sync_state();

        let mut ptr: *mut u8 = std::ptr::null_mut();
        let mut len: usize = 0;
        assert_eq!(AMsave_sync_state(ss, &mut ptr, &mut len), 0);

        let mut ss2: *mut AMsync_state = std::ptr::null_mut();
        assert_eq!(AMload_sync_state(ptr, len, &mut ss2), 0);
        assert!(!ss2.is_null());
        AMfree_bytes(ptr, len);

        AMfree_sync_state(ss);
        AMfree_sync_state(ss2);
    }
}

// --- Multiple put tests -------------------------------------------------------

#[test]
fn test_multiple_puts_overwrite() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"counter":1}"#);
        put_json_root(doc, r#"{"counter":2}"#);
        put_json_root(doc, r#"{"counter":3}"#);

        let val = get_value_str(doc, r#"["counter"]"#);
        assert_eq!(val, "3");

        AMdestroy_doc(doc);
    }
}

#[test]
fn test_various_scalar_types() {
    unsafe {
        let doc = AMcreate_doc();
        put_json_root(doc, r#"{"s":"text","i":-5,"u":42,"f":3.14,"b":true,"n":null}"#);

        let s = get_value_str(doc, r#"["s"]"#);
        assert_eq!(s, r#""text""#);

        let i = get_value_str(doc, r#"["i"]"#);
        assert_eq!(i, "-5");

        let b = get_value_str(doc, r#"["b"]"#);
        assert_eq!(b, "true");

        let n = get_value_str(doc, r#"["n"]"#);
        assert_eq!(n, "null");

        AMdestroy_doc(doc);
    }
}
