cmake_minimum_required(VERSION 3.25)

# ─── C++ tests ────────────────────────────────────────────────────────────────

# Fetch GoogleTest
include(FetchContent)
FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG        v1.14.0
    FIND_PACKAGE_ARGS NAMES GTest
)
FetchContent_MakeAvailable(googletest)

include(GoogleTest)

# ─── Test executable ──────────────────────────────────────────────────────────

add_executable(automerge_cpp_tests
    test_document.cpp
    test_sync.cpp
    test_changes.cpp
)

target_link_libraries(automerge_cpp_tests
    PRIVATE
        automerge_wrapper
        GTest::gtest_main
)

target_compile_features(automerge_cpp_tests PRIVATE cxx_std_20)

# Copy the Rust DLL next to the test executable
add_custom_command(TARGET automerge_cpp_tests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:automerge_core>"
        "$<TARGET_FILE_DIR:automerge_cpp_tests>"
    COMMENT "Copying automerge_core.dll to test directory"
)

gtest_discover_tests(automerge_cpp_tests)
