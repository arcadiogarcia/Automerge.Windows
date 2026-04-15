fn main() {
    // C header is manually written at include/automerge_core.h.
    // Re-run if src changes.
    println!("cargo:rerun-if-changed=src/");
}
