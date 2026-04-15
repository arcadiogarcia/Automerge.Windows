# cmake/toolchain-arm64-mingw.cmake
# ARM64 Windows toolchain using LLVM-MinGW (llvm-mingw-20260407-ucrt-aarch64).
# Usage:
#   cmake -DCMAKE_TOOLCHAIN_FILE=cmake/toolchain-arm64-mingw.cmake ..

set(LLVM_MINGW_ROOT "C:/llvm-mingw/llvm-mingw-20260407-ucrt-aarch64")
set(TRIPLE "aarch64-w64-mingw32")

set(CMAKE_SYSTEM_NAME      Windows)
set(CMAKE_SYSTEM_PROCESSOR aarch64)

set(CMAKE_C_COMPILER   "${LLVM_MINGW_ROOT}/bin/${TRIPLE}-clang.exe")
set(CMAKE_CXX_COMPILER "${LLVM_MINGW_ROOT}/bin/${TRIPLE}-clang++.exe")
set(CMAKE_RC_COMPILER  "${LLVM_MINGW_ROOT}/bin/${TRIPLE}-windres.exe")
set(CMAKE_AR           "${LLVM_MINGW_ROOT}/bin/llvm-ar.exe")
set(CMAKE_RANLIB       "${LLVM_MINGW_ROOT}/bin/llvm-ranlib.exe")

# MinGW doesn't use .lib import files; CMake must look for .dll.a files
set(CMAKE_FIND_LIBRARY_SUFFIXES ".dll.a;.a;.lib")

# Do not try to find libraries in host paths
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)
