#include "../internal/encoder.h"
#include "../internal/d3d_utils.h"

#include <chrono>
#include <cstring>
#include <algorithm>
#include <atomic>

// FFmpeg support (conditional compilation)
#ifdef QR_USE_FFMPEG
extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
}
#endif

namespace QuicRemote {
namespace Encoder {

using namespace QuicRemote::Utils;

// ============================================================================
// SoftwareEncoder Implementation
// ============================================================================

struct SoftwareEncoder::Impl {
    QR_EncoderConfig config;
    bool initialized = false;
    bool force_keyframe = false;

    // Frame buffer for encoding
    std::vector<uint8_t> frame_buffer;
    std::vector<uint8_t> converted_buffer;

    // Statistics
    std::atomic<int64_t> frames_encoded{0};
    std::atomic<int64_t> total_encode_time_us{0};
    std::atomic<int64_t> total_bytes_encoded{0};
    int64_t last_encode_time_us = 0;
    int64_t last_keyframe_time_us = 0;
    int frame_counter = 0;

    // Output packet pool
    std::vector<std::unique_ptr<QR_Packet>> packet_pool;

#ifdef QR_USE_FFMPEG
    // FFmpeg contexts
    const AVCodec* codec = nullptr;
    AVCodecContext* codec_ctx = nullptr;
    AVFrame* frame = nullptr;
    AVPacket* pkt = nullptr;
    SwsContext* sws_ctx = nullptr;

    // Pixel format conversion
    AVPixelFormat src_pix_fmt = AV_PIX_FMT_BGRA;
    AVPixelFormat dst_pix_fmt = AV_PIX_FMT_YUV420P;
#endif

    Impl() = default;
    ~Impl() { Cleanup(); }

    void Cleanup() {
#ifdef QR_USE_FFMPEG
        if (sws_ctx) {
            sws_freeContext(sws_ctx);
            sws_ctx = nullptr;
        }
        if (pkt) {
            av_packet_free(&pkt);
            pkt = nullptr;
        }
        if (frame) {
            av_frame_free(&frame);
            frame = nullptr;
        }
        if (codec_ctx) {
            avcodec_free_context(&codec_ctx);
            codec_ctx = nullptr;
        }
#endif
        initialized = false;
    }

    QR_Packet* AllocatePacket() {
        // Try to reuse packet from pool
        for (auto& p : packet_pool) {
            if (p && !p->internal) {  // internal used as "in-use" flag
                p->internal = reinterpret_cast<void*>(1);
                return p.get();
            }
        }
        // Create new packet
        auto new_packet = std::make_unique<QR_Packet>();
        new_packet->internal = reinterpret_cast<void*>(1);
        packet_pool.push_back(std::move(new_packet));
        return packet_pool.back().get();
    }

