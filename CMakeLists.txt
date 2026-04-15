cmake_minimum_required(VERSION 3.25)
project(AutomergeWindows VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ─── Options ──────────────────────────────────────────────────────────────────

option(AUTOMERGE_BUILD_TESTS "Build C++ tests" ON)
set(AUTOMERGE_RUST_PROFILE "release" CACHE STRING "Cargo build profile: debug|release")
# Architecture: "x64" (default) or "arm64" (cross-compile from x64 host).
# When arm64 is selected the Rust DLL is built for aarch64-pc-windows-msvc and
# tests are disabled (ARM64 binaries cannot run on an x64 host).
set(AUTOMERGE_TARGET_ARCH "x64" CACHE STRING "Target architecture: x64 or arm64")

# ─── Paths ────────────────────────────────────────────────────────────────────

set(RUST_CORE_DIR    "${CMAKE_CURRENT_SOURCE_DIR}/rust-core")
set(CPP_WRAPPER_DIR  "${CMAKE_CURRENT_SOURCE_DIR}/cpp-wrapper")
set(TESTS_CPP_DIR    "${CMAKE_CURRENT_SOURCE_DIR}/tests/cpp")

# ─── Rust build ───────────────────────────────────────────────────────────────

find_program(CARGO_EXE cargo
    HINTS "C:/Users/arcad/.cargo/bin"
    REQUIRED
)

# Determine cargo build flags and output directory
if(AUTOMERGE_RUST_PROFILE STREQUAL "release")
    set(CARGO_FLAGS --release)
else()
    set(CARGO_FLAGS "")
endif()

if(AUTOMERGE_TARGET_ARCH STREQUAL "arm64")
    set(RUST_TARGET "aarch64-pc-windows-msvc")
    list(APPEND CARGO_FLAGS --target ${RUST_TARGET})
    set(RUST_OUT_DIR "${RUST_CORE_DIR}/target/${RUST_TARGET}/${AUTOMERGE_RUST_PROFILE}")
    # ARM64 binaries can't run on the x64 build host; disable tests
    set(AUTOMERGE_BUILD_TESTS OFF)
    message(STATUS "ARM64 cross-compilation: tests disabled (cannot run on host)")
else()
    set(RUST_TARGET "x86_64-pc-windows-msvc")
    set(RUST_OUT_DIR "${RUST_CORE_DIR}/target/${AUTOMERGE_RUST_PROFILE}")
endif()

set(AUTOMERGE_CORE_DLL     "${RUST_OUT_DIR}/automerge_core.dll")
# MSVC-style import library (produced by the msvc toolchain)
set(AUTOMERGE_CORE_IMPLIB  "${RUST_OUT_DIR}/automerge_core.dll.lib")

# Custom command to build the Rust crate
add_custom_command(
    OUTPUT  "${AUTOMERGE_CORE_DLL}" "${AUTOMERGE_CORE_IMPLIB}"
    COMMAND "${CARGO_EXE}" build ${CARGO_FLAGS}
    WORKING_DIRECTORY "${RUST_CORE_DIR}"
    COMMENT "Building Rust automerge_core..."
    VERBATIM
)

add_custom_target(automerge_rust_build ALL
    DEPENDS "${AUTOMERGE_CORE_DLL}" "${AUTOMERGE_CORE_IMPLIB}"
)

# Import the Rust DLL as a CMake imported library
add_library(automerge_core SHARED IMPORTED GLOBAL)
set_target_properties(automerge_core PROPERTIES
    IMPORTED_LOCATION            "${AUTOMERGE_CORE_DLL}"
    IMPORTED_IMPLIB              "${AUTOMERGE_CORE_IMPLIB}"
    INTERFACE_INCLUDE_DIRECTORIES "${RUST_CORE_DIR}/include"
)
add_dependencies(automerge_core automerge_rust_build)

# ─── C++ wrapper library ──────────────────────────────────────────────────────

add_library(automerge_wrapper STATIC
    "${CPP_WRAPPER_DIR}/src/Document.cpp"
    "${CPP_WRAPPER_DIR}/src/SyncState.cpp"
)

target_include_directories(automerge_wrapper
    PUBLIC
        "${CPP_WRAPPER_DIR}/include"
        "${RUST_CORE_DIR}/include"
)

target_link_libraries(automerge_wrapper
    PUBLIC automerge_core
)

target_compile_features(automerge_wrapper PUBLIC cxx_std_20)

# ─── Testing ───────────────────────────────────────────────────────────────────

if(AUTOMERGE_BUILD_TESTS)
    enable_testing()
    add_subdirectory("${TESTS_CPP_DIR}")
endif()

