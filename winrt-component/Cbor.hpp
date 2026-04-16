// Cbor.hpp — minimal CBOR encoder/decoder for the automerge-repo v1 sync
// protocol.  Only the subset needed: maps, text strings, byte strings, arrays.
#pragma once
#ifndef AUTOMERGE_CBOR_HPP
#define AUTOMERGE_CBOR_HPP

#include <cstdint>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <variant>
#include <vector>

namespace Cbor {

using Value = std::variant<std::string, std::vector<uint8_t>>;
using Map   = std::unordered_map<std::string, Value>;

// ─── Encoder helpers ──────────────────────────────────────────────────────────

inline void write_len(std::vector<uint8_t>& out, uint8_t major, uint64_t len)
{
    uint8_t mt = static_cast<uint8_t>(major << 5);
    if (len <= 23) {
        out.push_back(mt | static_cast<uint8_t>(len));
    } else if (len <= 0xFF) {
        out.push_back(mt | 24u);
        out.push_back(static_cast<uint8_t>(len));
    } else if (len <= 0xFFFF) {
        out.push_back(mt | 25u);
        out.push_back(static_cast<uint8_t>(len >> 8));
        out.push_back(static_cast<uint8_t>(len));
    } else {
        out.push_back(mt | 26u);
        out.push_back(static_cast<uint8_t>(len >> 24));
        out.push_back(static_cast<uint8_t>(len >> 16));
        out.push_back(static_cast<uint8_t>(len >> 8));
        out.push_back(static_cast<uint8_t>(len));
    }
}

inline void write_text(std::vector<uint8_t>& out, std::string_view s)
{
    write_len(out, 3, s.size());
    out.insert(out.end(), s.begin(), s.end());
}

inline void write_bytes(std::vector<uint8_t>& out, std::span<const uint8_t> b)
{
    write_len(out, 2, b.size());
    out.insert(out.end(), b.begin(), b.end());
}

inline void map_header(std::vector<uint8_t>& out, uint64_t count)
{
    write_len(out, 5, count);
}

inline void array_header(std::vector<uint8_t>& out, uint64_t count)
{
    write_len(out, 4, count);
}

// ─── Message encoders ─────────────────────────────────────────────────────────

// Encode a "join" message (client → server, starts the handshake).
inline std::vector<uint8_t> encode_join(std::string_view peer_id)
{
    std::vector<uint8_t> out;
    map_header(out, 4);
    write_text(out, "type");                   write_text(out, "join");
    write_text(out, "senderId");               write_text(out, peer_id);
    write_text(out, "peerMetadata");           map_header(out, 0);
    write_text(out, "supportedProtocolVersions");
    array_header(out, 1);                      write_text(out, "1");
    return out;
}

// Encode a "sync" message carrying raw automerge sync-protocol bytes.
inline std::vector<uint8_t> encode_sync(
    std::string_view sender_id,
    std::string_view target_id,
    std::string_view doc_id,
    std::span<const uint8_t> data)
{
    std::vector<uint8_t> out;
    map_header(out, 5);
    write_text(out, "type");       write_text(out, "sync");
    write_text(out, "senderId");   write_text(out, sender_id);
    write_text(out, "targetId");   write_text(out, target_id);
    write_text(out, "documentId"); write_text(out, doc_id);
    write_text(out, "data");       write_bytes(out, data);
    return out;
}

// ─── Decoder ──────────────────────────────────────────────────────────────────

struct Decoder
{
    const uint8_t* p;
    const uint8_t* end;

    explicit Decoder(std::span<const uint8_t> data) noexcept
        : p(data.data()), end(data.data() + data.size()) {}

    uint8_t peek() const
    {
        if (p >= end) throw std::runtime_error("CBOR: unexpected end");
        return *p;
    }

    uint8_t consume()
    {
        if (p >= end) throw std::runtime_error("CBOR: unexpected end");
        return *p++;
    }