    void ReleasePacket(QR_Packet* packet) {
        if (packet) {
            packet->internal = nullptr;
            // Free data buffer
            if (packet->data) {
                delete[] static_cast<uint8_t*>(packet->data);
                packet->data = nullptr;
            }
            packet->size = 0;
        }
    }
};

SoftwareEncoder::SoftwareEncoder() : impl_(std::make_unique<Impl>()) {}

SoftwareEncoder::~SoftwareEncoder() {
    Shutdown();
}

QR_Result SoftwareEncoder::Initialize(const QR_EncoderConfig& config, ID3D11Device* device) {
    if (impl_->initialized) {
        return QR_Error_AlreadyInitialized;
    }

    if (config.width <= 0 || config.height <= 0 ||
        config.bitrate_kbps <= 0 || config.framerate <= 0) {
        return QR_Error_InvalidParam;
    }

    impl_->config = config;

#ifdef QR_USE_FFMPEG
    // Initialize FFmpeg encoder
    AVCodecID codec_id = AV_CODEC_ID_H264;
    if (config.codec == QR_Codec_H265) {
        codec_id = AV_CODEC_ID_H265;
    }

    // Find encoder
    impl_->codec = avcodec_find_encoder(codec_id);
    if (!impl_->codec) {
        // Fallback to H264 if H265 not available
        if (codec_id == AV_CODEC_ID_H265) {
            impl_->codec = avcodec_find_encoder(AV_CODEC_ID_H264);
        }
        if (!impl_->codec) {
            return QR_Error_EncoderNotSupported;
        }
    }

    // Create codec context
    impl_->codec_ctx = avcodec_alloc_context3(impl_->codec);
    if (!impl_->codec_ctx) {
        return QR_Error_EncoderCreateFailed;
    }

    // Configure for low latency real-time encoding
    impl_->codec_ctx->width = config.width;
    impl_->codec_ctx->height = config.height;
    impl_->codec_ctx->time_base = { 1, 1000000 };  // microseconds
    impl_->codec_ctx->framerate = { config.framerate, 1 };
    impl_->codec_ctx->bit_rate = config.bitrate_kbps * 1000;
    impl_->codec_ctx->gop_size = config.gop_size > 0 ? config.gop_size : config.framerate * 2;
    impl_->codec_ctx->max_b_frames = 0;  // No B-frames for low latency
    impl_->codec_ctx->pix_fmt = impl_->dst_pix_fmt;

    // Set preset based on quality setting
    const char* preset = "veryfast";
    switch (config.quality_preset) {
        case 0: preset = "slow"; break;
        case 1: preset = "medium"; break;
        case 2: preset = "fast"; break;
        case 3: preset = "veryfast"; break;
        case 4: preset = "ultrafast"; break;
    }

    // Rate control
    if (config.rate_control == QR_RateControl_CBR) {
        av_opt_set(impl_->codec_ctx->priv_data, "rc-mode", "cbr", 0);
        impl_->codec_ctx->rc_max_rate = config.bitrate_kbps * 1000;
        impl_->codec_ctx->rc_min_rate = config.bitrate_kbps * 1000;
        impl_->codec_ctx->rc_buffer_size = config.bitrate_kbps * 1000 / config.framerate * 2;
    } else if (config.rate_control == QR_RateControl_VBR) {
        impl_->codec_ctx->rc_max_rate = config.bitrate_kbps * 1500;  // 150% target
        impl_->codec_ctx->rc_buffer_size = config.bitrate_kbps * 1000 / config.framerate * 2;
    }

    // Low latency settings
    if (config.low_latency) {
        av_opt_set(impl_->codec_ctx->priv_data, "tune", "zerolatency", 0);
        av_opt_set(impl_->codec_ctx->priv_data, "preset", preset, 0);
        impl_->codec_ctx->thread_count = 1;  // Single thread for minimal latency
    } else {
        av_opt_set(impl_->codec_ctx->priv_data, "preset", preset, 0);
        impl_->codec_ctx->thread_count = 0;  // Auto
    }

    // Open codec
    int ret = avcodec_open2(impl_->codec_ctx, impl_->codec, nullptr);
    if (ret < 0) {
        impl_->Cleanup();
        return QR_Error_EncoderCreateFailed;
    }

    // Allocate frame
    impl_->frame = av_frame_alloc();
    if (!impl_->frame) {
        impl_->Cleanup();
        return QR_Error_OutOfMemory;
    }
    impl_->frame->format = impl_->dst_pix_fmt;
    impl_->frame->width = config.width;
    impl_->frame->height = config.height;
    ret = av_frame_get_buffer(impl_->frame, 0);
    if (ret < 0) {
        impl_->Cleanup();
        return QR_Error_OutOfMemory;
    }

    // Allocate packet
    impl_->pkt = av_packet_alloc();
    if (!impl_->pkt) {
        impl_->Cleanup();
        return QR_Error_OutOfMemory;
    }

    // Initialize swscale context for format conversion
    impl_->sws_ctx = sws_getContext(
        config.width, config.height, impl_->src_pix_fmt,
        config.width, config.height, impl_->dst_pix_fmt,
        SWS_BILINEAR, nullptr, nullptr, nullptr
    );
    if (!impl_->sws_ctx) {
        impl_->Cleanup();
        return QR_Error_EncoderCreateFailed;
    }

#endif  // QR_USE_FFMPEG

    // Allocate frame buffer for CPU copy
    impl_->frame_buffer.resize(config.width * config.height * 4);  // BGRA

    impl_->initialized = true;
    return QR_Success;
}

QR_Result SoftwareEncoder::Encode(QR_Frame* frame, QR_Packet** packet) {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }
    if (!frame || !packet) {
        return QR_Error_InvalidParam;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    // Get frame data from D3D11 texture or system memory
    uint8_t* frame_data = nullptr;
    int stride = 0;

    if (frame->texture && frame->device) {
        // Copy from D3D11 texture to system memory
        ID3D11Texture2D* texture = static_cast<ID3D11Texture2D*>(frame->texture);
        ID3D11Device* device = static_cast<ID3D11Device*>(frame->device);

        ComPtr<ID3D11DeviceContext> context;
        device->GetImmediateContext(&context);

        // Create staging texture if needed
        D3D11_TEXTURE2D_DESC desc;
        texture->GetDesc(&desc);

        ComPtr<ID3D11Texture2D> staging;
        D3D11_TEXTURE2D_DESC staging_desc = {};
        staging_desc.Width = desc.Width;
        staging_desc.Height = desc.Height;
        staging_desc.Format = desc.Format;
        staging_desc.MipLevels = 1;
        staging_desc.ArraySize = 1;
        staging_desc.SampleDesc.Count = 1;
        staging_desc.Usage = D3D11_USAGE_STAGING;
        staging_desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

        HRESULT hr = device->CreateTexture2D(&staging_desc, nullptr, &staging);
        if (FAILED(hr)) {
            return QR_Error_EncoderEncodeFailed;
        }

        // Copy and map
        context->CopyResource(staging.Get(), texture);

        D3D11_MAPPED_SUBRESOURCE mapped;
        hr = context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr)) {
            return QR_Error_EncoderEncodeFailed;
        }

