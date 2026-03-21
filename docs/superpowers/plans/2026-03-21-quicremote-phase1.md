# QuicRemote Phase 1: 基础框架 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭建 QuicRemote 项目的基础框架，包括 C++ Native DLL 基础结构、MsQuic 网络层封装、基础消息协议。

**Architecture:** 分层单体架构，C++ Native DLL 提供高性能媒体管道，C# 层负责网络通信和业务逻辑，通过 P/Invoke 跨语言调用。

**Tech Stack:** C++20, C#/.NET 8, MsQuic, CMake, Visual Studio 2022

**Spec Reference:** `docs/superpowers/specs/2026-03-21-quicremote-design.md`

---

## 文件结构规划

```
QuicRemote/
├── src/
│   ├── QuicRemote.Native/
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── quicremote.h           # C API 头文件
│   │   ├── src/
│   │   │   ├── dllmain.cpp
│   │   │   ├── capture.cpp            # 屏幕捕获
│   │   │   ├── encoder.cpp            # 编码器
│   │   │   ├── decoder.cpp            # 解码器
│   │   │   ├── input.cpp              # 输入注入
│   │   │   ├── audio.cpp              # 音频
│   │   │   └── exports.cpp            # API 导出
│   │   └── tests/
│   │       └── native_tests.cpp
│   │
│   ├── QuicRemote.Network/
│   │   ├── QuicRemote.Network.csproj
│   │   ├── Quic/
│   │   │   ├── QuicConnection.cs
│   │   │   ├── QuicListener.cs
│   │   │   └── QuicStream.cs
│   │   ├── Protocol/
│   │   │   ├── Message.cs
│   │   │   ├── MessageSerializer.cs
│   │   │   └── MessageTypes.cs
│   │   └── P2P/
│   │       └── StunClient.cs
│   │
│   ├── QuicRemote.Core/
│   │   ├── QuicRemote.Core.csproj
│   │   ├── Session/
│   │   │   └── SessionManager.cs
│   │   └── Auth/
│   │       └── AuthService.cs
│   │
│   ├── QuicRemote.Host/
│   │   ├── QuicRemote.Host.csproj
│   │   ├── App.xaml
│   │   └── App.xaml.cs
│   │
│   └── QuicRemote.Client/
│       ├── QuicRemote.Client.csproj
│       ├── App.xaml
│       └── App.xaml.cs
│
├── tests/
│   ├── QuicRemote.Network.Tests/
│   └── QuicRemote.Core.Tests/
│
├── Directory.Build.props
└── Directory.Build.targets
```

---

## Task 1: 项目脚手架搭建

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Build.targets`
- Create: `src/Directory.sln`

### 1.1 创建目录结构

- [ ] **Step 1: 创建基础目录结构**

```bash
mkdir -p src/QuicRemote.Native/{include,src,tests}
mkdir -p src/QuicRemote.Network/{Quic,Protocol,P2P}
mkdir -p src/QuicRemote.Core/{Session,Auth}
mkdir -p src/QuicRemote.Host
mkdir -p src/QuicRemote.Client
mkdir -p tests/QuicRemote.Network.Tests
mkdir -p tests/QuicRemote.Core.Tests
```

- [ ] **Step 2: 创建 Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <PropertyGroup>
    <Company>QuicRemote</Company>
    <Product>QuicRemote</Product>
    <Version>0.1.0</Version>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: 创建 .gitignore**

```
# Build results
[Dd]ebug/
[Rr]elease/
x64/
x86/
[Bb]in/
[Oo]bj/

# Visual Studio
.vs/
*.user
*.suo
*.vcxproj.filters

# JetBrains Rider
.idea/
*.DotSettings

# VS Code
.vscode/

# CMake
CMakeCache.txt
CMakeFiles/
cmake_install.cmake

# Native
*.dll
*.lib
*.exp
*.pdb
```

- [ ] **Step 4: 提交**

```bash
git add .gitignore Directory.Build.props Directory.Build.targets
git commit -m "chore: add project scaffolding and build configuration"
```

---

## Task 2: C++ Native DLL 基础结构

**Files:**
- Create: `src/QuicRemote.Native/CMakeLists.txt`
- Create: `src/QuicRemote.Native/include/quicremote.h`
- Create: `src/QuicRemote.Native/src/dllmain.cpp`
- Create: `src/QuicRemote.Native/src/exports.cpp`

### 2.1 定义 C API 头文件

- [ ] **Step 1: 创建头文件基础结构**

```cpp
// src/QuicRemote.Native/include/quicremote.h
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// 版本信息
#define QR_VERSION_MAJOR 0
#define QR_VERSION_MINOR 1
#define QR_VERSION_PATCH 0

// 导出宏
#ifdef _WIN32
    #ifdef QR_EXPORTS
        #define QR_API __declspec(dllexport)
    #else
        #define QR_API __declspec(dllimport)
    #endif
#else
    #define QR_API
#endif

