# QuicRemote - QUIC远程控制软件设计文档

> 版本: 1.0
> 日期: 2026-03-21
> 状态: 待评审

## 1. 项目概述

### 1.1 项目简介

QuicRemote 是一款基于 QUIC 协议的远程控制软件，支持远程桌面办公和游戏串流两种场景。采用 WPF + C# 构建应用层，C++ 实现高性能媒体管道，通过 MsQuic 库实现低延迟网络通信。

### 1.2 核心特性

- **双场景支持**: 远程办公 + 游戏串流
- **混合连接**: P2P 直连优先，中继服务器回退
- **多编码器**: H.264/H.265/VP9 动态切换，硬件加速优先
- **跨平台潜力**: Windows 被控端，可扩展多平台控制端
- **增强功能**: 多显示器、会话录制、剪贴板同步、文件传输、聊天

### 1.3 性能目标

| 场景 | 延迟目标 | 帧率目标 | 码率范围 |
|------|---------|---------|---------|
| 局域网办公 | ≤ 30ms | 60 fps | 2-8 Mbps |
| 局域网游戏 | ≤ 20ms | 120 fps | 10-50 Mbps |
| 公网办公 | ≤ 100ms | 30 fps | 1-4 Mbps |
| 公网游戏 | ≤ 50ms | 60 fps | 5-20 Mbps |

### 1.4 技术栈

| 层级 | 技术选型 |
|------|---------|
| UI层 | WPF + .NET 8 |
| 业务逻辑 | C# |
| 网络通信 | MsQuic |
| 媒体管道 | C++ (DXGI, NVENC/AMF/QSV, FFmpeg) |
| 数据库 | SQLite |
| 测试框架 | xUnit, Google Test, BenchmarkDotNet |

---

## 2. 系统架构

### 2.1 分层单体架构

```
┌─────────────────────────────────────────────┐
│                 WPF UI 层                    │
│         (控制端/被控端界面)                   │
├─────────────────────────────────────────────┤
│              C# 业务逻辑层                   │
│  会话管理 │ 认证 │ 文件传输 │ 剪贴板 │ 聊天   │
├─────────────────────────────────────────────┤
│              C# 网络通信层                   │
│         MsQuic + P2P/中继切换               │
├─────────────────────────────────────────────┤
│           C++ Native DLL (核心管道)          │
│  屏幕捕获 │ 编码器 │ 解码器 │ 输入注入 │ 音频  │
└─────────────────────────────────────────────┘
```

### 2.2 项目结构

```
QuicRemote/
├── src/
│   ├── QuicRemote.Core/                 # C# 核心业务库
│   │   ├── Session/                     # 会话管理
│   │   ├── Auth/                        # 认证模块
│   │   ├── Transfer/                    # 文件传输
│   │   ├── Clipboard/                   # 剪贴板同步
│   │   ├── Chat/                        # 聊天功能
│   │   └── Recording/                   # 会话录制
│   │
│   ├── QuicRemote.Network/              # C# 网络通信层
│   │   ├── Quic/                        # MsQuic封装
│   │   ├── P2P/                         # P2P穿透
│   │   ├── Relay/                       # 中继客户端
│   │   └── Protocol/                    # 应用层协议
│   │
│   ├── QuicRemote.Native/               # C++ 核心管道 (DLL)
│   │   ├── Capture/                     # 屏幕捕获 (DXGI Desktop Duplication)
│   │   ├── Encoder/                     # 编码器 (H.264/H.265/VP9)
│   │   ├── Decoder/                     # 解码器
│   │   ├── Input/                       # 鼠标键盘注入
│   │   └── Audio/                       # 音频捕获/播放
│   │
│   ├── QuicRemote.Host/                 # WPF 被控端应用
│   │   ├── Views/                       # UI界面
│   │   ├── ViewModels/                  # 视图模型
│   │   └── Services/                    # 本地服务
│   │
│   ├── QuicRemote.Client/               # WPF 控制端应用
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   └── Services/
│   │
│   └── QuicRemote.Relay/                # 中继服务器 (可选部署)
│       └── ...
│
├── tests/                               # 测试项目
├── docs/                                # 文档
└── tools/                               # 构建工具
```

### 2.3 模块职责

| 模块 | 语言 | 职责 |
|------|------|------|
| QuicRemote.Native | C++ | 高性能媒体管道，导出C接口供C#调用 |
| QuicRemote.Network | C# | QUIC通信、P2P穿透、中继连接 |
| QuicRemote.Core | C# | 业务逻辑、会话管理、文件传输等 |
| QuicRemote.Host | C# | 被控端UI和本地服务 |
| QuicRemote.Client | C# | 控制端UI和远程渲染 |
| QuicRemote.Relay | C# | 中继服务器 |

---

## 3. C++ Native 模块设计

### 3.1 模块架构

```
┌──────────────────────────────────────────────────────────┐
│                    QuicRemote.Native.dll                  │
├──────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐       │
│  │  Capture    │  │  Encoder    │  │  Decoder    │       │
│  │  Manager    │  │  Manager    │  │  Manager    │       │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘       │
│         │                │                │              │
│  ┌──────▼──────┐  ┌──────▼──────┐  ┌──────▼──────┐       │
│  │DXGI Desktop │  │ NVENC/AMF   │  │ NVDEC/D3D11│       │
│  │ Duplication │  │ QSV/Software│  │ Software   │       │
│  └─────────────┘  └─────────────┘  └─────────────┘       │
│                                                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐       │
│  │   Input     │  │   Audio     │  │   Frame     │       │
│  │  Injector   │  │   Capture   │  │   Buffer    │       │
│  └─────────────┘  └─────────────┘  └─────────────┘       │
├──────────────────────────────────────────────────────────┤
│                    C API Export Layer                     │
│              (供C#通过P/Invoke调用)                       │
└──────────────────────────────────────────────────────────┘
```