        // Copy to frame buffer
        uint8_t* src = static_cast<uint8_t*>(mapped.pData);
        uint8_t* dst = impl_->frame_buffer.data();
        int src_stride = mapped.RowPitch;
        int dst_stride = frame->width * 4;

        for (int y = 0; y < frame->height; y++) {
            memcpy(dst + y * dst_stride, src + y * src_stride, dst_stride);
        }

        context->Unmap(staging.Get(), 0);

        frame_data = impl_->frame_buffer.data();
        stride = dst_stride;
    } else if (frame->data) {
        // Use system memory directly
        frame_data = static_cast<uint8_t*>(frame->data);
        stride = frame->stride > 0 ? frame->stride : frame->width * 4;
    } else {
        return QR_Error_InvalidParam;
    }

    QR_Packet* out_packet = impl_->AllocatePacket();
    if (!out_packet) {
        return QR_Error_OutOfMemory;
    }

#ifdef QR_USE_FFMPEG
    // Convert to YUV420P
    int ret = av_frame_make_writable(impl_->frame);
    if (ret < 0) {
        impl_->ReleasePacket(out_packet);
        return QR_Error_EncoderEncodeFailed;
    }

    const uint8_t* src_data[1] = { frame_data };
    int src_linesize[1] = { stride };

    sws_scale(impl_->sws_ctx,
              src_data, src_linesize, 0, frame->height,
              impl_->frame->data, impl_->frame->linesize);

    // Set frame properties
    impl_->frame->pts = frame->timestamp_us;

    // Force keyframe if requested
    if (impl_->force_keyframe) {
        impl_->frame->pict_type = AV_PICTURE_TYPE_I;
        impl_->force_keyframe = false;
    }

    // Send frame to encoder
    ret = avcodec_send_frame(impl_->codec_ctx, impl_->frame);
    if (ret < 0) {
        impl_->ReleasePacket(out_packet);
        return QR_Error_EncoderEncodeFailed;
    }