// 错误码定义
typedef enum QR_Result {
    // 成功
    QR_Success = 0,

    // 通用错误 (-1 ~ -99)
    QR_Error_Unknown = -1,
    QR_Error_InvalidParam = -2,
    QR_Error_OutOfMemory = -3,
    QR_Error_NotInitialized = -4,
    QR_Error_AlreadyInitialized = -5,
    QR_Error_Timeout = -6,
    QR_Error_OperationCancelled = -7,

    // 设备错误 (-100 ~ -199)
    QR_Error_DeviceNotFound = -100,
    QR_Error_DeviceLost = -101,
    QR_Error_DeviceBusy = -102,

    // 编码器错误 (-200 ~ -299)
    QR_Error_EncoderNotSupported = -200,
    QR_Error_EncoderCreateFailed = -201,
    QR_Error_EncoderEncodeFailed = -202,
    QR_Error_EncoderReconfigureFailed = -203,

    // 解码器错误 (-300 ~ -399)
    QR_Error_DecoderNotSupported = -300,
    QR_Error_DecoderCreateFailed = -301,
    QR_Error_DecoderDecodeFailed = -302,

    // 捕获错误 (-400 ~ -499)
    QR_Error_CaptureFailed = -400,
    QR_Error_CaptureAccessDenied = -401,
    QR_Error_CaptureDesktopSwitched = -402,

    // 输入错误 (-500 ~ -599)
    QR_Error_InputAccessDenied = -500,
    QR_Error_InputBlocked = -501,

    // 音频错误 (-600 ~ -699)
    QR_Error_AudioDeviceNotFound = -600,
    QR_Error_AudioCaptureFailed = -601,
} QR_Result;

// 版本函数
QR_API uint32_t QR_GetVersion(void);
QR_API const char* QR_GetVersionString(void);
QR_API const char* QR_GetErrorDescription(QR_Result result);

// 初始化/销毁
typedef struct QR_Config {
    int log_level;
    int max_frame_pool_size;
} QR_Config;

QR_API QR_Result QR_Init(QR_Config* config);
QR_API QR_Result QR_Shutdown(void);