### 3.2 子模块设计

#### 3.2.1 屏幕捕获 (Capture)

- **技术选型**: DXGI Desktop Duplication API
- **功能**: 捕获桌面帧、脏区域检测、光标叠加
- **输出**: D3D11纹理或系统内存缓冲区
- **优化**: 脏区域检测减少编码工作量，光标独立传输

#### 3.2.2 编码器 (Encoder)

- **硬件编码优先级**: NVENC (NVIDIA) → AMF (AMD) → QSV (Intel)
- **软件编码回退**: FFmpeg x264/x265
- **动态切换**: 根据GPU能力和场景需求自动选择
- **参数配置**: 码率控制(CBR/VBR)、延迟模式、质量预设

#### 3.2.3 解码器 (Decoder)

- **硬件解码优先级**: NVDEC → D3D11 Video Decoder → QSV
- **软件解码回退**: FFmpeg
- **输出**: D3D11纹理供渲染或系统内存供处理

#### 3.2.4 输入注入 (Input)

- **技术**: SendInput API + 低级钩子
- **支持**: 鼠标移动、点击、滚轮、键盘按键、组合键

#### 3.2.5 音频捕获 (Audio)

- **技术**: WASAPI Loopback
- **功能**: 捕获系统音频输出、可选麦克风输入

### 3.3 C API 接口

#### 3.3.1 错误码定义

```c
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

// 获取错误描述
const char* QR_GetErrorDescription(QR_Result result);
const char* QR_GetVersionString();
uint32_t QR_GetVersion();
```

#### 3.3.2 核心接口

```c
// 初始化/销毁
QR_Result QR_Init(QR_Config* config);
QR_Result QR_Shutdown();

// 屏幕捕获
QR_Result QR_Capture_Start(int monitor_index);
QR_Result QR_Capture_GetFrame(QR_Frame** frame);
QR_Result QR_Capture_ReleaseFrame(QR_Frame* frame);
QR_Result QR_Capture_Stop();

// 编码
QR_Result QR_Encoder_Create(QR_EncoderConfig* config, QR_EncoderHandle* handle);
QR_Result QR_Encoder_Encode(QR_EncoderHandle handle, QR_Frame* frame, QR_Packet** packet);
QR_Result QR_Encoder_Destroy(QR_EncoderHandle handle);

// 解码
QR_Result QR_Decoder_Create(QR_DecoderConfig* config, QR_DecoderHandle* handle);
QR_Result QR_Decoder_Decode(QR_DecoderHandle handle, QR_Packet* packet, QR_Frame** frame);
QR_Result QR_Decoder_Destroy(QR_DecoderHandle handle);

// 输入注入
QR_Result QR_Input_MouseMove(int x, int y);
QR_Result QR_Input_MouseButton(QR_MouseButton button, QR_ButtonAction action);
QR_Result QR_Input_MouseWheel(int delta);
QR_Result QR_Input_Key(QR_KeyCode key, QR_KeyAction action);

// 音频
QR_Result QR_Audio_StartCapture();
QR_Result QR_Audio_GetData(QR_AudioData** data);
QR_Result QR_Audio_StopCapture();

// 回调注册
QR_Result QR_SetFrameCallback(QR_FrameCallback callback, void* context);
```

#### 3.3.3 内存生命周期管理

**帧数据所有权规则：**

```
┌─────────────────────────────────────────────────────────────────┐
│                    帧数据生命周期                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. GetFrame 返回的帧数据由调用者拥有                           │
│  2. 调用者必须在使用完毕后调用 ReleaseFrame                     │
│  3. 未释放的帧会导致纹理池耗尽，影响捕获性能                     │
│  4. 帧数据不是线程安全的，不应跨线程传递                        │
│  5. 最大未释放帧数：30帧（超过将阻塞捕获）                      │
│                                                                 │
│  示例:                                                          │
│  QR_Frame* frame;                                               │
│  QR_Capture_GetFrame(&frame);    // 获取帧，获得所有权          │
│  // ... 使用帧数据 ...                                          │
│  QR_Capture_ReleaseFrame(frame); // 释放帧，归还纹理池          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**编码包生命周期：**

```
┌─────────────────────────────────────────────────────────────────┐
│                    编码包生命周期                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Encode 输出的 Packet 由调用者拥有                           │
│  2. Packet 内部数据在下次 Encode 调用前有效                     │
│  3. 如需长期保存，调用者应拷贝数据                               │
│  4. 推荐使用环形缓冲区管理 Packet 内存                           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**C# SafeHandle 封装：**

```csharp
public sealed class NativeFrameHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeFrameHandle() : base(true) { }

    protected override bool ReleaseHandle()
    {
        NativeMethods.QR_Capture_ReleaseFrame(handle);
        return true;
    }
}

public sealed class EncoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected override bool ReleaseHandle()
    {
        NativeMethods.QR_Encoder_Destroy(handle);
        return true;
    }
}
```

---

## 4. 网络通信层设计

### 4.1 网络层架构

```
┌─────────────────────────────────────────────────────────┐
│                  QuicRemote.Network                      │
├─────────────────────────────────────────────────────────┤
│                   Application Protocol                   │
│  ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌─────────┐ │
│  │ Session   │ │ Control   │ │  Media    │ │  File   │ │
│  │ Protocol  │ │ Channel   │ │ Channel   │ │Transfer │ │
│  └───────────┘ └───────────┘ └───────────┘ └─────────┘ │
├─────────────────────────────────────────────────────────┤
│                   Connection Manager                     │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐ │
│  │ P2P Client  │◄──►│ Relay Client│◄──►│ Discovery   │ │
│  └─────────────┘    └─────────────┘    └─────────────┘ │
├─────────────────────────────────────────────────────────┤
│                   QUIC Transport (MsQuic)                │
└─────────────────────────────────────────────────────────┘
```

