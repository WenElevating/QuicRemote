# QuicRemote

基于 QUIC 协议的远程桌面控制软件，支持远程办公和游戏串流场景。

## 特性

- **低延迟通信**: 基于 MsQuic 协议，支持 P2P 直连和中继回退
- **多编码器支持**: H.264/H.265/VP9，优先使用硬件加速 (NVENC/AMF/QSV)
- **双模式场景**: 远程办公 (≤30ms) 和游戏串流 (≤20ms)
- **多显示器**: 支持多显示器选择和 DPI 缩放
- **会话管理**: 角色权限控制、断线自动恢复

## 架构

```
┌─────────────────────────────────────────────┐
│           WPF UI 层 (Host/Client)            │
├─────────────────────────────────────────────┤
│           C# 业务逻辑层 (Core)               │
│    会话管理 │ 远程控制 │ 恢复机制 │ 媒体     │
├─────────────────────────────────────────────┤
│           C# 网络通信层 (Network)            │
│         QUIC + 协议消息序列化                │
├─────────────────────────────────────────────┤
│        C++ Native DLL (QuicRemote.Native)    │
│  屏幕捕获 │ 编码器 │ 解码器 │ 输入注入 │ 音频│
└─────────────────────────────────────────────┘
```

## 项目结构

| 项目 | 说明 |
|------|------|
| QuicRemote.Core | 核心业务逻辑，Native DLL P/Invoke 绑定 |
| QuicRemote.Network | QUIC 网络通信，协议消息定义 |
| QuicRemote.Host | 被控端 WPF 应用 |
| QuicRemote.Client | 控制端 WPF 应用 |
| QuicRemote.Native | C++ 原生 DLL (屏幕捕获、编解码、输入注入) |

## 构建

### .NET 项目

```bash
dotnet build QuicRemote.sln
```

### Native DLL (Windows)

```bash
cd src/QuicRemote.Native
cmake -B build2 -DCMAKE_BUILD_TYPE=Release
cmake --build build2 --config Release
```

## 运行测试

```bash
dotnet test QuicRemote.sln
```

## 运行应用

```bash
# 被控端
dotnet run --project src/QuicRemote.Host/QuicRemote.Host.csproj

# 控制端
dotnet run --project src/QuicRemote.Client/QuicRemote.Client.csproj
```

## 技术栈

- **UI**: WPF + .NET 8
- **网络**: MsQuic (System.Net.Quic)
- **媒体**: DXGI Desktop Duplication, NVENC/AMF/QSV/FFmpeg
- **测试**: xUnit

## 许可证

MIT License