    uint64_t read_additional(uint8_t add)
    {
        if (add <= 23) return add;
        if (add == 24) { return consume(); }
        if (add == 25) { uint64_t v = static_cast<uint64_t>(consume()) << 8; return v | consume(); }
        if (add == 26) {
            uint64_t v = static_cast<uint64_t>(consume()) << 24;
            v |= static_cast<uint64_t>(consume()) << 16;
            v |= static_cast<uint64_t>(consume()) << 8;
            return v | consume();
        }
        if (add == 27) { uint64_t v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | consume(); return v; }
        if (add == 31) return UINT64_MAX; // indefinite
        throw std::runtime_error("CBOR: unsupported additional info");
    }

    std::string read_text()
    {
        uint8_t b = consume();
        if ((b >> 5) != 3) throw std::runtime_error("CBOR: expected text");
        uint64_t len = read_additional(b & 0x1F);
        if (len == UINT64_MAX) throw std::runtime_error("CBOR: indefinite text not supported");
        if (p + len > end) throw std::runtime_error("CBOR: truncated text");
        std::string s(reinterpret_cast<const char*>(p), static_cast<size_t>(len));
        p += len;
        return s;
    }

    std::vector<uint8_t> read_bytes()
    {
        uint8_t b = consume();
        if ((b >> 5) != 2) throw std::runtime_error("CBOR: expected bytes");
        uint64_t len = read_additional(b & 0x1F);
        if (len == UINT64_MAX) throw std::runtime_error("CBOR: indefinite bytes not supported");
        if (p + len > end) throw std::runtime_error("CBOR: truncated bytes");
        std::vector<uint8_t> v(p, p + len);
        p += len;
        return v;
    }

    void skip_value()
    {
        uint8_t b = consume();
        uint8_t major = b >> 5;
        uint8_t add   = b & 0x1F;
        uint64_t len  = read_additional(add);
        bool indef    = (len == UINT64_MAX);
        switch (major) {
        case 0: case 1:
            break; // integer — additional bytes already consumed
        case 2: case 3:
            if (indef) { while (peek() != 0xFF) skip_value(); consume(); }
            else        { if (p + len > end) throw std::runtime_error("CBOR: truncated"); p += len; }
            break;
        case 4:
            for (uint64_t i = 0; indef ? peek() != 0xFF : i < len; i++) skip_value();
            if (indef) consume();
            break;
        case 5:
            for (uint64_t i = 0; indef ? peek() != 0xFF : i < len; i++) { skip_value(); skip_value(); }
            if (indef) consume();
            break;
        default:
            break; // float / simple / break
        }
    }

    Map decode_map()
    {
        Map result;
        uint8_t b   = consume();
        if ((b >> 5) != 5) throw std::runtime_error("CBOR: expected map");
        uint8_t add = b & 0x1F;
        uint64_t count = read_additional(add);
        bool indef  = (count == UINT64_MAX);

        for (uint64_t i = 0; indef ? peek() != 0xFF : i < count; i++) {
            if (peek() >> 5 != 3) { skip_value(); skip_value(); continue; }
            auto key = read_text();
            uint8_t vb = peek();
            if ((vb >> 5) == 3)      result[key] = read_text();
            else if ((vb >> 5) == 2) result[key] = read_bytes();
            else                     skip_value();
        }
        if (indef) consume(); // 0xFF break
        return result;
    }
};

// Parse CBOR bytes into a flat map of string→(string|bytes) fields.
// Returns {} on any error (tolerate malformed frames).
inline Map parse(std::span<const uint8_t> data) noexcept
{
    try {
        Decoder d(data);
        return d.decode_map();
    } catch (...) {
        return {};
    }
}

// Convenience accessors (return {} if key absent or wrong type).
inline std::string get_string(const Map& m, const std::string& key)
{
    auto it = m.find(key);
    if (it == m.end()) return {};
    if (auto* s = std::get_if<std::string>(&it->second)) return *s;
    return {};
}

inline std::vector<uint8_t> get_bytes(const Map& m, const std::string& key)
{
    auto it = m.find(key);
    if (it == m.end()) return {};
    if (auto* v = std::get_if<std::vector<uint8_t>>(&it->second)) return *v;
    return {};
}

} // namespace Cbor

#endif // AUTOMERGE_CBOR_HPP