### 4.2 连接建立流程

```
控制端                      中继服务器                    被控端
   │                            │                          │
   │  1. 查询被控端地址         │                          │
   │ ─────────────────────────► │                          │
   │                            │  2. 返回被控端NAT信息    │
   │ ◄───────────────────────── │ ◄───────────────────────  │
   │                            │                          │
   │  3. 尝试P2P直连 (UDP打洞)  │                          │
   │ ───────────────────────────────────────────────────► │
   │                            │                          │
   │      [P2P成功] ──────────────────────────────────►   │
   │                            │                          │
   │      [P2P失败时回退中继]    │                          │
   │ ─────────────────────────► │ ───────────────────────► │
   │                            │                          │
   │  4. QUIC握手 + 认证        │                          │
   │ ◄─────────────────────────────────────────────────►  │
   │                            │                          │
   │  5. 建立多路流通道         │                          │
   │ ◄─────────────────────────────────────────────────►  │
```

### 4.3 QUIC流通道设计

| Stream ID | 方向 | 用途 | QoS优先级 |
|-----------|------|------|-----------|
| 0 | 双向 | 控制信令 | 最高 |
| 1 | 被控端→控制端 | 视频流 | 高 |
| 2 | 被控端→控制端 | 音频流 | 高 |
| 3 | 控制端→被控端 | 输入事件 | 最高 |
| 4 | 双向 | 文件传输 | 低 |
| 5 | 双向 | 聊天消息 | 中 |
| 6-63 | 保留 | 扩展用 | - |

### 4.4 消息包格式

```
┌─────────────────────────────────────────────────────┐
│  Message Header (12 bytes)                          │
├─────────────────────────────────────────────────────┤
│  Magic (2) │ Type (1) │ Flags (1) │ SeqNum (4)      │
├─────────────────────────────────────────────────────┤
│  PayloadLen (4)                                     │
├─────────────────────────────────────────────────────┤
│  Payload (variable length)                          │
│  ...                                                │
├─────────────────────────────────────────────────────┤
│  CRC32 (4 bytes)                                    │
└─────────────────────────────────────────────────────┘
```

**字段说明：**

| 字段 | 大小 | 说明 |
|------|------|------|
| Magic | 2 bytes | 魔数 `0x5152` ("QR") |
| Type | 1 byte | 消息类型 |
| Flags | 1 byte | 标志位（压缩、加密、分片等） |
| SeqNum | 4 bytes | 序列号 |
| PayloadLen | 4 bytes | 载荷长度 |
| Payload | 变长 | 实际数据 |
| CRC32 | 4 bytes | 消息校验值（从Magic到Payload） |

### 4.5 消息类型定义

```csharp
// 会话控制
SessionRequest      = 0x01,  // 请求连接
SessionAccept       = 0x02,  // 接受连接
SessionReject       = 0x03,  // 拒绝连接
SessionEnd          = 0x04,  // 结束会话
Heartbeat           = 0x05,  // 心跳

// 媒体控制
VideoConfig         = 0x10,  // 视频参数配置
VideoFrame          = 0x11,  // 视频帧数据
AudioConfig         = 0x12,  // 音频参数配置
AudioData           = 0x13,  // 音频数据

// 输入控制
MouseEvent          = 0x20,  // 鼠标事件
KeyboardEvent       = 0x21,  // 键盘事件
ClipboardSync       = 0x22,  // 剪贴板同步

// 文件传输
FileTransferRequest = 0x30,  // 文件传输请求
FileData            = 0x31,  // 文件数据块
FileAck             = 0x32,  // 传输确认

// 聊天
ChatMessage         = 0x40,  // 聊天消息
```

### 4.6 Flags 标志位

```
Bit 0: Compressed   - Payload已压缩
Bit 1: Encrypted    - Payload已加密
Bit 2: Fragmented   - 大消息分片传输
Bit 3: LastFragment - 最后一个分片
Bit 4-7: Reserved   - 保留
```

### 4.7 P2P穿透策略

1. **STUN探测**: 获取双方NAT类型和公网地址
2. **打洞策略**:
   - Full Cone NAT → 直接打洞
   - Symmetric NAT → 端口预测或中继回退
3. **ICE框架**: 尝试所有候选地址直到成功

---

## 5. 安全认证设计

### 5.1 混合认证方案

```
┌─────────────────────────────────────────────────────────────┐
│                     认证流程决策                             │
├─────────────────────────────────────────────────────────────┤
│   ┌─────────────┐                                           │
│   │ 连接请求    │                                           │
│   └──────┬──────┘                                           │
│          │                                                   │
│          ▼                                                   │
│   ┌─────────────┐    是     ┌─────────────────────────┐    │
│   │ 局域网连接? │─────────►│ 简单密码认证             │    │
│   └──────┬──────┘           │ (设备码 + PIN)          │    │
│          │ 否                └─────────────────────────┘    │
│          ▼                                                   │
│   ┌─────────────────────────┐                              │
│   │ 公网连接                │                              │
│   └──────────┬──────────────┘                              │
│              │                                               │
│              ▼                                               │
│   ┌─────────────────────────┐    成功   ┌───────────────┐  │
│   │ 账号系统认证            │─────────►│ 颁发会话令牌  │  │
│   │ (用户名/密码 + OTP)     │          └───────────────┘  │
│   └──────────┬──────────────┘                              │
│              │ 失败                                          │
│              ▼                                               │
│   ┌─────────────────────────┐                              │
│   │ 拒绝连接                │                              │
│   └─────────────────────────┘                              │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 证书与加密

- **QUIC层加密**: TLS 1.3 内置加密
- **局域网**: 自签名证书
- **公网**: CA签发证书
- **应用层加密**: 敏感数据使用 AES-256-GCM 额外加密

### 5.3 认证消息流程

```
控制端                                      被控端
   │                                          │
   │  1. ConnectionRequest                    │
   │  {device_id, timestamp, nonce}          │
   │ ─────────────────────────────────────►  │
   │                                          │
   │  2. ConnectionChallenge                 │
   │  {challenge, auth_methods}              │
   │ ◄────────────────────────────────────   │
   │                                          │
   │  3. AuthResponse                        │
   │  {auth_type, credentials, signature}    │
   │ ─────────────────────────────────────►  │
   │                                          │
   │  4. AuthResult                          │
   │  {success, session_token, permissions}  │
   │ ◄────────────────────────────────────   │