    // Receive encoded packet
    ret = avcodec_receive_packet(impl_->codec_ctx, impl_->pkt);
    if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
        impl_->ReleasePacket(out_packet);
        return QR_Error_EncoderEncodeFailed;
    } else if (ret < 0) {
        impl_->ReleasePacket(out_packet);
        return QR_Error_EncoderEncodeFailed;
    }

    // Copy encoded data to output packet
    out_packet->size = impl_->pkt->size;
    out_packet->data = new uint8_t[out_packet->size];
    memcpy(out_packet->data, impl_->pkt->data, out_packet->size);
    out_packet->timestamp_us = impl_->pkt->pts;
    out_packet->is_keyframe = (impl_->pkt->flags & AV_PKT_FLAG_KEY) != 0;
    out_packet->frame_num = impl_->frame_counter++;

    impl_->total_bytes_encoded += out_packet->size;

    av_packet_unref(impl_->pkt);

#else
    // Placeholder implementation (no FFmpeg)
    // Create a dummy packet for testing
    int dummy_size = impl_->config.width * impl_->config.height / 10;  // Simulated compressed size
    out_packet->data = new uint8_t[dummy_size];
    memset(out_packet->data, 0, dummy_size);
    out_packet->size = dummy_size;
    out_packet->timestamp_us = frame->timestamp_us;
    out_packet->is_keyframe = (impl_->frame_counter % impl_->config.gop_size == 0) || impl_->force_keyframe;
    out_packet->frame_num = impl_->frame_counter++;

    if (impl_->force_keyframe) {
        impl_->force_keyframe = false;
    }

    impl_->total_bytes_encoded += out_packet->size;
#endif

    auto end_time = std::chrono::high_resolution_clock::now();
    auto encode_time = std::chrono::duration_cast<std::chrono::microseconds>(end_time - start_time);

    impl_->frames_encoded++;
    impl_->total_encode_time_us += encode_time.count();
    impl_->last_encode_time_us = encode_time.count();

    if (out_packet->is_keyframe) {
        impl_->last_keyframe_time_us = frame->timestamp_us;
    }

    *packet = out_packet;
    return QR_Success;
}

QR_Result SoftwareEncoder::RequestKeyframe() {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }
    impl_->force_keyframe = true;
    return QR_Success;
}

QR_Result SoftwareEncoder::Reconfigure(int bitrate_kbps) {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }
    if (bitrate_kbps <= 0) {
        return QR_Error_InvalidParam;
    }

    impl_->config.bitrate_kbps = bitrate_kbps;

#ifdef QR_USE_FFMPEG
    // Update bitrate
    impl_->codec_ctx->bit_rate = bitrate_kbps * 1000;
    if (impl_->config.rate_control == QR_RateControl_CBR) {
        impl_->codec_ctx->rc_max_rate = bitrate_kbps * 1000;
        impl_->codec_ctx->rc_min_rate = bitrate_kbps * 1000;
    }

    // Reconfigure encoder (x264 supports reconfiguration)
    int ret = avcodec_send_frame(impl_->codec_ctx, nullptr);  // Flush
    if (ret < 0) {
        return QR_Error_EncoderReconfigureFailed;
    }
#endif

    return QR_Success;
}

void SoftwareEncoder::GetStats(int* bitrate, int* fps, float* latency_ms) {
    if (bitrate) {
        if (impl_->frames_encoded > 0 && impl_->total_encode_time_us > 0) {
            double time_seconds = impl_->total_encode_time_us / 1000000.0;
            *bitrate = static_cast<int>((impl_->total_bytes_encoded * 8 / 1000.0) / time_seconds);
        } else {
            *bitrate = 0;
        }
    }

    if (fps) {
        if (impl_->total_encode_time_us > 0) {
            double time_seconds = impl_->total_encode_time_us / 1000000.0;
            *fps = static_cast<int>(impl_->frames_encoded / time_seconds);
        } else {
            *fps = 0;
        }
    }

    if (latency_ms) {
        if (impl_->frames_encoded > 0) {
            *latency_ms = static_cast<float>(impl_->last_encode_time_us) / 1000.0f;
        } else {
            *latency_ms = 0.0f;
        }
    }
}

