// pch.h — precompiled header for the WinRT projection component.
#pragma once

// Windows / WinRT
// (NOMINMAX and WIN32_LEAN_AND_MEAN are injected by the build system via /D flags)
#include <windows.h>

// C++/WinRT core
#include <unknwn.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.Streams.h>

// Bring the entire winrt namespace into scope — standard C++/WinRT component
// practice so that component headers can use Windows::... shorthand.
using namespace winrt;

// C++ standard library
#include <array>
#include <cstdint>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

// Forward declaration of C++ wrapper types used throughout.
#include "../cpp-wrapper/include/automerge.hpp"