```

### 5.4 权限控制

| 权限 | 说明 |
|------|------|
| `view` | 仅查看屏幕 |
| `control` | 鼠标键盘控制 |
| `file_read` | 读取文件 |
| `file_write` | 写入文件 |
| `clipboard` | 剪贴板同步 |
| `audio` | 音频传输 |
| `chat` | 聊天功能 |
| `admin` | 完全控制 |

---

## 6. UI层设计

### 6.1 设计风格

采用苹果风格设计语言：

| 特性 | 说明 |
|------|------|
| 圆角 | 大圆角（12-20px） |
| 毛玻璃 | 半透明背景 + 模糊效果 |
| 阴影 | 柔和多层阴影 |
| 颜色 | 支持浅色/深色模式 |
| 动画 | 流畅自然的过渡 |
| 留白 | 充足空间 |

### 6.2 颜色定义

```csharp
// 浅色模式
LightBackground = #F5F5F7
LightCard = #FFFFFF
LightSecondaryText = #86868B

// 深色模式
DarkBackground = #1C1C1E
DarkCard = #2C2C2E
DarkSecondaryText = #98989D

// 主题色
Accent = #007AFF
Success = #34C759
Warning = #FF9500
Error = #FF3B30
```

### 6.3 MVVM架构

```
QuicRemote.Client/
├── App.xaml
├── Views/
│   ├── MainWindow.xaml
│   ├── ConnectWindow.xaml
│   ├── SettingsWindow.xaml
│   └── Controls/
│       ├── RemoteScreenControl.xaml
│       ├── ChatPanel.xaml
│       └── FileTransferPanel.xaml
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── ConnectViewModel.cs
│   ├── SettingsViewModel.cs
│   └── RemoteScreenViewModel.cs
├── Services/
│   ├── IRemoteSessionService.cs
│   └── SettingsService.cs
├── Models/
│   └── Settings.cs
└── Converters/
    └── ConnectionStatusConverter.cs
```

---

## 7. 数据流设计

### 7.1 被控端到控制端数据流

```
被控端                                              控制端
  │                                                   │
  │  DXGI捕获 → 纹理池 → 编码器 → NAL包队列          │
  │                                                   │
  │  音频捕获 ────────────────────────────────►      │
  │                                                   │
  │  QUIC Stream ─────────────────────────────► 接收队列
  │                                                   │
  │                                         解包 → 解码 → 渲染
  │                                                   │
  │                                         音频解码 → 播放
```

### 7.2 控制端到被控端输入流

```
控制端                                              被控端
  │                                                   │
  │  用户输入 → 事件收集 → 序列化                    │
  │                                                   │
  │  剪贴板监控 ───────────────────────────────►     │
  │                                                   │
  │  QUIC Stream ─────────────────────────────► 接收队列
  │                                                   │
  │                                         反序列化 → 注入器
  │                                                   │
  │                                         SendInput API
```

### 7.3 文件传输流程

```
发送方                                              接收方
   │                                                   │
   │  FileTransferRequest {filename, size}            │
   │ ────────────────────────────────────────────────►│
   │                                                   │
   │  FileTransferAccept {save_path}                  │
   │ ◄────────────────────────────────────────────────│
   │                                                   │
   │  FileData (分块) ───────────────────────────────►│
   │                                                   │
   │  FileAck (确认) ◄────────────────────────────────│
   │                                                   │
   │  FileTransferComplete {checksum}                 │
   │ ────────────────────────────────────────────────►│
```

---

## 8. 错误处理与容错

### 8.1 连接状态机

```
Idle ──连接中──► Connecting ──成功──► Connected
                     │                   │
                     │失败               │丢失
                     ▼                   ▼
                 Failed              Reconnecting
                                         │
                         ┌───────────────┤
                         │成功           │失败(达上限)
                         ▼               ▼
                     Connected      Disconnected
```

### 8.2 重连策略

```csharp
MaxRetries = 5
InitialDelay = 1s
MaxDelay = 30s
BackoffMultiplier = 2.0
// 指数退避: 1s → 2s → 4s → 8s → 16s
```

### 8.3 编码器故障恢复

```
NVENC故障 → 重置NVENC
    │           │
    │           │失败
    │           ▼
    │      切换AMF (AMD)
    │           │
    │           │失败
    │           ▼
    │      切换QSV (Intel)
    │           │
    │           │失败
    │           ▼
    └─────► 软件编码 (x264/x265)
```

### 8.4 错误类型处理

| 错误类型 | 处理策略 |
|---------|---------|
| NetworkTimeout | 自动重连 |
| ConnectionLost | P2P→中继切换 |
| AuthFailed | 提示用户 |
| EncoderError | 自动切换编码器 |
| DecoderError | 请求关键帧 |
| CaptureError | 重启捕获管道 |

---

## 9. 监控与指标上报

### 9.1 监控架构

```
Metrics Collector (采集)
        │
        ▼
