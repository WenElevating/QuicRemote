#pragma once

#include "../include/quicremote.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <memory>
#include <vector>

namespace QuicRemote {
namespace Encoder {

using Microsoft::WRL::ComPtr;

// 编码器抽象接口
class IEncoder {
public:
    virtual ~IEncoder() = default;

    virtual QR_Result Initialize(const QR_EncoderConfig& config, ID3D11Device* device) = 0;
    virtual QR_Result Encode(QR_Frame* frame, QR_Packet** packet) = 0;
    virtual QR_Result RequestKeyframe() = 0;
    virtual QR_Result Reconfigure(int bitrate_kbps) = 0;
    virtual void GetStats(int* bitrate, int* fps, float* latency_ms) = 0;
    virtual void Shutdown() = 0;

    virtual bool IsHardwareAccelerated() const = 0;
    virtual const char* GetName() const = 0;
};

// 编码器句柄实现
struct EncoderHandle {
    std::unique_ptr<IEncoder> encoder;
    QR_EncoderConfig config;
    int64_t frames_encoded;
    int64_t total_latency_us;
    int last_bitrate;
    int last_fps;
    float last_latency_ms;
};

// 编码器工厂
class EncoderFactory {
public:
    // 检测可用的编码器类型
    static int GetAvailableCount(QR_EncoderType type);
    static QR_EncoderType DetectBestEncoder();

    // 创建编码器
    static std::unique_ptr<IEncoder> Create(QR_EncoderType type);

    // NVENC 编码器
    static std::unique_ptr<IEncoder> CreateNVENCEncoder();

    // AMF 编码器
    static std::unique_ptr<IEncoder> CreateAMFEncoder();

    // QSV 编码器
    static std::unique_ptr<IEncoder> CreateQSVEncoder();

    // 软件编码器
    static std::unique_ptr<IEncoder> CreateSoftwareEncoder();
};

// NVENC 编码器实现声明
class NVENCEncoder : public IEncoder {
public:
    NVENCEncoder();
    ~NVENCEncoder() override;

    QR_Result Initialize(const QR_EncoderConfig& config, ID3D11Device* device) override;
    QR_Result Encode(QR_Frame* frame, QR_Packet** packet) override;
    QR_Result RequestKeyframe() override;
    QR_Result Reconfigure(int bitrate_kbps) override;
    void GetStats(int* bitrate, int* fps, float* latency_ms) override;
    void Shutdown() override;

    bool IsHardwareAccelerated() const override { return true; }
    const char* GetName() const override { return "NVENC"; }

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// 软件编码器实现声明 (FFmpeg x264/x265)
class SoftwareEncoder : public IEncoder {
public:
    SoftwareEncoder();
    ~SoftwareEncoder() override;

    QR_Result Initialize(const QR_EncoderConfig& config, ID3D11Device* device) override;
    QR_Result Encode(QR_Frame* frame, QR_Packet** packet) override;
    QR_Result RequestKeyframe() override;
    QR_Result Reconfigure(int bitrate_kbps) override;
    void GetStats(int* bitrate, int* fps, float* latency_ms) override;
    void Shutdown() override;

    bool IsHardwareAccelerated() const override { return false; }
    const char* GetName() const override { return "Software"; }

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// 编码器管理器
class EncoderManager {
public:
    EncoderManager();
    ~EncoderManager();

    QR_Result Create(const QR_EncoderConfig& config, EncoderHandle** handle);
    QR_Result Encode(EncoderHandle* handle, QR_Frame* frame, QR_Packet** packet);
    QR_Result RequestKeyframe(EncoderHandle* handle);
    QR_Result Reconfigure(EncoderHandle* handle, int bitrate_kbps);
    QR_Result GetStats(EncoderHandle* handle, int* bitrate, int* fps, float* latency_ms);
    QR_Result Destroy(EncoderHandle* handle);

private:
    ComPtr<ID3D11Device> shared_device_;
};

// 全局实例
EncoderManager& GetEncoderManager();

} // namespace Encoder
} // namespace QuicRemote
