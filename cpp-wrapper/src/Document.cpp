#include "../include/automerge/Document.hpp"

#include <cassert>
#include <cstddef>
#include <utility>

namespace automerge {

// ─── Helpers ─────────────────────────────────────────────────────────────────

static std::vector<uint8_t> take_cbuf(uint8_t* ptr, size_t len) {
    std::vector<uint8_t> result;
    if (ptr && len > 0) {
        result.assign(ptr, ptr + len);
        AMfree_bytes(ptr, len);
    } else if (ptr) {
        AMfree_bytes(ptr, 0);
    }
    return result;
}

// Read a NUL-terminated JSON string returned by the C API.
std::string Document::take_json(uint8_t* ptr, size_t len) {
    // ptr points to len bytes of JSON + 1 NUL byte; total alloc = len + 1.
    if (!ptr) return {};
    std::string result(reinterpret_cast<const char*>(ptr), len);
    AMfree_bytes(ptr, len + 1);
    return result;
}

// Read a NUL-terminated object ID string returned by put_object / insert_object.
std::string Document::take_cstring(uint8_t* ptr, size_t len) {
    if (!ptr) return {};
    std::string result(reinterpret_cast<const char*>(ptr), len);
    AMfree_bytes(ptr, len + 1);
    return result;
}

// ─── check ────────────────────────────────────────────────────────────────────

void Document::check(int rc) {
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
}

// ─── Lifecycle ────────────────────────────────────────────────────────────────

Document::Document() : handle_(AMcreate_doc()) {
    if (!handle_) throw AutomergeError("AMcreate_doc returned null");
}

Document::Document(AMdoc* handle) noexcept : handle_(handle) {}

Document::~Document() {
    if (handle_) { AMdestroy_doc(handle_); handle_ = nullptr; }
}

Document::Document(Document&& other) noexcept : handle_(other.handle_) {
    other.handle_ = nullptr;
}

Document& Document::operator=(Document&& other) noexcept {
    if (this != &other) {
        if (handle_) AMdestroy_doc(handle_);
        handle_ = other.handle_;
        other.handle_ = nullptr;
    }
    return *this;
}

// ─── Persistence ─────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::save() const {
    uint8_t* ptr{}; size_t len{};
    check(AMsave(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

Document Document::load(std::span<const uint8_t> data) {
    AMdoc* out{};
    int rc = AMload(data.data(), data.size(), &out);
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
    return Document(out);
}

std::vector<uint8_t> Document::save_incremental() {
    uint8_t* ptr{}; size_t len{};
    check(AMsave_incremental(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

// ─── Fork ─────────────────────────────────────────────────────────────────────

Document Document::fork() {
    AMdoc* out{};
    check(AMfork(handle_, &out));
    return Document(out);
}

// ─── Heads ───────────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::get_heads() const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_heads(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

// ─── Changes ─────────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::get_changes(std::span<const uint8_t> heads) const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_changes(handle_, heads.data(), heads.size(), &ptr, &len));
    return take_cbuf(ptr, len);
}

void Document::apply_changes(std::span<const uint8_t> changes) {
    check(AMapply_changes(handle_, changes.data(), changes.size()));
}

// ─── Merge ───────────────────────────────────────────────────────────────────

void Document::merge(Document& other) {
    check(AMmerge(handle_, other.handle_));
}

// ─── Actor ───────────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::get_actor() const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_actor(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

void Document::set_actor(std::span<const uint8_t> actor_bytes) {
    check(AMset_actor(handle_, actor_bytes.data(), actor_bytes.size()));
}

// ─── Read ────────────────────────────────────────────────────────────────────

std::string Document::get_value(const std::string& path_json) const {
    uint8_t* ptr{}; size_t len{};
    const char* path = path_json.empty() ? nullptr : path_json.c_str();
    check(AMget_value(handle_, path, &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::get(const ObjId& obj_id, const std::string& key) const {
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMget(handle_, oid, key.c_str(), &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::get_idx(const ObjId& obj_id, size_t index) const {
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMget_idx(handle_, oid, index, &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::keys(const ObjId& obj_id) const {
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMkeys(handle_, oid, &ptr, &len));
    return take_json(ptr, len);
}

size_t Document::length(const ObjId& obj_id) const {
    size_t n{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMlength(handle_, oid, &n));
    return n;
}

std::string Document::get_text(const ObjId& obj_id) const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_text(handle_, obj_id.c_str(), &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::get_all(const ObjId& obj_id, const std::string& key) const {
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMget_all(handle_, oid, key.c_str(), &ptr, &len));
    return take_json(ptr, len);
}

// ─── Write ───────────────────────────────────────────────────────────────────

void Document::put_json_root(const std::string& json_obj) {
    check(AMput_json_root(handle_, json_obj.c_str()));
}

void Document::put(const ObjId& obj_id, const std::string& key,
                   const std::string& scalar_json)
{
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMput(handle_, oid, key.c_str(), scalar_json.c_str()));
}

void Document::put_idx(const ObjId& obj_id, size_t index,
                       const std::string& scalar_json)
{
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMput_idx(handle_, oid, index, scalar_json.c_str()));
}

ObjId Document::put_object(const ObjId& obj_id, const std::string& key,
                            const std::string& obj_type)
{
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMput_object(handle_, oid, key.c_str(), obj_type.c_str(), &ptr, &len));
    return take_cstring(ptr, len);
}

void Document::del(const ObjId& obj_id, const std::string& key) {
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMdelete(handle_, oid, key.c_str()));
}

// ─── List operations ─────────────────────────────────────────────────────────

void Document::insert(const ObjId& obj_id, size_t index,
                      const std::string& scalar_json)
{
    check(AMinsert(handle_, obj_id.c_str(), index, scalar_json.c_str()));
}

ObjId Document::insert_object(const ObjId& obj_id, size_t index,
                               const std::string& obj_type)
{
    uint8_t* ptr{}; size_t len{};
    check(AMinsert_object(handle_, obj_id.c_str(), index, obj_type.c_str(), &ptr, &len));
    return take_cstring(ptr, len);
}

void Document::delete_at(const ObjId& obj_id, size_t index) {
    check(AMdelete_at(handle_, obj_id.c_str(), index));
}

// ─── Counter ─────────────────────────────────────────────────────────────────

void Document::put_counter(const ObjId& obj_id, const std::string& key,
                            int64_t initial)
{
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMput_counter(handle_, oid, key.c_str(), initial));
}

void Document::increment(const ObjId& obj_id, const std::string& key,
                          int64_t delta)
{
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMincrement(handle_, oid, key.c_str(), delta));
}

// ─── Text ────────────────────────────────────────────────────────────────────

void Document::splice_text(const ObjId& obj_id, size_t start,
                            std::ptrdiff_t delete_count,
                            const std::string& text)
{
    check(AMsplice_text(handle_, obj_id.c_str(), start, delete_count,
                        text.empty() ? nullptr : text.c_str()));
}

// ─── Commit ──────────────────────────────────────────────────────────────────

void Document::commit(const std::string& message, int64_t timestamp) {
    const char* msg = message.empty() ? nullptr : message.c_str();
    check(AMcommit(handle_, msg, timestamp));
}

// ─── Diff / patches ──────────────────────────────────────────────────────────

std::string Document::diff_incremental() {
    uint8_t* ptr{}; size_t len{};
    check(AMdiff_incremental(handle_, &ptr, &len));
    return take_json(ptr, len);
}

// ─── New APIs ────────────────────────────────────────────────────────────────

std::vector<uint8_t> Document::get_all_changes() const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_all_changes(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

std::vector<uint8_t> Document::get_last_local_change() const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_last_local_change(handle_, &ptr, &len));
    return take_cbuf(ptr, len);
}

std::vector<uint8_t> Document::get_missing_deps(std::span<const uint8_t> heads) const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_missing_deps(handle_, heads.data(), heads.size(), &ptr, &len));
    return take_cbuf(ptr, len);
}

void Document::empty_change(const std::string& message, int64_t timestamp) {
    const char* msg = message.empty() ? nullptr : message.c_str();
    check(AMempty_change(handle_, msg, timestamp));
}

std::vector<uint8_t> Document::save_after(std::span<const uint8_t> heads) const {
    uint8_t* ptr{}; size_t len{};
    check(AMsave_after(handle_, heads.data(), heads.size(), &ptr, &len));
    return take_cbuf(ptr, len);
}

void Document::load_incremental(std::span<const uint8_t> data) {
    check(AMload_incremental(handle_, data.data(), data.size()));
}

Document Document::fork_at(std::span<const uint8_t> heads) {
    AMdoc* out{};
    int rc = AMfork_at(handle_, heads.data(), heads.size(), &out);
    if (rc != AM_OK) {
        char buf[512]{};
        AMget_last_error(buf, sizeof(buf));
        throw AutomergeError(buf);
    }
    return Document(out);
}

std::string Document::object_type(const ObjId& obj_id) const {
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    check(AMobject_type(handle_, oid, &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::diff(std::span<const uint8_t> before_heads,
                           std::span<const uint8_t> after_heads) const {
    uint8_t* ptr{}; size_t len{};
    check(AMdiff(handle_, before_heads.data(), before_heads.size(),
                 after_heads.data(), after_heads.size(), &ptr, &len));
    return take_json(ptr, len);
}

void Document::update_text(const ObjId& obj_id, const std::string& new_text) {
    check(AMupdate_text(handle_, obj_id.c_str(), new_text.c_str()));
}

void Document::mark(const ObjId& obj_id, size_t start, size_t end,
                    const std::string& name, const std::string& value_json,
                    uint8_t expand) {
    check(AMmark(handle_, obj_id.c_str(), start, end,
                 name.c_str(), value_json.c_str(), expand));
}

void Document::unmark(const ObjId& obj_id, const std::string& name,
                      size_t start, size_t end, uint8_t expand) {
    check(AMunmark(handle_, obj_id.c_str(), name.c_str(), start, end, expand));
}

std::string Document::marks(const ObjId& obj_id) const {
    uint8_t* ptr{}; size_t len{};
    check(AMmarks(handle_, obj_id.c_str(), &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::marks_at(const ObjId& obj_id,
                               std::span<const uint8_t> heads) const {
    uint8_t* ptr{}; size_t len{};
    check(AMmarks_at(handle_, obj_id.c_str(), heads.data(), heads.size(), &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::get_cursor(const ObjId& obj_id, size_t position,
                                 std::span<const uint8_t> heads) const {
    uint8_t* ptr{}; size_t len{};
    check(AMget_cursor(handle_, obj_id.c_str(), position,
                       heads.data(), heads.size(), &ptr, &len));
    return take_cstring(ptr, len);
}

size_t Document::get_cursor_position(const ObjId& obj_id,
                                     const std::string& cursor,
                                     std::span<const uint8_t> heads) const {
    size_t pos{};
    check(AMget_cursor_position(handle_, obj_id.c_str(), cursor.c_str(),
                                heads.data(), heads.size(), &pos));
    return pos;
}

std::string Document::spans(const ObjId& obj_id) const {
    uint8_t* ptr{}; size_t len{};
    check(AMspans(handle_, obj_id.c_str(), &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::stats() const {
    uint8_t* ptr{}; size_t len{};
    check(AMstats(handle_, &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::map_range(const ObjId& obj_id,
                                const std::string& start,
                                const std::string& end) const {
    uint8_t* ptr{}; size_t len{};
    const char* oid = obj_id.empty() ? nullptr : obj_id.c_str();
    const char* s = start.empty() ? nullptr : start.c_str();
    const char* e = end.empty()   ? nullptr : end.c_str();
    check(AMmap_range(handle_, oid, s, e, &ptr, &len));
    return take_json(ptr, len);
}

std::string Document::list_range(const ObjId& obj_id,
                                 size_t start, size_t end) const {
    uint8_t* ptr{}; size_t len{};
    check(AMlist_range(handle_, obj_id.c_str(), start, end, &ptr, &len));
    return take_json(ptr, len);
}

} // namespace automerge

