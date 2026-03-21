cmake_minimum_required(VERSION 3.20)
project(QuicRemote.Native VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(WIN32)
    add_definitions(-DWIN32_LEAN_AND_MEAN)
    add_definitions(-DNOMINMAX)
    add_definitions(-DQR_EXPORTS)
endif()

set(SOURCES
    src/dllmain.cpp
    src/exports.cpp
    src/capture.cpp
    src/encoder.cpp
    src/decoder.cpp
    src/input.cpp
    src/audio.cpp
)

set(HEADERS
    include/quicremote.h
)

add_library(QuicRemote.Native SHARED ${SOURCES} ${HEADERS})

target_include_directories(QuicRemote.Native
    PUBLIC include
    PRIVATE src
)

if(WIN32)
    target_link_libraries(QuicRemote.Native
        d3d11
        dxgi
        dxguid
        user32
    )
endif()

install(TARGETS QuicRemote.Native
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)

install(FILES include/quicremote.h DESTINATION include)