Metrics Aggregator (聚合)
        │
   ┌────┼────┐
   ▼    ▼    ▼
本地存储  UI展示  远程上报
```

### 9.2 指标分类

#### 网络指标
- 延迟、抖动、丢包率
- 上下行速率
- QUIC RTT、拥塞窗口、重传次数

#### 媒体指标
- 帧率、编码/解码延迟
- 端到端延迟、丢帧数
- 码率、编码器利用率

#### 系统指标
- CPU/GPU使用率
- 内存/显存占用
- 电池电量、温度状态

#### 会话指标
- 会话数、成功率
- 会话时长
- 输入事件数、文件传输量

### 9.3 告警规则

| 规则名 | 条件 | 阈值 | 持续时间 | 严重级别 |
|--------|------|------|---------|---------|
| HighLatency | latency > | 100ms | 10s | Warning |
| LowFrameRate | fps < | 20 | 5s | Warning |
| HighPacketLoss | packet_loss > | 5% | 10s | Error |
| EncoderFailure | encoder_errors > | 0 | 0s | Critical |

### 9.4 本地存储

```sql
CREATE TABLE metrics (
    id INTEGER PRIMARY KEY,
    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    session_id TEXT,
    metric_name TEXT,
    metric_value REAL,
    tags TEXT
);

CREATE TABLE session_stats (
    session_id TEXT PRIMARY KEY,
    start_time DATETIME,
    end_time DATETIME,
    avg_latency REAL,
    avg_fps REAL,
    encoder_used TEXT
);
```

---

## 10. 测试策略

### 10.1 测试金字塔

```
        ┌─────────┐
        │ E2E测试 │  (少量)
        └─────────┘
       ┌───────────┐
       │ 集成测试  │  (中等)
       └───────────┘
      ┌─────────────┐
      │  单元测试   │  (大量)
      └─────────────┘
```

### 10.2 测试项目

```
tests/
├── QuicRemote.Native.Tests/        # C++ Google Test
├── QuicRemote.Core.Tests/          # C# xUnit
├── QuicRemote.Network.Tests/       # C# xUnit
├── QuicRemote.Integration.Tests/   # 集成测试
├── QuicRemote.E2E.Tests/           # 端到端测试
└── QuicRemote.Performance.Tests/   # BenchmarkDotNet
```

### 10.3 覆盖率目标

| 模块 | 目标覆盖率 |
|------|-----------|
| QuicRemote.Core | ≥ 80% |
| QuicRemote.Network | ≥ 75% |
| QuicRemote.Native | ≥ 70% |
| UI层 | ≥ 50% |

### 10.4 关键测试场景

- 连接建立与断开
- 认证流程（成功/失败/锁定）
- 编码器切换
- 网络断线重连
- 长时间运行内存泄漏
- 并发多会话

---

## 11. 性能优化策略

### 11.1 延迟分解目标（局域网30ms）

| 环节 | 目标延迟 |
|------|---------|
| 屏幕捕获 | 2ms |
| 编码 | 5ms |
| 网络传输 | 10ms |
| 解码 | 3ms |
| 渲染 | 2ms |
| 缓冲 | 8ms |

### 11.2 资源占用目标

| 指标 | 被控端 | 控制端 |
|------|--------|--------|
| CPU | ≤ 15% | ≤ 10% |
| GPU | ≤ 30% | ≤ 20% |
| 内存 | ≤ 200MB | ≤ 150MB |

### 11.3 优化措施

#### 屏幕捕获
- 脏区域检测
- 光标独立传输
- 纹理池复用
- 多显示器并行捕获

#### 编码
- 硬件编码低延迟预设
- 动态码率控制
- ROI区域编码
- 禁用B帧

#### 网络
- BBR拥塞控制
- 帧优先级队列
- 丢帧策略
- 前向纠错（可选）

#### 解码渲染
- 零拷贝管线
- 渲染线程分离
- 自适应缓冲深度

#### 输入
- 事件合并
- 输入预测（高延迟场景）

#### 内存
- 对象池
- 非托管缓冲区
- GC低延迟模式

---

## 12. 日志系统设计

### 12.1 日志级别

| 级别 | 用途 |
|------|------|
| Trace | 详细追踪信息，仅调试模式 |
| Debug | 调试信息，开发阶段 |
| Info | 常规操作信息 |
| Warning | 警告信息，不影响正常运行 |
| Error | 错误信息，需要关注 |
| Critical | 严重错误，影响核心功能 |

### 12.2 日志输出

```
┌─────────────────────────────────────────────────────────────────┐
│                     日志输出目标                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│  │  文件日志   │  │ Windows事件 │  │  远程上报   │            │
│  │             │  │    日志     │  │   (可选)    │            │
│  └─────────────┘  └─────────────┘  └─────────────┘            │
│                                                                 │
│  路径: %AppData%/QuicRemote/logs/                              │
│  格式: quicremote_YYYYMMDD.log                                 │
│  滚动: 按日期滚动，保留30天                                     │
│  大小: 单文件最大 50MB                                          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 12.3 日志格式

```
[时间] [级别] [线程ID] [模块] 消息内容
[2026-03-21 10:30:45.123] [INFO] [12] [Network] 连接建立: QR-AB1234
[2026-03-21 10:30:45.456] [DEBUG] [8] [Encoder] NVENC 编码帧: 1920x1080, 2.1ms
```

### 12.4 敏感信息处理

