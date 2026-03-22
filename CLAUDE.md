# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

QuicRemote is a QUIC-based remote desktop application supporting both remote office work and game streaming scenarios. It uses a layered architecture with WPF UI, C# business logic, and a C++ native DLL for high-performance media processing.

## Build Commands

```bash
# Build the entire solution
dotnet build QuicRemote.sln

# Build in Release mode
dotnet build QuicRemote.sln -c Release

# Build a specific project
dotnet build src/QuicRemote.Core/QuicRemote.Core.csproj

# Build the native DLL (Windows only, from src/QuicRemote.Native)
cd src/QuicRemote.Native && cmake -B build2 -DCMAKE_BUILD_TYPE=Release && cmake --build build2 --config Release
```

## Test Commands

```bash
# Run all tests
dotnet test QuicRemote.sln

# Run specific test project
dotnet test tests/QuicRemote.Core.Tests/QuicRemote.Core.Tests.csproj

# Run a single test class
dotnet test tests/QuicRemote.Core.Tests/QuicRemote.Core.Tests.csproj --filter "FullyQualifiedName~NativeMethodsTests"

# Run a specific test method
dotnet test tests/QuicRemote.Core.Tests/QuicRemote.Core.Tests.csproj --filter "FullyQualifiedName~NativeMethodsTests.GetVersion_ReturnsValidVersion"
```

## Run Applications

```bash
# Run Host (controlled device) - Windows only
dotnet run --project src/QuicRemote.Host/QuicRemote.Host.csproj

# Run Client (controller) - Windows only
dotnet run --project src/QuicRemote.Client/QuicRemote.Client.csproj
```

## Architecture

### Layered Structure

```
┌─────────────────────────────────────────────┐
│           WPF UI Layer (Host/Client)         │
├─────────────────────────────────────────────┤
│           C# Business Logic (Core)           │
│    Session │ Control │ Recovery │ Media     │
├─────────────────────────────────────────────┤
│           C# Network Layer (Network)         │
│         QUIC + Protocol Messages            │
├─────────────────────────────────────────────┤
│        C++ Native DLL (QuicRemote.Native)    │
│  Capture │ Encoder │ Decoder │ Input │ Audio│
└─────────────────────────────────────────────┘
```

### Project Dependencies

- **QuicRemote.Host** → QuicRemote.Core, QuicRemote.Network (WPF app for controlled device)
- **QuicRemote.Client** → QuicRemote.Core, QuicRemote.Network (WPF app for controller)
- **QuicRemote.Core** → System.IO.Pipelines (no network dependency)
- **QuicRemote.Network** → (standalone, QUIC protocol layer)
- **QuicRemote.Native** → C++ DLL for screen capture, encoding/decoding, input injection

### Key Namespaces

| Layer | Namespace | Purpose |
|-------|-----------|---------|
| Core | `QuicRemote.Core.Media` | P/Invoke bindings to Native DLL, wrappers for Capture/Encoder/Decoder/Input |
| Core | `QuicRemote.Core.Session` | Session state management, roles, permissions |
| Core | `QuicRemote.Core.Control` | Remote control service, coordinate mapping |
| Network | `QuicRemote.Network.Quic` | QUIC connection/stream wrappers |
| Network | `QuicRemote.Network.Protocol` | Message types, serialization with CRC32 validation |

## Critical Patterns

### QUIC Stream Behavior

**Important:** `AcceptInboundStreamAsync()` blocks until data is written to the stream. QUIC streams are not visible until they have data. Use `Task.Run` for parallel stream setup:

```csharp
// CORRECT - Parallel stream creation
var acceptTask = Task.Run(async () => await connection.AcceptStreamAsync());
var clientTask = Task.Run(async () => {
    await Task.Delay(200); // Let server start accepting
    var stream = await connection.OpenStreamAsync();
    await stream.WriteAsync(initialData); // Make stream visible
    return stream;
});
```

### QUIC Connection Configuration

Required settings for both client and server connections:
- `DefaultStreamErrorCode = 1` (must be > 0)
- `DefaultCloseErrorCode = 1` (must be > 0)
- `MaxInboundBidirectionalStreams = 10` (or higher)
- `MaxInboundUnidirectionalStreams = 10` (or higher)
- Application protocol: `"quicremote"`

### Message Protocol

All messages use a 12-byte header + payload + 4-byte CRC32:
- Magic: `0x5152` (2 bytes)
- Type: `MessageType` enum (1 byte)
- Flags: `MessageFlags` (1 byte)
- Sequence: uint32 (4 bytes)
- Length: uint32 (4 bytes)
- Payload: variable
- CRC32: uint32 (4 bytes)

### Native DLL Integration

The C++ native DLL (`QuicRemote.Native.dll`) must be present in the output directory. It provides:
- Screen capture via DXGI Desktop Duplication
- Hardware encoding (NVENC/AMF/QSV) and software encoding (FFmpeg optional)
- Hardware decoding (D3D11)
- Input injection (mouse/keyboard)
- Audio capture

The DLL is built via CMake and copied to test output directories automatically.

## .NET 8 Preview Features

The project uses `EnablePreviewFeatures>true` for QUIC support. Suppress warnings:
- `CA2252` - Preview features
- `CA1416` - Platform support

## Test Structure

- **QuicRemote.Core.Tests** - Native DLL integration, Encoder/Decoder, Input, Capture tests
- **QuicRemote.Network.Tests** - Protocol message serialization tests
- **QuicRemote.Integration.Tests** - QUIC connection/stream integration tests

Tests require the native DLL and may skip tests requiring a display device in headless environments.