#ifdef __cplusplus
}
#endif
```

- [ ] **Step 2: 创建 CMakeLists.txt**

```cmake
# src/QuicRemote.Native/CMakeLists.txt
cmake_minimum_required(VERSION 3.20)
project(QuicRemote.Native VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Windows 特定设置
if(WIN32)
    add_definitions(-DWIN32_LEAN_AND_MEAN)
    add_definitions(-DNOMINMAX)
    add_definitions(-DQR_EXPORTS)
endif()

# 源文件
set(SOURCES
    src/dllmain.cpp
    src/exports.cpp
    src/capture.cpp
    src/encoder.cpp
    src/decoder.cpp
    src/input.cpp
    src/audio.cpp
)

# 头文件
set(HEADERS
    include/quicremote.h
)

# 创建 DLL
add_library(QuicRemote.Native SHARED ${SOURCES} ${HEADERS})

target_include_directories(QuicRemote.Native
    PUBLIC include
    PRIVATE src
)

# 链接 Windows 库
if(WIN32)
    target_link_libraries(QuicRemote.Native
        d3d11
        dxgi
        dxguid
        user32
    )
endif()

# 安装
install(TARGETS QuicRemote.Native
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)

install(FILES include/quicremote.h DESTINATION include)
```

- [ ] **Step 3: 创建 dllmain.cpp**

```cpp
// src/QuicRemote.Native/src/dllmain.cpp
#include <windows.h>

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        break;
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
```

- [ ] **Step 4: 创建 exports.cpp 基础实现**

```cpp
// src/QuicRemote.Native/src/exports.cpp
#include "quicremote.h"
#include <atomic>
#include <string>

namespace {
    std::atomic<bool> g_initialized{false};
    std::string g_versionString;
}

extern "C" {

QR_API uint32_t QR_GetVersion(void)
{
    return (QR_VERSION_MAJOR << 16) | (QR_VERSION_MINOR << 8) | QR_VERSION_PATCH;
}

QR_API const char* QR_GetVersionString(void)
{
    if (g_versionString.empty())
    {
        g_versionString = std::to_string(QR_VERSION_MAJOR) + "." +
                          std::to_string(QR_VERSION_MINOR) + "." +
                          std::to_string(QR_VERSION_PATCH);
    }
    return g_versionString.c_str();
}

QR_API const char* QR_GetErrorDescription(QR_Result result)
{
    switch (result)
    {
    case QR_Success: return "Success";
    case QR_Error_Unknown: return "Unknown error";
    case QR_Error_InvalidParam: return "Invalid parameter";
    case QR_Error_OutOfMemory: return "Out of memory";
    case QR_Error_NotInitialized: return "Not initialized";
    case QR_Error_AlreadyInitialized: return "Already initialized";
    case QR_Error_Timeout: return "Operation timeout";
    case QR_Error_OperationCancelled: return "Operation cancelled";
    case QR_Error_DeviceNotFound: return "Device not found";
    case QR_Error_DeviceLost: return "Device lost";
    case QR_Error_DeviceBusy: return "Device busy";
    case QR_Error_EncoderNotSupported: return "Encoder not supported";
    case QR_Error_EncoderCreateFailed: return "Failed to create encoder";
    case QR_Error_EncoderEncodeFailed: return "Encoding failed";
    case QR_Error_EncoderReconfigureFailed: return "Failed to reconfigure encoder";
    case QR_Error_DecoderNotSupported: return "Decoder not supported";
    case QR_Error_DecoderCreateFailed: return "Failed to create decoder";
    case QR_Error_DecoderDecodeFailed: return "Decoding failed";
    case QR_Error_CaptureFailed: return "Capture failed";
    case QR_Error_CaptureAccessDenied: return "Capture access denied";
    case QR_Error_CaptureDesktopSwitched: return "Desktop switched";
    case QR_Error_InputAccessDenied: return "Input access denied";
    case QR_Error_InputBlocked: return "Input blocked";
    case QR_Error_AudioDeviceNotFound: return "Audio device not found";
    case QR_Error_AudioCaptureFailed: return "Audio capture failed";
    default: return "Unknown error code";
    }
}

QR_API QR_Result QR_Init(QR_Config* config)
{
    if (g_initialized.exchange(true))
    {
        return QR_Error_AlreadyInitialized;
    }

    if (!config)
    {
        return QR_Error_InvalidParam;
    }

    // TODO: 初始化各模块
    return QR_Success;
}

QR_API QR_Result QR_Shutdown(void)
{
    if (!g_initialized.exchange(false))
    {
        return QR_Error_NotInitialized;
    }

    // TODO: 清理各模块
    return QR_Success;
}

} // extern "C"
```

- [ ] **Step 5: 创建占位实现文件**

> **注意:** 这些文件是 Phase 1 的空桩实现，用于建立项目结构。具体功能将在 Phase 2 实现。

```cpp
// src/QuicRemote.Native/src/capture.cpp
#include "quicremote.h"

// Phase 2: 实现屏幕捕获 (DXGI Desktop Duplication)

// src/QuicRemote.Native/src/encoder.cpp
#include "quicremote.h"

// Phase 2: 实现编码器 (NVENC/AMF/QSV)

// src/QuicRemote.Native/src/decoder.cpp
#include "quicremote.h"

// Phase 2: 实现解码器 (NVDEC/D3D11)

// src/QuicRemote.Native/src/input.cpp
#include "quicremote.h"

// Phase 2: 实现输入注入 (SendInput API)

// src/QuicRemote.Native/src/audio.cpp
#include "quicremote.h"

// Phase 3: 实现音频捕获 (WASAPI)
```

- [ ] **Step 6: 提交**

```bash
git add src/QuicRemote.Native/
git commit -m "feat(native): add C++ Native DLL base structure with C API"
```

---

## Task 3: MsQuic 网络层封装

**Files:**
- Create: `src/QuicRemote.Network/QuicRemote.Network.csproj`
- Create: `src/QuicRemote.Network/Quic/QuicConnection.cs`
- Create: `src/QuicRemote.Network/Quic/QuicListener.cs`
- Create: `src/QuicRemote.Network/Quic/QuicStream.cs`

### 3.1 创建网络层项目

- [ ] **Step 1: 创建项目文件**

```xml
<!-- src/QuicRemote.Network/QuicRemote.Network.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.NativeAOT" Version="8.0.0" />
    <PackageReference Include="System.IO.Pipelines" Version="8.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 创建 QuicConnection 封装**

```csharp
// src/QuicRemote.Network/Quic/QuicConnection.cs
using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace QuicRemote.Network.Quic;

/// <summary>
/// QUIC 连接封装
/// </summary>
public sealed class QuicConnection : IAsyncDisposable
{
    private readonly System.Net.Quic.QuicConnection _connection;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;
    public bool IsConnected => _connection.Connected;
    public TimeSpan RoundTripTime => _connection.Statistics.Rtt;

    private QuicConnection(System.Net.Quic.QuicConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// 作为客户端连接到服务器
    /// </summary>
    public static async Task<QuicConnection> ConnectAsync(
        IPEndPoint endpoint,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        var clientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            TargetHost = serverName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true // 开发环境跳过验证
        };

        var connectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = endpoint,
            ClientAuthenticationOptions = clientAuthenticationOptions,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 10,
            IdleTimeout = TimeSpan.FromMinutes(5),
            KeepAliveInterval = TimeSpan.FromSeconds(5)
        };

        var connection = await System.Net.Quic.QuicConnection.ConnectAsync(
            connectionOptions, cancellationToken);

        return new QuicConnection(connection);
    }

    /// <summary>
    /// 打开双向流
    /// </summary>
    public async Task<QuicStream> OpenStreamAsync(CancellationToken cancellationToken = default)
    {
        var stream = await _connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional, cancellationToken);

        return new QuicStream(stream);
    }

    /// <summary>
    /// 接受入站流
    /// </summary>
    public async Task<QuicStream> AcceptStreamAsync(CancellationToken cancellationToken = default)
    {
        var stream = await _connection.AcceptInboundStreamAsync(cancellationToken);
        return new QuicStream(stream);
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public async Task CloseAsync(int errorCode = 0, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _connection.CloseAsync(errorCode, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        await _connection.DisposeAsync();
        _cts.Dispose();
    }
}
```

- [ ] **Step 3: 创建 QuicListener 封装**

```csharp
// src/QuicRemote.Network/Quic/QuicListener.cs
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace QuicRemote.Network.Quic;

/// <summary>
/// QUIC 监听器封装
/// </summary>
public sealed class QuicListener : IAsyncDisposable
{
    private readonly System.Net.Quic.QuicListener _listener;
    private bool _disposed;

    public IPEndPoint LocalEndPoint => _listener.LocalEndPoint;

    private QuicListener(System.Net.Quic.QuicListener listener)
    {
        _listener = listener;
    }

    /// <summary>
    /// 创建监听器
    /// </summary>
    public static async Task<QuicListener> CreateAsync(
        IPEndPoint listenEndpoint,
        X509Certificate2? certificate = null,
        CancellationToken cancellationToken = default)
    {
        certificate ??= GenerateSelfSignedCertificate();

        var serverAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol("quicremote")
            },
            ServerCertificate = certificate
        };

        var listenerOptions = new QuicListenerOptions
        {
            ListenEndPoint = listenEndpoint,
            ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol("quicremote")
            },
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = serverAuthenticationOptions,
                    DefaultStreamErrorCode = 0,
                    DefaultCloseErrorCode = 0,
                    MaxInboundUnidirectionalStreams = 10,
                    MaxInboundBidirectionalStreams = 10,
                    IdleTimeout = TimeSpan.FromMinutes(5)
                })
        };

        var listener = await System.Net.Quic.QuicListener.ListenAsync(
            listenerOptions, cancellationToken);

        return new QuicListener(listener);
    }

    /// <summary>
    /// 接受连接
    /// </summary>
    public async Task<QuicConnection> AcceptConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _listener.AcceptConnectionAsync(cancellationToken);
        return new QuicConnection(connection);
    }

    /// <summary>
    /// 生成自签名证书（用于开发）
    /// </summary>
    private static X509Certificate2 GenerateSelfSignedCertificate()
    {
        // 使用预生成的开发证书或运行时生成
        // 开发阶段：从嵌入式资源加载预生成证书
        // 生产环境：使用 CA 签发的证书

        var assembly = typeof(QuicListener).Assembly;
        var resourceName = "QuicRemote.Network.Certificates.dev-cert.pfx";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            return new X509Certificate2(bytes, "quicremote");
        }

        // 如果没有预生成证书，运行时生成（仅限开发环境）
        return GenerateDevelopmentCertificate();
    }

    private static X509Certificate2 GenerateDevelopmentCertificate()
    {
        // 使用 System.Security.Cryptography 生成自签名证书
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=QuicRemote-Dev",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Auth
                false));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return new X509Certificate2(
            certificate.Export(X509ContentType.Pfx, "quicremote"),
            "quicremote");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _listener.DisposeAsync();
    }
}
```

- [ ] **Step 4: 创建 QuicStream 封装**

```csharp
// src/QuicRemote.Network/Quic/QuicStream.cs
using System.Buffers;
using System.IO.Pipelines;

namespace QuicRemote.Network.Quic;

/// <summary>
/// QUIC 流封装
/// </summary>
public sealed class QuicStream : IAsyncDisposable
{
    private readonly System.Net.Quic.QuicStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private bool _disposed;

    public long StreamId => _stream.Id;
    public QuicStreamType StreamType => _stream.Type;
    public bool CanRead => _stream.CanRead;
    public bool CanWrite => _stream.CanWrite;

    internal QuicStream(System.Net.Quic.QuicStream stream)
    {
        _stream = stream;
        _reader = PipeReader.Create(stream);
        _writer = PipeWriter.Create(stream);
    }

    /// <summary>
    /// 读取数据
    /// </summary>
    public async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        return await _reader.ReadAsync(cancellationToken);
    }

    /// <summary>
    /// 标记已读取
    /// </summary>
    public void AdvanceTo(SequencePosition consumed)
    {
        _reader.AdvanceTo(consumed);
    }

    public void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        _reader.AdvanceTo(consumed, examined);
    }

    /// <summary>
    /// 写入数据
    /// </summary>
    public async ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _writer.WriteSpan(buffer.Span);
        return await _writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 写入并完成
    /// </summary>
    public async ValueTask CompleteWriteAsync(CancellationToken cancellationToken = default)
    {
        await _writer.CompleteAsync();
    }

    /// <summary>
    /// 完成流
    /// </summary>
    public async ValueTask CompleteAsync(int errorCode = 0)
    {
        await _reader.CompleteAsync();
        await _writer.CompleteAsync();
        _stream.Abort(errorCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _reader.CompleteAsync();
        await _writer.CompleteAsync();
        await _stream.DisposeAsync();
    }
}
```

- [ ] **Step 5: 提交**

```bash
git add src/QuicRemote.Network/
git commit -m "feat(network): add MsQuic connection, listener and stream wrappers"
```

---

## Task 4: 基础消息协议

**Files:**
- Create: `src/QuicRemote.Network/Protocol/Message.cs`
- Create: `src/QuicRemote.Network/Protocol/MessageSerializer.cs`
- Create: `src/QuicRemote.Network/Protocol/MessageTypes.cs`

### 4.1 定义消息结构

- [ ] **Step 1: 创建消息基类**

```csharp
// src/QuicRemote.Network/Protocol/Message.cs
using System.Buffers.Binary;

namespace QuicRemote.Network.Protocol;

/// <summary>
/// 消息魔数
/// </summary>
public const ushort MessageMagic = 0x5152; // "QR"

/// <summary>
/// 消息标志位
/// </summary>
[Flags]
public enum MessageFlags : byte
{
    None = 0,
    Compressed = 1 << 0,
    Encrypted = 1 << 1,
    Fragmented = 1 << 2,
    LastFragment = 1 << 3
}

/// <summary>
/// 消息基类
/// </summary>
public abstract class Message
{
    /// <summary>
    /// 消息类型
    /// </summary>
    public abstract MessageType Type { get; }

    /// <summary>
    /// 标志位
    /// </summary>
    public MessageFlags Flags { get; set; }

    /// <summary>
    /// 序列号
    /// </summary>
    public uint SequenceNumber { get; set; }

    /// <summary>
    /// 序列化为字节数组
    /// </summary>
    public byte[] Serialize()
    {
        var payload = SerializePayload();
        var buffer = new byte[16 + payload.Length + 4]; // header(12) + len(4) + payload + crc32(4)
        var span = buffer.AsSpan();

        // Header
        BinaryPrimitives.WriteUInt16BigEndian(span, MessageMagic);
        span[2] = (byte)Type;
        span[3] = (byte)Flags;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4), SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8), (uint)payload.Length);

        // Payload
        payload.CopyTo(span.Slice(12));

        // CRC32
        var crc = CalculateCrc32(buffer.AsSpan(0, 12 + payload.Length));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12 + payload.Length), crc);

        return buffer;
    }

    /// <summary>
    /// 序列化载荷（子类实现）
    /// </summary>
    protected abstract byte[] SerializePayload();

    /// <summary>
    /// 计算 CRC32
    /// </summary>
    private static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 & (-(crc & 1)));
            }
        }
        return ~crc;
    }
}

/// <summary>
/// 消息类型
/// </summary>
public enum MessageType : byte
{
    // 会话控制
    SessionRequest = 0x01,
    SessionAccept = 0x02,
    SessionReject = 0x03,
    SessionEnd = 0x04,
    Heartbeat = 0x05,

    // 媒体控制
    VideoConfig = 0x10,
    VideoFrame = 0x11,
    AudioConfig = 0x12,
    AudioData = 0x13,

    // 输入控制
    MouseEvent = 0x20,
    KeyboardEvent = 0x21,
    ClipboardSync = 0x22,

    // 文件传输
    FileTransferRequest = 0x30,
    FileData = 0x31,
    FileAck = 0x32,

    // 聊天
    ChatMessage = 0x40
}
```

- [ ] **Step 2: 创建消息序列化器**

```csharp
// src/QuicRemote.Network/Protocol/MessageSerializer.cs
using System.Buffers.Binary;

namespace QuicRemote.Network.Protocol;

/// <summary>
/// 消息序列化器
/// </summary>
public static class MessageSerializer
{
    /// <summary>
    /// 反序列化消息
    /// </summary>
    public static MessageDeserializeResult Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
        {
            return MessageDeserializeResult.InsufficientData(16);
        }

        // 验证魔数
        var magic = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (magic != MessageMagic)
        {
            return MessageDeserializeResult.Error("Invalid magic number");
        }

        // 解析头部
        var type = (MessageType)data[2];
        var flags = (MessageFlags)data[3];
        var sequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4));
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8));

        var totalLength = 12 + (int)payloadLength + 4;
        if (data.Length < totalLength)
        {
            return MessageDeserializeResult.InsufficientData(totalLength);
        }

        // 验证 CRC32
        var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(12 + (int)payloadLength));
        var actualCrc = CalculateCrc32(data.Slice(0, 12 + (int)payloadLength));
        if (expectedCrc != actualCrc)
        {
            return MessageDeserializeResult.Error("CRC32 mismatch");
        }

        // 提取载荷
        var payload = data.Slice(12, (int)payloadLength).ToArray();

        var message = CreateMessage(type, payload);
        if (message == null)
        {
            return MessageDeserializeResult.Error($"Unknown message type: {type}");
        }

        message.Flags = flags;
        message.SequenceNumber = sequenceNumber;

        return MessageDeserializeResult.Success(message, totalLength);
    }

    private static Message? CreateMessage(MessageType type, byte[] payload)
    {
        return type switch
        {
            MessageType.SessionRequest => SessionRequestMessage.Deserialize(payload),
            MessageType.Heartbeat => new HeartbeatMessage(),
            // TODO: 实现其他消息类型
            _ => null
        };
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 & (-(crc & 1)));
            }
        }
        return ~crc;
    }
}

/// <summary>
/// 消息反序列化结果
/// </summary>
public readonly struct MessageDeserializeResult
{
    public bool Success { get; }
    public Message? Message { get; }
    public int BytesConsumed { get; }
    public int BytesRequired { get; }
    public string? Error { get; }

    private MessageDeserializeResult(bool success, Message? message, int bytesConsumed, int bytesRequired, string? error)
    {
        Success = success;
        Message = message;
        BytesConsumed = bytesConsumed;
        BytesRequired = bytesRequired;
        Error = error;
    }

    public static MessageDeserializeResult Success(Message message, int bytesConsumed)
        => new(true, message, bytesConsumed, 0, null);

    public static MessageDeserializeResult InsufficientData(int bytesRequired)
        => new(false, null, 0, bytesRequired, null);

    public static MessageDeserializeResult Error(string error)
        => new(false, null, 0, 0, error);
}
```

- [ ] **Step 3: 创建具体消息类型**

```csharp
// src/QuicRemote.Network/Protocol/MessageTypes.cs
using System.Buffers.Binary;
using System.Text;

namespace QuicRemote.Network.Protocol;

/// <summary>
/// 会话请求消息
/// </summary>
public class SessionRequestMessage : Message
{
    public override MessageType Type => MessageType.SessionRequest;
    public string DeviceId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    protected override byte[] SerializePayload()
    {
        var deviceIdBytes = Encoding.UTF8.GetBytes(DeviceId);
        var buffer = new byte[8 + 4 + deviceIdBytes.Length + 4 + Nonce.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteInt64BigEndian(span, Timestamp);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(8), deviceIdBytes.Length);
        deviceIdBytes.CopyTo(span.Slice(12));
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(12 + deviceIdBytes.Length), Nonce.Length);
        Nonce.CopyTo(span.Slice(16 + deviceIdBytes.Length));

        return buffer;
    }

    public static SessionRequestMessage Deserialize(byte[] payload)
    {
        var message = new SessionRequestMessage();
        var span = payload.AsSpan();

        message.Timestamp = BinaryPrimitives.ReadInt64BigEndian(span);

        var deviceIdLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(8));
        message.DeviceId = Encoding.UTF8.GetString(span.Slice(12, deviceIdLength));

        var nonceOffset = 12 + deviceIdLength;
        var nonceLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(nonceOffset));
        message.Nonce = span.Slice(nonceOffset + 4, nonceLength).ToArray();

        return message;
    }
}

/// <summary>
/// 心跳消息
/// </summary>
public class HeartbeatMessage : Message
{
    public override MessageType Type => MessageType.Heartbeat;
    public long Timestamp { get; set; }

    protected override byte[] SerializePayload()
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, Timestamp);
        return buffer;
    }
}

/// <summary>
/// 鼠标事件消息
/// </summary>
public class MouseEventMessage : Message
{
    public override MessageType Type => MessageType.MouseEvent;
    public int X { get; set; }
    public int Y { get; set; }
    public MouseButton Button { get; set; }
    public MouseAction Action { get; set; }
    public int Delta { get; set; } // 滚轮增量

    protected override byte[] SerializePayload()
    {
        var buffer = new byte[16];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteInt32BigEndian(span, X);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(4), Y);
        span[8] = (byte)Button;
        span[9] = (byte)Action;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(12), Delta);

        return buffer;
    }
}

public enum MouseButton : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3
}

public enum MouseAction : byte
{
    Move = 0,
    Press = 1,
    Release = 2,
    Wheel = 3
}

/// <summary>
/// 键盘事件消息
/// </summary>
public class KeyboardEventMessage : Message
{
    public override MessageType Type => MessageType.KeyboardEvent;
    public ushort KeyCode { get; set; }
    public KeyAction Action { get; set; }
    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }

    protected override byte[] SerializePayload()
    {
        var buffer = new byte[8];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, KeyCode);
        span[2] = (byte)Action;
        span[3] = (byte)((Shift ? 1 : 0) | (Ctrl ? 2 : 0) | (Alt ? 4 : 0));

        return buffer;
    }
}

public enum KeyAction : byte
{
    Press = 0,
    Release = 1
}
```

- [ ] **Step 4: 提交**

```bash
git add src/QuicRemote.Network/Protocol/
git commit -m "feat(protocol): add message protocol with CRC32 validation"
```

---

## Task 5: Core 项目基础结构

**Files:**
- Create: `src/QuicRemote.Core/QuicRemote.Core.csproj`
- Create: `src/QuicRemote.Core/Session/SessionManager.cs`

### 5.1 创建核心业务库

- [ ] **Step 1: 创建项目文件**

```xml
<!-- src/QuicRemote.Core/QuicRemote.Core.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\QuicRemote.Network\QuicRemote.Network.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 创建会话管理器**

```csharp
// src/QuicRemote.Core/Session/SessionManager.cs
using QuicRemote.Network.Quic;

namespace QuicRemote.Core.Session;

/// <summary>
/// 会话状态
/// </summary>
public enum SessionState
{
    Idle,
    Connecting,
    Connected,
    Authenticating,
    Active,
    Disconnecting,
    Disconnected,
    Failed
}

/// <summary>
/// 会话信息
/// </summary>
public class SessionInfo
{
    public Guid SessionId { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public SessionState State { get; set; }
}

/// <summary>
/// 会话管理器
/// </summary>
public class SessionManager : IAsyncDisposable
{
    private readonly Dictionary<Guid, SessionInfo> _sessions = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<SessionInfo>? SessionStateChanged;

    /// <summary>
    /// 创建新会话
    /// </summary>
    public SessionInfo CreateSession(string deviceId)
    {
        var session = new SessionInfo
        {
            SessionId = Guid.NewGuid(),
            DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow,
            State = SessionState.Idle
        };

        lock (_lock)
        {
            _sessions[session.SessionId] = session;
        }

        return session;
    }

    /// <summary>
    /// 更新会话状态
    /// </summary>
    public void UpdateState(Guid sessionId, SessionState newState)
    {
        SessionInfo? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return;
            session.State = newState;
        }

        SessionStateChanged?.Invoke(this, session);
    }

    /// <summary>
    /// 获取会话
    /// </summary>
    public SessionInfo? GetSession(Guid sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    /// <summary>
    /// 移除会话
    /// </summary>
    public void RemoveSession(Guid sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
        }
    }

    /// <summary>
    /// 获取所有活跃会话
    /// </summary>
    public IReadOnlyList<SessionInfo> GetActiveSessions()
    {
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => s.State is SessionState.Active or SessionState.Connected or SessionState.Authenticating)
                .ToList();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        lock (_lock)
        {
            _sessions.Clear();
        }

        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 3: 提交**

```bash
git add src/QuicRemote.Core/
git commit -m "feat(core): add session manager and base structures"
```

---

## Task 6: WPF 应用项目

**Files:**
- Create: `src/QuicRemote.Host/QuicRemote.Host.csproj`
- Create: `src/QuicRemote.Host/App.xaml`
- Create: `src/QuicRemote.Host/App.xaml.cs`
- Create: `src/QuicRemote.Client/QuicRemote.Client.csproj`
- Create: `src/QuicRemote.Client/App.xaml`
- Create: `src/QuicRemote.Client/App.xaml.cs`

### 6.1 创建被控端项目

- [ ] **Step 1: 创建项目文件**

```xml
<!-- src/QuicRemote.Host/QuicRemote.Host.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\QuicRemote.Core\QuicRemote.Core.csproj" />
    <ProjectReference Include="..\QuicRemote.Network\QuicRemote.Network.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 创建 App.xaml**

```xml
<!-- src/QuicRemote.Host/App.xaml -->
<Application x:Class="QuicRemote.Host.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <!-- 苹果风格颜色 -->
        <Color x:Key="LightBackgroundColor">#F5F5F7</Color>
        <Color x:Key="LightCardColor">#FFFFFF</Color>
        <Color x:Key="DarkBackgroundColor">#1C1C1E</Color>
        <Color x:Key="DarkCardColor">#2C2C2E</Color>
        <Color x:Key="AccentColor">#007AFF</Color>

        <!-- 全局样式 -->
        <Style TargetType="Window">
            <Setter Property="Background" Value="{DynamicResource WindowBackgroundBrush}"/>
        </Style>

        <Style TargetType="Button">
            <Setter Property="Padding" Value="12,8"/>
            <Setter Property="CornerRadius" Value="8"/>
        </Style>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: 创建 App.xaml.cs**

```csharp
// src/QuicRemote.Host/App.xaml.cs
using System.Windows;

namespace QuicRemote.Host;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // TODO: 初始化日志、配置等
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // TODO: 清理资源
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: 创建主窗口**

```xml
<!-- src/QuicRemote.Host/MainWindow.xaml -->
<Window x:Class="QuicRemote.Host.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="QuicRemote Host" Height="300" Width="400"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent">
    <Border CornerRadius="12" Background="White" Padding="20">
        <Border.Effect>
            <DropShadowEffect BlurRadius="20" ShadowDepth="0" Opacity="0.1"/>
        </Border.Effect>

        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="QuicRemote Host"
                       FontSize="24"
                       FontWeight="Bold"
                       HorizontalAlignment="Center"/>
            <TextBlock Text="设备码: QR-XXXXXX"
                       FontSize="14"
                       Foreground="Gray"
                       HorizontalAlignment="Center"
                       Margin="0,10,0,0"/>
            <TextBlock Text="状态: 等待连接"
                       FontSize="12"
                       Foreground="Green"
                       HorizontalAlignment="Center"
                       Margin="0,5,0,0"/>
        </StackPanel>
    </Border>
</Window>
```

```csharp
// src/QuicRemote.Host/MainWindow.xaml.cs
using System.Windows;

namespace QuicRemote.Host;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: 创建控制端项目（类似结构）**

```xml
<!-- src/QuicRemote.Client/QuicRemote.Client.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\QuicRemote.Core\QuicRemote.Core.csproj" />
    <ProjectReference Include="..\QuicRemote.Network\QuicRemote.Network.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: 提交**

```bash
git add src/QuicRemote.Host/ src/QuicRemote.Client/
git commit -m "feat(ui): add WPF Host and Client application projects"
```

---

## Task 7: 测试项目

**Files:**
- Create: `tests/QuicRemote.Network.Tests/QuicRemote.Network.Tests.csproj`
- Create: `tests/QuicRemote.Network.Tests/ProtocolTests.cs`

### 7.1 创建测试项目

- [ ] **Step 1: 创建测试项目文件**

```xml
<!-- tests/QuicRemote.Network.Tests/QuicRemote.Network.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\QuicRemote.Network\QuicRemote.Network.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 创建协议测试**

```csharp
// tests/QuicRemote.Network.Tests/ProtocolTests.cs
using QuicRemote.Network.Protocol;
using Xunit;

namespace QuicRemote.Network.Tests;

public class ProtocolTests
{
    [Fact]
    public void SessionRequest_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        // Arrange
        var original = new SessionRequestMessage
        {
            DeviceId = "QR-AB1234",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            SequenceNumber = 1
        };

        // Act
        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        // Assert
        Assert.True(result.Success);
        Assert.IsType<SessionRequestMessage>(result.Message);

        var deserialized = (SessionRequestMessage)result.Message!;
        Assert.Equal(original.DeviceId, deserialized.DeviceId);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.Nonce, deserialized.Nonce);
        Assert.Equal(original.SequenceNumber, deserialized.SequenceNumber);
    }

    [Fact]
    public void Message_WithInvalidMagic_ReturnsError()
    {
        // Arrange
        var buffer = new byte[20];
        // 不设置魔数或设置错误的魔数

        // Act
        var result = MessageSerializer.Deserialize(buffer);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("magic", result.Error?.ToLower() ?? "");
    }

    [Fact]
    public void Message_WithCorruptedCrc_ReturnsError()
    {
        // Arrange
        var message = new HeartbeatMessage
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        var serialized = message.Serialize();

        // 破坏 CRC
        serialized[^1] ^= 0xFF;

        // Act
        var result = MessageSerializer.Deserialize(serialized);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("crc", result.Error?.ToLower() ?? "");
    }

    [Fact]
    public void MouseEvent_SerializeThenDeserialize_ReturnsEqualMessage()
    {
        // Arrange
        var original = new MouseEventMessage
        {
            X = 100,
            Y = 200,
            Button = MouseButton.Left,
            Action = MouseAction.Press,
            SequenceNumber = 42
        };

        // Act
        var serialized = original.Serialize();
        var result = MessageSerializer.Deserialize(serialized);

        // Assert
        Assert.True(result.Success);
        var deserialized = (MouseEventMessage)result.Message!;
        Assert.Equal(original.X, deserialized.X);
        Assert.Equal(original.Y, deserialized.Y);
        Assert.Equal(original.Button, deserialized.Button);
        Assert.Equal(original.Action, deserialized.Action);
    }
}
```

- [ ] **Step 3: 运行测试**

```bash
dotnet test tests/QuicRemote.Network.Tests/
```

Expected: All tests pass

- [ ] **Step 4: 提交**

```bash
git add tests/
git commit -m "test: add protocol tests with CRC validation"
```

---

## Task 8: 解决方案文件与构建验证

**Files:**
- Create: `QuicRemote.sln`

### 8.1 创建解决方案文件

- [ ] **Step 1: 创建解决方案**

```bash
cd /d/AiProject/QuicRemote
dotnet new sln -n QuicRemote
# 注意: QuicRemote.Native 是 C++ CMake 项目，不添加到 .NET 解决方案
# 使用 CMake 单独构建: cmake -B build && cmake --build build
dotnet sln add src/QuicRemote.Network/QuicRemote.Network.csproj
dotnet sln add src/QuicRemote.Core/QuicRemote.Core.csproj
dotnet sln add src/QuicRemote.Host/QuicRemote.Host.csproj
dotnet sln add src/QuicRemote.Client/QuicRemote.Client.csproj
dotnet sln add tests/QuicRemote.Network.Tests/QuicRemote.Network.Tests.csproj
```

- [ ] **Step 2: 构建验证**

```bash
dotnet build QuicRemote.sln
```

Expected: Build succeeded

- [ ] **Step 3: 运行所有测试**

```bash
dotnet test
```

Expected: All tests pass

- [ ] **Step 4: 最终提交**

```bash
git add QuicRemote.sln
git commit -m "chore: add solution file and verify build"
```

---

## 完成检查清单

- [ ] 项目结构完整
- [ ] C++ Native DLL 可编译
- [ ] MsQuic 网络层封装完成
- [ ] 消息协议实现并通过测试
- [ ] WPF 应用项目可启动
- [ ] 所有测试通过
- [ ] 代码已提交到 Git

---

## Phase 2 预告

下一阶段将实现：
- 屏幕捕获 (DXGI Desktop Duplication)
- 硬件编码器 (NVENC/AMF/QSV)
- 解码与渲染
- 输入注入
- 完整会话流程
