# automerge-windows CMake package config
#
# Provides two imported targets:
#
#   automerge::core    — the Rust native DLL (imported SHARED)
#   automerge::wrapper — the C++ wrapper static lib (imported STATIC)
#
# Typical usage:
#   find_package(automerge-windows CONFIG REQUIRED)
#   target_link_libraries(my_app PRIVATE automerge::wrapper)
#
# automerge_core.dll must be deployed alongside the application executable.
# With vcpkg + CMake integration this is handled automatically.

cmake_minimum_required(VERSION 3.25)

get_filename_component(_am_share_dir  "${CMAKE_CURRENT_LIST_DIR}" DIRECTORY)  # share/automerge-windows -> share
get_filename_component(_am_root       "${_am_share_dir}"          DIRECTORY)  # share -> install root

if(NOT TARGET automerge::core)
    add_library(automerge::core SHARED IMPORTED)
    set_target_properties(automerge::core PROPERTIES
        IMPORTED_LOCATION             "${_am_root}/bin/automerge_core.dll"
        IMPORTED_IMPLIB               "${_am_root}/lib/automerge_core.dll.lib"
        INTERFACE_INCLUDE_DIRECTORIES "${_am_root}/include"
    )
endif()

if(NOT TARGET automerge::wrapper)
    add_library(automerge::wrapper STATIC IMPORTED)
    set_target_properties(automerge::wrapper PROPERTIES
        IMPORTED_LOCATION             "${_am_root}/lib/automerge_wrapper.lib"
        INTERFACE_INCLUDE_DIRECTORIES "${_am_root}/include"
        INTERFACE_LINK_LIBRARIES      automerge::core
    )
endif()
