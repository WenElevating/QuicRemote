cmake_minimum_required(VERSION 3.20)
project(QuicRemote.Native VERSION 0.2.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 内部头文件
set(INTERNAL_HEADERS
    internal/capture.h
    internal/encoder.h
    internal/decoder.h
    internal/input.h
    internal/d3d_utils.h
)

set(SOURCES
    src/dllmain.cpp
    src/exports.cpp
    src/capture.cpp
    src/d3d_utils.cpp
    src/encoder.cpp
    src/decoder.cpp
    src/input.cpp
    src/audio.cpp
)

set(HEADERS
    include/quicremote.h
)

add_library(QuicRemote.Native SHARED ${SOURCES} ${HEADERS} ${INTERNAL_HEADERS})

if(WIN32)
    target_compile_definitions(QuicRemote.Native PRIVATE
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        QR_EXPORTS
    )
    # Use UTF-8 source and execution character set
    target_compile_options(QuicRemote.Native PRIVATE /utf-8)
endif()

target_include_directories(QuicRemote.Native
    PUBLIC include
    PRIVATE src
    PRIVATE internal
)

if(WIN32)
    target_link_libraries(QuicRemote.Native
        d3d11
        dxgi
        dxguid
        user32
        ole32
        oleaut32
    )
endif()

# FFmpeg 支持 (可选)
option(USE_FFMPEG "Use FFmpeg for software encoding/decoding" OFF)
if(USE_FFMPEG)
    find_package(FFMPEG REQUIRED)
    target_compile_definitions(QuicRemote.Native PRIVATE QR_USE_FFMPEG)
    target_include_directories(QuicRemote.Native PRIVATE ${FFMPEG_INCLUDE_DIRS})
    target_link_libraries(QuicRemote.Native ${FFMPEG_LIBRARIES})
endif()

# CUDA/NVENC 支持 (可选)
option(USE_NVENC "Use NVIDIA NVENC for hardware encoding" OFF)
if(USE_NVENC)
    find_package(CUDA REQUIRED)
    target_compile_definitions(QuicRemote.Native PRIVATE USE_NVENC)
    target_include_directories(QuicRemote.Native PRIVATE ${CUDA_INCLUDE_DIRS})
endif()

install(TARGETS QuicRemote.Native
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)

install(FILES include/quicremote.h DESTINATION include)

# 测试
option(BUILD_TESTS "Build unit tests" OFF)
if(BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()