void SoftwareEncoder::Shutdown() {
    impl_->Cleanup();

    // Cleanup packet pool
    for (auto& p : impl_->packet_pool) {
        if (p && p->data) {
            delete[] static_cast<uint8_t*>(p->data);
        }
    }
    impl_->packet_pool.clear();
    impl_->frame_buffer.clear();
}

// ============================================================================
// NVENC Encoder (Placeholder)
// ============================================================================

struct NVENCEncoder::Impl {
    QR_EncoderConfig config;
    bool initialized = false;
    std::atomic<int64_t> frames_encoded{0};
    std::vector<std::unique_ptr<QR_Packet>> packet_pool;
    int frame_counter = 0;
    bool force_keyframe = false;
    int64_t last_encode_time_us = 0;
};

NVENCEncoder::NVENCEncoder() : impl_(std::make_unique<Impl>()) {}
NVENCEncoder::~NVENCEncoder() { Shutdown(); }

QR_Result NVENCEncoder::Initialize(const QR_EncoderConfig& config, ID3D11Device* device) {
    if (config.encoder_type != QR_EncoderType_NVENC && config.encoder_type != QR_EncoderType_Auto) {
        return QR_Error_EncoderNotSupported;
    }

    // TODO: Implement NVENC using NVIDIA Video Codec SDK
    // For now, return not supported
    impl_->config = config;
    impl_->initialized = false;
    return QR_Error_EncoderNotSupported;
}

QR_Result NVENCEncoder::Encode(QR_Frame* frame, QR_Packet** packet) {
    return QR_Error_EncoderNotSupported;
}

QR_Result NVENCEncoder::RequestKeyframe() {
    impl_->force_keyframe = true;
    return QR_Success;
}

QR_Result NVENCEncoder::Reconfigure(int bitrate_kbps) {
    return QR_Error_EncoderNotSupported;
}

void NVENCEncoder::GetStats(int* bitrate, int* fps, float* latency_ms) {
    if (bitrate) *bitrate = 0;
    if (fps) *fps = 0;
    if (latency_ms) *latency_ms = 0.0f;
}

void NVENCEncoder::Shutdown() {
    impl_->initialized = false;
}

// ============================================================================
// EncoderFactory Implementation
// ============================================================================

int EncoderFactory::GetAvailableCount(QR_EncoderType type) {
    switch (type) {
        case QR_EncoderType_Auto:
            return GetAvailableCount(QR_EncoderType_NVENC) +
                   GetAvailableCount(QR_EncoderType_AMF) +
                   GetAvailableCount(QR_EncoderType_QSV) +
                   GetAvailableCount(QR_EncoderType_Software);

        case QR_EncoderType_NVENC: {
            // Check for NVIDIA GPU
            ComPtr<IDXGIFactory> factory;
            if (FAILED(CreateDXGIFactory(__uuidof(IDXGIFactory), &factory))) {
                return 0;
            }

            ComPtr<IDXGIAdapter> adapter;
            int count = 0;
            for (UINT i = 0; SUCCEEDED(factory->EnumAdapters(i, &adapter)); i++) {
                DXGI_ADAPTER_DESC desc;
                if (SUCCEEDED(adapter->GetDesc(&desc))) {
                    // Check if NVIDIA (Vendor ID: 0x10DE)
                    if (desc.VendorId == 0x10DE) {
                        count++;
                    }
                }
            }
            return count;
        }

        case QR_EncoderType_AMF: {
            // Check for AMD GPU
            ComPtr<IDXGIFactory> factory;
            if (FAILED(CreateDXGIFactory(__uuidof(IDXGIFactory), &factory))) {
                return 0;
            }

            ComPtr<IDXGIAdapter> adapter;
            int count = 0;
            for (UINT i = 0; SUCCEEDED(factory->EnumAdapters(i, &adapter)); i++) {
                DXGI_ADAPTER_DESC desc;
                if (SUCCEEDED(adapter->GetDesc(&desc))) {
                    // Check if AMD (Vendor ID: 0x1002)
                    if (desc.VendorId == 0x1002) {
                        count++;
                    }
                }
            }
            return count;
        }

        case QR_EncoderType_QSV: {
            // Check for Intel GPU
            ComPtr<IDXGIFactory> factory;
            if (FAILED(CreateDXGIFactory(__uuidof(IDXGIFactory), &factory))) {
                return 0;
            }

            ComPtr<IDXGIAdapter> adapter;
            int count = 0;
            for (UINT i = 0; SUCCEEDED(factory->EnumAdapters(i, &adapter)); i++) {
                DXGI_ADAPTER_DESC desc;
                if (SUCCEEDED(adapter->GetDesc(&desc))) {
                    // Check if Intel (Vendor ID: 0x8086)
                    if (desc.VendorId == 0x8086) {
                        count++;
                    }
                }
            }
            return count;
        }

        case QR_EncoderType_Software:
            return 1;  // Software encoder is always available

        default:
            return 0;
    }
}

