# automerge-windows vcpkg port
#
# Pre-built binary port — downloads the release ZIP from GitHub Releases.
# SHAs and version are updated automatically by the release workflow on each tag.
#
# USAGE (overlay port — add to your CMake preset or command line):
#   vcpkg install automerge-windows --overlay-ports=<repo>/ports
# or in vcpkg-configuration.json:
#   "overlay-ports": ["<repo>/ports"]

vcpkg_check_linkage(ONLY_DYNAMIC_LIBRARY)

# ─── Architecture ─────────────────────────────────────────────────────────────
if(VCPKG_TARGET_ARCHITECTURE STREQUAL "x64")
    set(ZIP_ARCH   "x64")
    set(ZIP_SHA512 "6c68e825bffd453c9b8acd993dc35e8d3e42c0ea21b9d0025a0e9853b76a3af86ba3f49bacb3e84205d213386dfa80ee6ccb4c9f2939fb610a53b46ee030da73")
elseif(VCPKG_TARGET_ARCHITECTURE STREQUAL "arm64")
    set(ZIP_ARCH   "arm64")
    set(ZIP_SHA512 "2181840b93ee32da96214eb5dc6dae9f30789018780554b6be74d36eaa585ee2167968bd5fedf74429271e5b08de4624fc74b3fe903598a508e3c2f4467f4ba6")
else()
    message(FATAL_ERROR "${PORT}: unsupported architecture '${VCPKG_TARGET_ARCHITECTURE}'. Only x64 and arm64 are supported on Windows.")
endif()

# ─── Version (bumped by release workflow) ─────────────────────────────────────
set(AUTOMERGE_VERSION "0.6.1")

# ─── Download ─────────────────────────────────────────────────────────────────
vcpkg_download_distfile(ARCHIVE
    URLS     "https://github.com/arcadiogarcia/Automerge.Windows/releases/download/v${AUTOMERGE_VERSION}/automerge-cpp-${ZIP_ARCH}-${AUTOMERGE_VERSION}.zip"
    FILENAME "automerge-cpp-${ZIP_ARCH}-${AUTOMERGE_VERSION}.zip"
    SHA512   ${ZIP_SHA512}
)

vcpkg_extract_source_archive(
    SOURCE_PATH
    ARCHIVE "${ARCHIVE}"
    NO_REMOVE_ONE_LEVEL
)

# ─── Install ──────────────────────────────────────────────────────────────────

# Headers: automerge_core.h  +  automerge/ (C++ wrapper headers)
file(INSTALL "${SOURCE_PATH}/include/"
    DESTINATION "${CURRENT_PACKAGES_DIR}/include")

# Import library for dynamic Rust DLL
file(INSTALL "${SOURCE_PATH}/automerge_core.dll.lib"
    DESTINATION "${CURRENT_PACKAGES_DIR}/lib")

# Static C++ wrapper (links to automerge_core.dll via the import lib above)
file(INSTALL "${SOURCE_PATH}/automerge_wrapper.lib"
    DESTINATION "${CURRENT_PACKAGES_DIR}/lib")

# Runtime DLL
file(INSTALL "${SOURCE_PATH}/automerge_core.dll"
    DESTINATION "${CURRENT_PACKAGES_DIR}/bin")

# ─── CMake package config ─────────────────────────────────────────────────────
file(INSTALL "${CMAKE_CURRENT_LIST_DIR}/automerge-windows-config.cmake"
    DESTINATION "${CURRENT_PACKAGES_DIR}/share/${PORT}")

file(WRITE "${CURRENT_PACKAGES_DIR}/share/${PORT}/copyright"
    "Copyright (c) Arcadio Garcia. MIT License.\nhttps://github.com/arcadiogarcia/Automerge.Windows\n")

# Allow static wrapper lib alongside dynamic core
set(VCPKG_POLICY_ALLOW_STATIC_LIBRARY enabled)







