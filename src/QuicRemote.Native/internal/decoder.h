#pragma once

#include "../include/quicremote.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <memory>
#include <vector>

namespace QuicRemote {
namespace Decoder {

using Microsoft::WRL::ComPtr;

// 解码器抽象接口
class IDecoder {
public:
    virtual ~IDecoder() = default;

    virtual QR_Result Initialize(const QR_DecoderConfig& config, ID3D11Device* device) = 0;
    virtual QR_Result Decode(QR_Packet* packet, QR_Frame** frame) = 0;
    virtual QR_Result Reset() = 0;
    virtual void GetStats(int* fps, float* latency_ms) = 0;
    virtual void Shutdown() = 0;

    virtual bool IsHardwareAccelerated() const = 0;
    virtual const char* GetName() const = 0;
};

// 解码器句柄实现
struct DecoderHandle {
    std::unique_ptr<IDecoder> decoder;
    QR_DecoderConfig config;
    int64_t frames_decoded;
    int64_t total_latency_us;
    int last_fps;
    float last_latency_ms;
    std::unique_ptr<QR_Frame> output_frame;
};

// 解码器工厂
class DecoderFactory {
public:
    // 检测可用的解码器类型
    static QR_Result DetectBestDecoder();

    // 创建解码器
    static std::unique_ptr<IDecoder> Create(bool hardware_accelerated);

    // NVDEC 解码器 (NVIDIA)
    static std::unique_ptr<IDecoder> CreateNVDECDecoder();

    // D3D11 Video Decoder
    static std::unique_ptr<IDecoder> CreateD3D11Decoder();

    // QSV 解码器 (Intel)
    static std::unique_ptr<IDecoder> CreateQSVDecoder();

    // 软件解码器 (FFmpeg)
    static std::unique_ptr<IDecoder> CreateSoftwareDecoder();
};

// NVDEC 解码器实现声明
class NVDECDecoder : public IDecoder {
public:
    NVDECDecoder();
    ~NVDECDecoder() override;

    QR_Result Initialize(const QR_DecoderConfig& config, ID3D11Device* device) override;
    QR_Result Decode(QR_Packet* packet, QR_Frame** frame) override;
    QR_Result Reset() override;
    void GetStats(int* fps, float* latency_ms) override;
    void Shutdown() override;

    bool IsHardwareAccelerated() const override { return true; }
    const char* GetName() const override { return "NVDEC"; }

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// D3D11 Video Decoder 实现声明
class D3D11Decoder : public IDecoder {
public:
    D3D11Decoder();
    ~D3D11Decoder() override;

    QR_Result Initialize(const QR_DecoderConfig& config, ID3D11Device* device) override;
    QR_Result Decode(QR_Packet* packet, QR_Frame** frame) override;
    QR_Result Reset() override;
    void GetStats(int* fps, float* latency_ms) override;
    void Shutdown() override;

    bool IsHardwareAccelerated() const override { return true; }
    const char* GetName() const override { return "D3D11"; }

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// 软件解码器实现声明 (FFmpeg)
class SoftwareDecoder : public IDecoder {
public:
    SoftwareDecoder();
    ~SoftwareDecoder() override;

    QR_Result Initialize(const QR_DecoderConfig& config, ID3D11Device* device) override;
    QR_Result Decode(QR_Packet* packet, QR_Frame** frame) override;
    QR_Result Reset() override;
    void GetStats(int* fps, float* latency_ms) override;
    void Shutdown() override;

    bool IsHardwareAccelerated() const override { return false; }
    const char* GetName() const override { return "Software"; }

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// 解码器管理器
class DecoderManager {
public:
    DecoderManager();
    ~DecoderManager();

    QR_Result Create(const QR_DecoderConfig& config, DecoderHandle** handle);
    QR_Result Decode(DecoderHandle* handle, QR_Packet* packet, QR_Frame** frame);
    QR_Result Reset(DecoderHandle* handle);
    QR_Result GetStats(DecoderHandle* handle, int* fps, float* latency_ms);
    QR_Result Destroy(DecoderHandle* handle);

private:
    ComPtr<ID3D11Device> shared_device_;
};

// 全局实例
DecoderManager& GetDecoderManager();

} // namespace Decoder
} // namespace QuicRemote