| 信息类型 | 处理方式 |
|---------|---------|
| 密码 | 完全脱敏 (*****) |
| 令牌 | 仅显示前6位 |
| IP地址 | 最后一段隐藏 (192.168.1.*) |
| 文件路径 | 用户目录替换为 ~ |

### 12.5 结构化日志

```csharp
// 使用 Serilog
Log.Information("会话建立 {DeviceId} {ConnectionType} {Latency}ms",
    deviceId, connectionType, latency);

// 输出JSON格式便于分析
{
    "timestamp": "2026-03-21T10:30:45.123Z",
    "level": "Information",
    "message": "会话建立",
    "properties": {
        "DeviceId": "QR-AB1234",
        "ConnectionType": "P2P",
        "Latency": 12
    }
}
```

---

## 13. 配置管理设计

### 13.1 配置文件

```
%AppData%/QuicRemote/
├── config.json          # 主配置文件
├── profiles/            # 连接配置文件
│   ├── default.json
│   └── gaming.json
└── certificates/        # 证书文件
    └── client.pfx
```

### 13.2 配置项结构

```csharp
public class AppConfig
{
    // 网络配置
    public NetworkConfig Network { get; set; }

    // 媒体配置
    public MediaConfig Media { get; set; }

    // UI配置
    public UiConfig Ui { get; set; }

    // 安全配置
    public SecurityConfig Security { get; set; }

    // 日志配置
    public LoggingConfig Logging { get; set; }
}

public class NetworkConfig
{
    public int ListenPort { get; set; } = 48960;
    public int MaxConnections { get; set; } = 5;
    public bool EnableP2P { get; set; } = true;
    public bool EnableRelay { get; set; } = true;
    public string RelayServer { get; set; } = "relay.quicremote.com";
    public int ConnectionTimeout { get; set; } = 30000;
}

public class MediaConfig
{
    public VideoConfig Video { get; set; }
    public AudioConfig Audio { get; set; }
}

public class VideoConfig
{
    public string PreferredEncoder { get; set; } = "auto";  // auto, nvenc, amf, qsv, software
    public string PreferredCodec { get; set; } = "h265";    // h264, h265, vp9
    public int TargetFramerate { get; set; } = 60;
    public int MaxBitrate { get; set; } = 50000;  // Kbps
    public int QualityPreset { get; set; } = 2;   // 1=速度, 2=平衡, 3=质量
}
```

### 13.3 配置热更新

```csharp
public interface IConfigService
{
    T Get<T>(string key);
    void Set<T>(string key, T value);
    IObservable<T> Observe<T>(string key);
    event EventHandler<ConfigChangedEventArgs> ConfigChanged;
}

// 使用示例
configService.Observe<int>("Media.Video.TargetFramerate")
    .Subscribe(fps => encoder.SetFramerate(fps));
```

### 13.4 默认配置

```json
{
    "Network": {
        "ListenPort": 48960,
        "EnableP2P": true,
        "EnableRelay": true,
        "ConnectionTimeout": 30000
    },
    "Media": {
        "Video": {
            "PreferredEncoder": "auto",
            "PreferredCodec": "h265",
            "TargetFramerate": 60,
            "MaxBitrate": 50000
        },
        "Audio": {
            "Enabled": true,
            "SampleRate": 48000,
            "Channels": 2
        }
    },
    "Ui": {
        "Theme": "system",
        "Language": "zh-CN"
    },
    "Security": {
        "AuthMode": "hybrid",
        "SessionTimeout": 3600
    },
    "Logging": {
        "Level": "info",
        "EnableFileLog": true,
        "EnableRemoteLog": false
    }
}
```

---

## 14. 中继服务器设计

### 14.1 架构设计

```
┌─────────────────────────────────────────────────────────────────┐
│                     中继服务器集群                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│                    ┌─────────────┐                             │
│                    │ 负载均衡器  │                             │
│                    │   (LB)      │                             │
│                    └──────┬──────┘                             │
│                           │                                     │
│         ┌─────────────────┼─────────────────┐                  │
│         │                 │                 │                  │
│         ▼                 ▼                 ▼                  │
│   ┌───────────┐     ┌───────────┐     ┌───────────┐          │
│   │ Relay #1  │     │ Relay #2  │     │ Relay #3  │          │
│   │  (CN-SH)  │     │  (CN-BJ)  │     │  (US-LA)  │          │
│   └─────┬─────┘     └─────┬─────┘     └─────┬─────┘          │
│         │                 │                 │                  │
│         └─────────────────┼─────────────────┘                  │
│                           │                                     │
│                    ┌──────▼──────┐                             │
│                    │  Redis      │                             │
│                    │  (会话状态)  │                             │
│                    └─────────────┘                             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 14.2 技术栈

| 组件 | 技术选型 |
|------|---------|
| 框架 | ASP.NET Core 8.0 |
| QUIC | MsQuic |
| 存储 | Redis (会话状态) |
| 部署 | Docker + Kubernetes |
| 监控 | Prometheus + Grafana |

### 14.3 性能指标

| 指标 | 目标值 |
|------|--------|
| 单节点并发连接 | 10,000 |
| 转发延迟 | < 5ms |
| 吞吐量 | 10 Gbps |
| 可用性 | 99.9% |

### 14.4 核心功能

```
┌─────────────────────────────────────────────────────────────────┐
│                    Relay Server 功能                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. 连接注册                                                    │
│     - 设备ID → QUIC连接映射                                    │
│     - 心跳检测 (30s间隔)                                        │
│     - 连接状态跟踪                                              │
│                                                                 │
│  2. 数据转发                                                    │
│     - 双向数据透传                                              │
│     - 流量统计                                                  │
│     - 带宽限制 (可配置)                                         │
│                                                                 │
│  3. 认证授权                                                    │
│     - JWT令牌验证                                               │
│     - 权限检查                                                  │
│     - 访问日志                                                  │
│                                                                 │
│  4. NAT穿透辅助                                                 │
│     - STUN服务                                                  │
│     - 候选地址交换                                              │
│     - 穿透成功率统计                                            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 14.5 中继选择策略

