cmake_minimum_required(VERSION 3.25)
project(AutomergeWindows VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ─── Options ──────────────────────────────────────────────────────────────────

option(AUTOMERGE_BUILD_TESTS "Build C++ tests" ON)
set(AUTOMERGE_RUST_PROFILE "release" CACHE STRING "Cargo build profile: debug|release")

# ─── Paths ────────────────────────────────────────────────────────────────────

set(RUST_CORE_DIR    "${CMAKE_CURRENT_SOURCE_DIR}/rust-core")
set(CPP_WRAPPER_DIR  "${CMAKE_CURRENT_SOURCE_DIR}/cpp-wrapper")
set(TESTS_CPP_DIR    "${CMAKE_CURRENT_SOURCE_DIR}/tests/cpp")

# ─── Rust build ───────────────────────────────────────────────────────────────

find_program(CARGO_EXE cargo
    HINTS "C:/Users/arcad/.cargo/bin"
    REQUIRED
)

# Determine cargo build flags
if(AUTOMERGE_RUST_PROFILE STREQUAL "release")
    set(CARGO_FLAGS --release)
else()
    set(CARGO_FLAGS "")
endif()

# gnullvm toolchain output directory (default target for pinned toolchain)
set(RUST_OUT_DIR "${RUST_CORE_DIR}/target/${AUTOMERGE_RUST_PROFILE}")
set(AUTOMERGE_CORE_DLL "${RUST_OUT_DIR}/automerge_core.dll")
# MinGW-style import library (produced by gnullvm toolchain)
set(AUTOMERGE_CORE_IMPLIB "${RUST_OUT_DIR}/libautomerge_core.dll.a")

# Custom command to build the Rust crate
add_custom_command(
    OUTPUT  "${AUTOMERGE_CORE_DLL}" "${AUTOMERGE_CORE_IMPLIB}"
    COMMAND "${CARGO_EXE}" +stable-aarch64-pc-windows-gnullvm build ${CARGO_FLAGS}
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