QR_EncoderType EncoderFactory::DetectBestEncoder() {
    // Priority: NVENC > AMF > QSV > Software

    if (GetAvailableCount(QR_EncoderType_NVENC) > 0) {
        return QR_EncoderType_NVENC;
    }

    if (GetAvailableCount(QR_EncoderType_AMF) > 0) {
        return QR_EncoderType_AMF;
    }

    if (GetAvailableCount(QR_EncoderType_QSV) > 0) {
        return QR_EncoderType_QSV;
    }

    return QR_EncoderType_Software;
}

std::unique_ptr<IEncoder> EncoderFactory::Create(QR_EncoderType type) {
    if (type == QR_EncoderType_Auto) {
        type = DetectBestEncoder();
    }

    switch (type) {
        case QR_EncoderType_NVENC:
            return CreateNVENCEncoder();

        case QR_EncoderType_AMF:
            return CreateAMFEncoder();

        case QR_EncoderType_QSV:
            return CreateQSVEncoder();

        case QR_EncoderType_Software:
        default:
            return CreateSoftwareEncoder();
    }
}

std::unique_ptr<IEncoder> EncoderFactory::CreateNVENCEncoder() {
    return std::make_unique<NVENCEncoder>();
}

std::unique_ptr<IEncoder> EncoderFactory::CreateAMFEncoder() {
    // TODO: Implement AMF encoder
    return nullptr;
}

std::unique_ptr<IEncoder> EncoderFactory::CreateQSVEncoder() {
    // TODO: Implement QSV encoder
    return nullptr;
}

std::unique_ptr<IEncoder> EncoderFactory::CreateSoftwareEncoder() {
    return std::make_unique<SoftwareEncoder>();
}

// ============================================================================
// EncoderManager Implementation
// ============================================================================

EncoderManager::EncoderManager() = default;
EncoderManager::~EncoderManager() = default;

QR_Result EncoderManager::Create(const QR_EncoderConfig& config, EncoderHandle** handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    QR_EncoderType encoder_type = config.encoder_type;
    if (encoder_type == QR_EncoderType_Auto) {
        encoder_type = EncoderFactory::DetectBestEncoder();
    }

    // Create encoder
    auto encoder = EncoderFactory::Create(encoder_type);
    if (!encoder) {
        return QR_Error_EncoderNotSupported;
    }

    // Initialize encoder
    QR_Result result = encoder->Initialize(config, shared_device_.Get());
    if (result != QR_Success) {
        // If hardware encoder fails, fall back to software
        if (encoder_type != QR_EncoderType_Software) {
            encoder = EncoderFactory::Create(QR_EncoderType_Software);
            if (!encoder) {
                return QR_Error_EncoderNotSupported;
            }
            result = encoder->Initialize(config, shared_device_.Get());
            if (result != QR_Success) {
                return result;
            }
        } else {
            return result;
        }
    }

    // Create handle
    auto* new_handle = new EncoderHandle();
    new_handle->encoder = std::move(encoder);
    new_handle->config = config;
    new_handle->frames_encoded = 0;
    new_handle->total_latency_us = 0;
    new_handle->last_bitrate = config.bitrate_kbps;
    new_handle->last_fps = 0;
    new_handle->last_latency_ms = 0.0f;

    *handle = new_handle;
    return QR_Success;
}

