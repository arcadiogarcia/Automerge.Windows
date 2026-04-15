cmake_minimum_required(VERSION 3.25)
project(AutomergeWrapper VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(automerge_wrapper STATIC
    src/Document.cpp
    src/SyncState.cpp
)

target_include_directories(automerge_wrapper
    PUBLIC
        include
        ../rust-core/include
)

target_compile_features(automerge_wrapper PUBLIC cxx_std_20)

# Link against automerge_core when used standalone
find_library(AUTOMERGE_CORE_LIB automerge_core
    HINTS ../rust-core/target/release
          ../rust-core/target/debug
)
if(AUTOMERGE_CORE_LIB)
    target_link_libraries(automerge_wrapper PUBLIC "${AUTOMERGE_CORE_LIB}")
endif()
