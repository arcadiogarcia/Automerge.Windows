use std::cell::RefCell;

thread_local! {
    static LAST_ERROR: RefCell<String> = RefCell::new(String::new());
}

pub(crate) fn set_last_error(msg: impl Into<String>) {
    LAST_ERROR.with(|e| *e.borrow_mut() = msg.into());
}

/// Copies the last error message into the provided buffer.
/// Returns the number of bytes written (including NUL terminator).
/// If `buf` is null or `buf_len` is 0, returns the required buffer size.
#[no_mangle]
pub unsafe extern "C" fn AMget_last_error(buf: *mut std::os::raw::c_char, buf_len: usize) -> usize {
    LAST_ERROR.with(|e| {
        let msg = e.borrow();
        let bytes = msg.as_bytes();
        let required = bytes.len() + 1; // +1 for NUL

        if buf.is_null() || buf_len == 0 {
            return required;
        }

        let copy_len = std::cmp::min(bytes.len(), buf_len - 1);
        let dst = std::slice::from_raw_parts_mut(buf as *mut u8, buf_len);
        dst[..copy_len].copy_from_slice(&bytes[..copy_len]);
        dst[copy_len] = 0;
        copy_len + 1
    })
}
