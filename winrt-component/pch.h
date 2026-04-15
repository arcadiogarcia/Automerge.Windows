// pch.h — precompiled header for the WinRT projection component.
#pragma once

// Windows / WinRT
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// C++/WinRT core
#include <unknwn.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.Streams.h>

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