```csharp
public class RelaySelector
{
    // 选择最近的中继节点
    public RelayNode SelectBestNode(string clientRegion)
    {
        var nodes = _registry.GetAvailableNodes();

        return nodes
            .OrderBy(n => GetLatency(n.Region, clientRegion))
            .ThenBy(n => n.CurrentLoad)
            .First();
    }

    // 区域映射
    private readonly Dictionary<string, string> _regionMapping = new()
    {
        ["CN-EAST"] = "CN-SH",
        ["CN-NORTH"] = "CN-BJ",
        ["CN-SOUTH"] = "CN-GZ",
        ["US-WEST"] = "US-LA",
        ["US-EAST"] = "US-NY",
        ["EU-WEST"] = "EU-LON"
    };
}
```

### 14.6 部署方案

```yaml
# docker-compose.yml
version: '3.8'
services:
  relay:
    image: quicremote/relay:latest
    ports:
      - "48960:48960/udp"
      - "48960:48960/tcp"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Redis__Connection=redis:6379
    deploy:
      replicas: 3
      resources:
        limits:
          cpus: '4'
          memory: 4G

  redis:
    image: redis:7-alpine
    volumes:
      - redis-data:/data
```

---

## 15. 编码器热切换设计

### 15.1 切换触发条件

| 触发条件 | 说明 |
|---------|------|
| 编码错误 | 连续3次编码失败 |
| 设备丢失 | GPU驱动崩溃/设备移除 |
| 性能降级 | 编码延迟超过阈值 |
| 手动切换 | 用户主动切换 |

### 15.2 切换流程

```
┌─────────────────────────────────────────────────────────────────┐
│                   编码器热切换流程                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   检测到故障                                                    │
│        │                                                        │
│        ▼                                                        │
│   ┌─────────────┐                                              │
│   │ 发送切换通知│ ──► 控制端准备接收新编码流                   │
│   │ 到控制端    │                                              │
│   └──────┬──────┘                                              │
│          │                                                      │
│          ▼                                                      │
│   ┌─────────────┐                                              │
│   │ 销毁旧编码器│ ──► 释放GPU资源                              │
│   └──────┬──────┘                                              │
│          │                                                      │
│          ▼                                                      │
│   ┌─────────────┐                                              │
│   │ 创建新编码器│ ──► 选择备用编码器                            │
│   └──────┬──────┘                                              │
│          │                                                      │
│          ▼                                                      │
│   ┌─────────────┐                                              │
│   │ 发送关键帧  │ ──► 确保解码器能正确恢复                     │
│   └──────┬──────┘                                              │
│          │                                                      │
│          ▼                                                      │
│   ┌─────────────┐                                              │
│   │ 恢复正常编码│                                              │
│   └─────────────┘                                              │
│                                                                 │
│   预期中断时间: < 100ms                                         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 15.3 切换协议消息

```csharp
public class EncoderSwitchMessage
{
    public string OldEncoder { get; set; }      // NVENC
    public string NewEncoder { get; set; }      // AMF
    public string Codec { get; set; }           // H265
    public int Width { get; set; }
    public int Height { get; set; }
    public int Bitrate { get; set; }
}

// 控制端响应
public class EncoderSwitchAck
{
    public bool Ready { get; set; }
    public int ExpectedKeyFrameTime { get; set; }  // 预期关键帧时间
}
```

### 15.4 控制端处理

```csharp
public async Task OnEncoderSwitchAsync(EncoderSwitchMessage msg)
{
    // 1. 暂停解码
    _decoder.Pause();

    // 2. 重置解码器
    _decoder.Reset();

    // 3. 通知准备就绪
    await SendAsync(new EncoderSwitchAck { Ready = true });

    // 4. 等待关键帧
    await WaitForKeyFrameAsync();

    // 5. 恢复解码
    _decoder.Resume();

    // 6. 显示切换提示 (可选)
    ShowNotification($"编码器已切换: {msg.OldEncoder} → {msg.NewEncoder}");
}
```

---

## 16. P2P穿透成功率目标

### 16.1 NAT类型分布与穿透策略

| NAT类型 | 市场占比 | 穿透方法 | 成功率目标 |
|---------|---------|---------|-----------|
| Full Cone | 15% | 直接打洞 | 99% |
| Restricted Cone | 20% | 端口预测 | 95% |
| Port Restricted | 25% | 端口预测 | 85% |
| Symmetric | 35% | 中继回退 | 60% (P2P) → 100% (中继) |
| Double NAT | 5% | 中继回退 | 0% (P2P) → 100% (中继) |

### 16.2 整体成功率目标

```
┌─────────────────────────────────────────────────────────────────┐
│                    P2P穿透成功率                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  目标: P2P直连成功率 ≥ 65%                                      │
│        P2P + 中继 总成功率 ≥ 99.5%                              │
│                                                                 │
│  局域网场景: 100% P2P                                           │
│  公网场景: 65% P2P + 34.5% 中继                                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 16.3 穿透流程优化

```
1. STUN探测 (并行)
   ├── 探测NAT类型
   ├── 获取公网地址
   └── 检测是否对称NAT

2. ICE候选收集
   ├── 本地地址
   ├── STUN反射地址
   └── TURN中继地址 (备用)

3. 连接尝试 (超时5秒)
   ├── 尝试P2P打洞
   │   ├── 成功 → 建立直连
   │   └── 失败 → 进入步骤4
   └──

4. 中继回退
   └── 通过中继服务器建立连接
```

