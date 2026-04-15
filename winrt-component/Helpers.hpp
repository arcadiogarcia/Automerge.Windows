// Helpers.hpp — IBuffer <-> std::vector<uint8_t> conversions for C++/WinRT.
#pragma once

#include "pch.h"

namespace AutomergeWinRT {

/// Convert an IBuffer to a vector<uint8_t>.
inline std::vector<uint8_t> ibuffer_to_bytes(
    winrt::Windows::Storage::Streams::IBuffer const& buf)
{
    if (!buf || buf.Length() == 0) return {};
    auto reader = winrt::Windows::Storage::Streams::DataReader::FromBuffer(buf);
    std::vector<uint8_t> result(buf.Length());
    reader.ReadBytes(result);
    return result;
}

/// Convert a vector<uint8_t> to an IBuffer.
inline winrt::Windows::Storage::Streams::IBuffer bytes_to_ibuffer(
    const std::vector<uint8_t>& bytes)
{
    winrt::Windows::Storage::Streams::DataWriter writer;
    writer.WriteBytes(bytes);
    return writer.DetachBuffer();
}

/// Overload that takes a span.
inline winrt::Windows::Storage::Streams::IBuffer bytes_to_ibuffer(
    std::span<const uint8_t> bytes)
{
    winrt::Windows::Storage::Streams::DataWriter writer;
    writer.WriteBytes({ bytes.data(), static_cast<uint32_t>(bytes.size()) });
    return writer.DetachBuffer();
}

/// Convert a winrt::hstring to std::string (UTF-8).
inline std::string hstring_to_string(winrt::hstring const& h) {
    return winrt::to_string(h);
}

/// Convert an std::string to winrt::hstring.
inline winrt::hstring string_to_hstring(const std::string& s) {
    return winrt::to_hstring(s);
}

/// Propagate a c++ exception as a WinRT error.
[[noreturn]] inline void throw_winrt_error(const std::exception& ex) {
    throw winrt::hresult_error(E_FAIL, winrt::to_hstring(ex.what()));
}

} // namespace AutomergeWinRT