QR_Result EncoderManager::Encode(EncoderHandle* handle, QR_Frame* frame, QR_Packet** packet) {
    if (!handle || !handle->encoder || !frame || !packet) {
        return QR_Error_InvalidParam;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    QR_Result result = handle->encoder->Encode(frame, packet);

    auto end_time = std::chrono::high_resolution_clock::now();
    auto latency = std::chrono::duration_cast<std::chrono::microseconds>(end_time - start_time);

    if (result == QR_Success) {
        handle->frames_encoded++;
        handle->total_latency_us += latency.count();
    }

    return result;
}

QR_Result EncoderManager::RequestKeyframe(EncoderHandle* handle) {
    if (!handle || !handle->encoder) {
        return QR_Error_InvalidParam;
    }
    return handle->encoder->RequestKeyframe();
}

QR_Result EncoderManager::Reconfigure(EncoderHandle* handle, int bitrate_kbps) {
    if (!handle || !handle->encoder) {
        return QR_Error_InvalidParam;
    }

    QR_Result result = handle->encoder->Reconfigure(bitrate_kbps);
    if (result == QR_Success) {
        handle->config.bitrate_kbps = bitrate_kbps;
        handle->last_bitrate = bitrate_kbps;
    }
    return result;
}

QR_Result EncoderManager::GetStats(EncoderHandle* handle, int* bitrate, int* fps, float* latency_ms) {
    if (!handle || !handle->encoder) {
        return QR_Error_InvalidParam;
    }

    handle->encoder->GetStats(bitrate, fps, latency_ms);

    if (bitrate) handle->last_bitrate = *bitrate;
    if (fps) handle->last_fps = *fps;
    if (latency_ms) handle->last_latency_ms = *latency_ms;

    return QR_Success;
}

QR_Result EncoderManager::Destroy(EncoderHandle* handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    if (handle->encoder) {
        handle->encoder->Shutdown();
        handle->encoder.reset();
    }

    delete handle;
    return QR_Success;
}

// Global instance
EncoderManager& GetEncoderManager() {
    static EncoderManager instance;
    return instance;
}

} // namespace Encoder
} // namespace QuicRemote

// ============================================================================
// C API Implementation
// ============================================================================

extern "C" {

QR_API int QR_Encoder_GetAvailableCount(QR_EncoderType type) {
    return QuicRemote::Encoder::EncoderFactory::GetAvailableCount(type);
}

QR_API QR_Result QR_Encoder_Create(QR_EncoderConfig* config, QR_EncoderHandle* handle) {
    if (!config || !handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Encoder::GetEncoderManager();
    QuicRemote::Encoder::EncoderHandle* internal_handle = nullptr;

    QR_Result result = manager.Create(*config, &internal_handle);
    if (result == QR_Success) {
        *handle = reinterpret_cast<QR_EncoderHandle>(internal_handle);
    }

    return result;
}

QR_API QR_Result QR_Encoder_Encode(QR_EncoderHandle handle, QR_Frame* frame, QR_Packet** packet) {
    if (!handle || !frame || !packet) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Encoder::GetEncoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle);

    return manager.Encode(internal_handle, frame, packet);
}

QR_API QR_Result QR_Encoder_RequestKeyframe(QR_EncoderHandle handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Encoder::GetEncoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle);

    return manager.RequestKeyframe(internal_handle);
}

QR_API QR_Result QR_Encoder_Reconfigure(QR_EncoderHandle handle, int bitrate_kbps) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Encoder::GetEncoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle);

    return manager.Reconfigure(internal_handle, bitrate_kbps);
}

QR_API QR_Result QR_Encoder_GetStats(QR_EncoderHandle handle, int* bitrate, int* fps, float* latency_ms) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Encoder::GetEncoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle);

    return manager.GetStats(internal_handle, bitrate, fps, latency_ms);
}

QR_API QR_Result QR_Encoder_Destroy(QR_EncoderHandle handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Encoder::GetEncoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle);

    return manager.Destroy(internal_handle);
}

} // extern "C"