---

## 17. 多显示器详细设计

### 17.1 显示器枚举与管理

```csharp
public class MonitorManager
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        // 使用 EnumDisplayMonitors 获取所有显示器
        // 包含: 分辨率、位置、DPI、主显示器标识
    }

    public MonitorLayout GetLayout()
    {
        // 返回显示器布局信息
        // 用于控制端显示正确排列
    }
}

public class MonitorInfo
{
    public int Index { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public float DpiScale { get; set; }
    public bool IsPrimary { get; set; }
    public string Name { get; set; }
}
```

### 17.2 DPI缩放处理

```csharp
public class DpiHelper
{
    // 逻辑坐标 → 物理坐标
    public Point LogicalToPhysical(Point logical, MonitorInfo monitor)
    {
        return new Point(
            (int)(logical.X * monitor.DpiScale),
            (int)(logical.Y * monitor.DpiScale)
        );
    }

    // 物理坐标 → 逻辑坐标
    public Point PhysicalToLogical(Point physical, MonitorInfo monitor)
    {
        return new Point(
            (int)(physical.X / monitor.DpiScale),
            (int)(physical.Y / monitor.DpiScale)
        );
    }

    // 跨显示器坐标转换
    public Point TransformCoordinate(Point source, MonitorInfo from, MonitorInfo to)
    {
        // 处理不同DPI显示器间的坐标转换
    }
}
```

### 17.3 控制端显示模式

```
┌─────────────────────────────────────────────────────────────────┐
│                    多显示器显示模式                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  模式1: 单显示器                                                │
│  ┌───────────────────┐                                         │
│  │     显示器 1      │  全屏显示选中的单个显示器                │
│  └───────────────────┘                                         │
│                                                                 │
│  模式2: 全部显示 (网格布局)                                     │
│  ┌───────────┬───────────┐                                     │
│  │  显示器1  │  显示器2  │  缩略图显示所有显示器                │
│  ├───────────┼───────────┤  点击切换到单显示器模式              │
│  │  显示器3  │           │                                     │
│  └───────────┴───────────┘                                     │
│                                                                 │
│  模式3: 全部显示 (虚拟桌面)                                     │
│  ┌─────────────────────────────────────────────┐               │
│  │   显示器1    │    显示器2    │    显示器3   │               │
│  │              │               │              │               │
│  └─────────────────────────────────────────────┘               │
│  按照实际布局显示，支持滚动和缩放                               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 18. 里程碑规划

### Phase 1: 基础框架 (4周)
- 项目脚手架搭建
- C++ Native DLL 基础结构
- MsQuic 网络层封装
- 基础消息协议

### Phase 2: 核心功能 (6周)
- 屏幕捕获 + 编码
- 解码 + 渲染
- 输入注入
- 基础会话管理

### Phase 3: 完善功能 (4周)
- 音频传输
- 文件传输
- 剪贴板同步
- 多显示器支持

### Phase 4: 增强功能 (4周)
- 会话录制
- 聊天功能
- 监控上报
- P2P穿透

### Phase 5: 优化发布 (2周)
- 性能优化
- 测试完善
- 文档编写
- 发布准备

---

## 附录

### A. 技术依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| .NET | 8.0 | 应用框架 |
| MsQuic | 2.x | QUIC通信 |
| FFmpeg | 6.x | 软件编解码 |
| Google Test | 1.x | C++测试 |
| xUnit | 2.x | C#测试 |
| BenchmarkDotNet | 0.13.x | 性能测试 |

### B. 参考资料

- [MsQuic Documentation](https://github.com/microsoft/msquic)
- [DXGI Desktop Duplication API](https://docs.microsoft.com/en-us/windows/win32/direct3darticles/desktop-dup-api)
- [NVENC Programming Guide](https://developer.nvidia.com/nvidia-video-codec-sdk)
- [QUIC Protocol RFC 9000](https://www.rfc-editor.org/rfc/rfc9000)

### C. 术语表

| 术语 | 英文 | 说明 |
|------|------|------|
| QUIC | Quick UDP Internet Connections | 基于UDP的新一代传输协议，集成TLS 1.3 |
| P2P | Peer-to-Peer | 点对点直连通信 |
| NAT | Network Address Translation | 网络地址转换，用于内网地址映射 |
| STUN | Session Traversal Utilities for NAT | NAT穿透辅助协议 |
| TURN | Traversal Using Relays around NAT | 中继穿透协议 |
| ICE | Interactive Connectivity Establishment | 交互式连接建立框架 |
| NVENC | NVIDIA Encoder | NVIDIA硬件视频编码器 |
| NVDEC | NVIDIA Decoder | NVIDIA硬件视频解码器 |
| AMF | Advanced Media Framework | AMD媒体框架 |
| QSV | Quick Sync Video | Intel快速同步视频技术 |
| DXGI | DirectX Graphics Infrastructure | DirectX图形基础设施 |
| GOP | Group of Pictures | 图像组，视频编码中的帧序列 |
| NAL | Network Abstraction Layer | 网络抽象层，H.264/H.265编码单元 |
| DPI | Dots Per Inch | 每英寸点数，显示缩放比例 |
| WASAPI | Windows Audio Session API | Windows音频会话接口 |

### D. 文档修订记录

| 版本 | 日期 | 修订人 | 说明 |
|------|------|--------|------|
| 1.0 | 2026-03-21 | Claude | 初稿完成 |
| 1.1 | 2026-03-21 | Claude | 补充错误码定义、内存生命周期、日志系统、配置管理、中继服务器、编码器热切换、P2P目标、多显示器详细设计、术语表 |
