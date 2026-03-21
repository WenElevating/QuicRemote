#include "quicremote.h"
#include "../internal/decoder.h"
#include "../internal/d3d_utils.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <chrono>
#include <cstring>
#include <mutex>
#include <deque>
#include <atomic>

// FFmpeg headers (conditionally compiled)
#ifdef USE_FFMPEG
extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/imgutils.h>
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
}
#endif

namespace QuicRemote {
namespace Decoder {

using Microsoft::WRL::ComPtr;

// ============================================================================
// SoftwareDecoder Implementation (FFmpeg)
// ============================================================================

struct SoftwareDecoder::Impl {
    QR_DecoderConfig config;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> device_context;

    // Output frame
    int output_width = 0;
    int output_height = 0;
    QR_PixelFormat output_format = QR_PixelFormat_NV12;

    // Statistics
    std::atomic<int64_t> frames_decoded{0};
    std::atomic<int64_t> total_latency_us{0};
    std::deque<int64_t> recent_latencies;  // For FPS calculation
    std::chrono::steady_clock::time_point last_frame_time;

    // Frame buffer for output
    std::vector<uint8_t> frame_buffer;
    int frame_stride = 0;

#ifdef USE_FFMPEG
    const AVCodec* codec = nullptr;
    AVCodecContext* codec_ctx = nullptr;
    AVFrame* av_frame = nullptr;
    AVPacket* av_packet = nullptr;
#else
    // Placeholder data for non-FFmpeg builds
    std::vector<uint8_t> placeholder_frame;
#endif

    bool initialized = false;

    Impl() = default;
    ~Impl() { Shutdown(); }

    void Shutdown() {
#ifdef USE_FFMPEG
        if (av_frame) {
            av_frame_free(&av_frame);
            av_frame = nullptr;
        }
        if (av_packet) {
            av_packet_free(&av_packet);
            av_packet = nullptr;
        }
        if (codec_ctx) {
            avcodec_free_context(&codec_ctx);
            codec_ctx = nullptr;
        }
#endif
        initialized = false;
        frame_buffer.clear();
    }
};

SoftwareDecoder::SoftwareDecoder() : impl_(std::make_unique<Impl>()) {}

SoftwareDecoder::~SoftwareDecoder() = default;

QR_Result SoftwareDecoder::Initialize(const QR_DecoderConfig& config, ID3D11Device* device) {
    if (impl_->initialized) {
        return QR_Error_AlreadyInitialized;
    }

    if (config.max_width <= 0 || config.max_height <= 0) {
        return QR_Error_InvalidParam;
    }

    impl_->config = config;

    // Store device reference (may be null for software decoder)
    if (device) {
        impl_->device = device;
        device->GetImmediateContext(&impl_->device_context);
    }

#ifdef USE_FFMPEG
    // Find decoder based on codec type
    AVCodecID codec_id = AV_CODEC_ID_NONE;
    switch (config.codec) {
        case QR_Codec_H264:
            codec_id = AV_CODEC_ID_H264;
            break;
        case QR_Codec_H265:
            codec_id = AV_CODEC_ID_HEVC;
            break;
        case QR_Codec_VP9:
            codec_id = AV_CODEC_ID_VP9;
            break;
        default:
            return QR_Error_DecoderNotSupported;
    }

    impl_->codec = avcodec_find_decoder(codec_id);
    if (!impl_->codec) {
        return QR_Error_DecoderNotSupported;
    }

    impl_->codec_ctx = avcodec_alloc_context3(impl_->codec);
    if (!impl_->codec_ctx) {
        return QR_Error_OutOfMemory;
    }

    // Set decoder parameters
    impl_->codec_ctx->width = config.max_width;
    impl_->codec_ctx->height = config.max_height;
    impl_->codec_ctx->pix_fmt = AV_PIX_FMT_NV12;  // Default output format

    // Enable low latency decoding
    impl_->codec_ctx->flags |= AV_CODEC_FLAG_LOW_DELAY;
    impl_->codec_ctx->thread_count = 4;  // Use multiple threads for software decoding

    int ret = avcodec_open2(impl_->codec_ctx, impl_->codec, nullptr);
    if (ret < 0) {
        avcodec_free_context(&impl_->codec_ctx);
        return QR_Error_DecoderCreateFailed;
    }

    // Allocate frame and packet
    impl_->av_frame = av_frame_alloc();
    impl_->av_packet = av_packet_alloc();
    if (!impl_->av_frame || !impl_->av_packet) {
        av_frame_free(&impl_->av_frame);
        av_packet_free(&impl_->av_packet);
        avcodec_free_context(&impl_->codec_ctx);
        return QR_Error_OutOfMemory;
    }
#endif

    impl_->initialized = true;
    impl_->last_frame_time = std::chrono::steady_clock::now();

    return QR_Success;
}

QR_Result SoftwareDecoder::Decode(QR_Packet* packet, QR_Frame** frame) {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }

    if (!packet || !packet->data || packet->size <= 0 || !frame) {
        return QR_Error_InvalidParam;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

#ifdef USE_FFMPEG
    // Prepare packet
    impl_->av_packet->data = static_cast<uint8_t*>(packet->data);
    impl_->av_packet->size = packet->size;
    impl_->av_packet->pts = packet->timestamp_us;
    impl_->av_packet->flags = packet->is_keyframe ? AV_PKT_FLAG_KEY : 0;

    // Send packet to decoder
    int ret = avcodec_send_packet(impl_->codec_ctx, impl_->av_packet);
    if (ret < 0) {
        return QR_Error_DecoderDecodeFailed;
    }

    // Receive decoded frame
    ret = avcodec_receive_frame(impl_->codec_ctx, impl_->av_frame);
    if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
        // Need more data
        *frame = nullptr;
        return QR_Success;
    } else if (ret < 0) {
        return QR_Error_DecoderDecodeFailed;
    }

    // Update output dimensions
    impl_->output_width = impl_->av_frame->width;
    impl_->output_height = impl_->av_frame->height;

    // Convert pixel format
    switch (impl_->av_frame->format) {
        case AV_PIX_FMT_NV12:
            impl_->output_format = QR_PixelFormat_NV12;
            break;
        case AV_PIX_FMT_RGBA:
            impl_->output_format = QR_PixelFormat_RGBA;
            break;
        case AV_PIX_FMT_BGRA:
            impl_->output_format = QR_PixelFormat_RGB32;
            break;
        default:
            impl_->output_format = QR_PixelFormat_NV12;
            break;
    }

    // Copy frame data to buffer
    int y_size = impl_->av_frame->width * impl_->av_frame->height;
    int uv_size = impl_->av_frame->width * impl_->av_frame->height / 2;
    int total_size = y_size + uv_size;

    if (impl_->av_frame->format == AV_PIX_FMT_RGBA ||
        impl_->av_frame->format == AV_PIX_FMT_BGRA) {
        total_size = impl_->av_frame->width * impl_->av_frame->height * 4;
    }

    impl_->frame_buffer.resize(total_size);

    if (impl_->av_frame->format == AV_PIX_FMT_NV12) {
        // Copy Y plane
        int y_stride = impl_->av_frame->linesize[0];
        uint8_t* src_y = impl_->av_frame->data[0];
        uint8_t* dst_y = impl_->frame_buffer.data();

        for (int i = 0; i < impl_->av_frame->height; i++) {
            memcpy(dst_y + i * impl_->av_frame->width,
                   src_y + i * y_stride,
                   impl_->av_frame->width);
        }

        // Copy UV plane
        int uv_stride = impl_->av_frame->linesize[1];
        uint8_t* src_uv = impl_->av_frame->data[1];
        uint8_t* dst_uv = impl_->frame_buffer.data() + y_size;

        for (int i = 0; i < impl_->av_frame->height / 2; i++) {
            memcpy(dst_uv + i * impl_->av_frame->width,
                   src_uv + i * uv_stride,
                   impl_->av_frame->width);
        }

        impl_->frame_stride = impl_->av_frame->width;
    } else if (impl_->av_frame->format == AV_PIX_FMT_RGBA ||
               impl_->av_frame->format == AV_PIX_FMT_BGRA) {
        // Copy RGBA/BGRA data
        int stride = impl_->av_frame->linesize[0];
        uint8_t* src = impl_->av_frame->data[0];
        uint8_t* dst = impl_->frame_buffer.data();

        for (int i = 0; i < impl_->av_frame->height; i++) {
            memcpy(dst + i * impl_->av_frame->width * 4,
                   src + i * stride,
                   impl_->av_frame->width * 4);
        }

        impl_->frame_stride = impl_->av_frame->width * 4;
    }

    // Create output frame structure
    static thread_local QR_Frame output_frame;
    memset(&output_frame, 0, sizeof(QR_Frame));

    output_frame.width = impl_->output_width;
    output_frame.height = impl_->output_height;
    output_frame.format = impl_->output_format;
    output_frame.timestamp_us = packet->timestamp_us;
    output_frame.data = impl_->frame_buffer.data();
    output_frame.stride = impl_->frame_stride;
    output_frame.texture = nullptr;  // Software decoder outputs to system memory
    output_frame.device = impl_->device.Get();

    *frame = &output_frame;

#else
    // Placeholder implementation for non-FFmpeg builds
    // Create a dummy frame with the configured dimensions
    impl_->output_width = impl_->config.max_width;
    impl_->output_height = impl_->config.max_height;
    impl_->output_format = QR_PixelFormat_NV12;

    int y_size = impl_->output_width * impl_->output_height;
    int uv_size = impl_->output_width * impl_->output_height / 2;
    int total_size = y_size + uv_size;

    impl_->frame_buffer.resize(total_size, 128);  // Fill with gray

    static thread_local QR_Frame output_frame;
    memset(&output_frame, 0, sizeof(QR_Frame));

    output_frame.width = impl_->output_width;
    output_frame.height = impl_->output_height;
    output_frame.format = impl_->output_format;
    output_frame.timestamp_us = packet->timestamp_us;
    output_frame.data = impl_->frame_buffer.data();
    output_frame.stride = impl_->output_width;

    *frame = &output_frame;
#endif

    // Update statistics
    auto end_time = std::chrono::high_resolution_clock::now();
    int64_t latency_us = std::chrono::duration_cast<std::chrono::microseconds>(
        end_time - start_time).count();

    impl_->frames_decoded++;
    impl_->total_latency_us += latency_us;

    // Keep track of recent latencies for FPS calculation
    impl_->recent_latencies.push_back(latency_us);
    if (impl_->recent_latencies.size() > 30) {
        impl_->recent_latencies.pop_front();
    }

    return QR_Success;
}

QR_Result SoftwareDecoder::Reset() {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }

#ifdef USE_FFMPEG
    if (impl_->codec_ctx) {
        avcodec_flush_buffers(impl_->codec_ctx);
    }
#endif

    // Reset statistics
    impl_->frames_decoded = 0;
    impl_->total_latency_us = 0;
    impl_->recent_latencies.clear();

    return QR_Success;
}

void SoftwareDecoder::GetStats(int* fps, float* latency_ms) {
    if (fps) {
        if (impl_->recent_latencies.empty()) {
            *fps = 0;
        } else {
            // Calculate FPS based on average frame time
            int64_t avg_latency_us = 0;
            for (int64_t lat : impl_->recent_latencies) {
                avg_latency_us += lat;
            }
            avg_latency_us /= static_cast<int64_t>(impl_->recent_latencies.size());

            if (avg_latency_us > 0) {
                *fps = static_cast<int>(1000000 / avg_latency_us);
            } else {
                *fps = 0;
            }
        }
    }

    if (latency_ms) {
        if (impl_->frames_decoded > 0) {
            *latency_ms = static_cast<float>(impl_->total_latency_us) /
                          static_cast<float>(impl_->frames_decoded) / 1000.0f;
        } else {
            *latency_ms = 0.0f;
        }
    }
}

void SoftwareDecoder::Shutdown() {
    impl_->Shutdown();
}

// ============================================================================
// NVDECDecoder Implementation (NVIDIA Hardware Decoder)
// ============================================================================

struct NVDECDecoder::Impl {
    QR_DecoderConfig config;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> device_context;

    std::atomic<int64_t> frames_decoded{0};
    std::atomic<int64_t> total_latency_us{0};
    std::deque<int64_t> recent_latencies;

    bool initialized = false;
    bool cuda_available = false;
};

NVDECDecoder::NVDECDecoder() : impl_(std::make_unique<Impl>()) {}
NVDECDecoder::~NVDECDecoder() = default;

QR_Result NVDECDecoder::Initialize(const QR_DecoderConfig& config, ID3D11Device* device) {
    if (!device) {
        return QR_Error_InvalidParam;  // NVDEC requires D3D11 device
    }

    impl_->config = config;
    impl_->device = device;
    device->GetImmediateContext(&impl_->device_context);

    // Check for NVIDIA GPU
    // In a real implementation, we would check for CUDA/NVDEC availability here
    impl_->cuda_available = false;  // Placeholder

    impl_->initialized = true;
    return QR_Success;
}

QR_Result NVDECDecoder::Decode(QR_Packet* packet, QR_Frame** frame) {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }

    // Placeholder implementation
    // Real implementation would use NVDEC API (cuvidDecodePicture, etc.)
    *frame = nullptr;
    return QR_Error_DecoderDecodeFailed;
}

QR_Result NVDECDecoder::Reset() {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }
    impl_->frames_decoded = 0;
    impl_->total_latency_us = 0;
    impl_->recent_latencies.clear();
    return QR_Success;
}

void NVDECDecoder::GetStats(int* fps, float* latency_ms) {
    if (fps) *fps = 0;
    if (latency_ms) *latency_ms = 0.0f;
}

void NVDECDecoder::Shutdown() {
    impl_->initialized = false;
    impl_->device.Reset();
    impl_->device_context.Reset();
}

// ============================================================================
// D3D11Decoder Implementation (D3D11 Video Processor)
// ============================================================================

struct D3D11Decoder::Impl {
    QR_DecoderConfig config;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> device_context;

    std::atomic<int64_t> frames_decoded{0};
    std::atomic<int64_t> total_latency_us{0};
    std::deque<int64_t> recent_latencies;

    bool initialized = false;
};

D3D11Decoder::D3D11Decoder() : impl_(std::make_unique<Impl>()) {}
D3D11Decoder::~D3D11Decoder() = default;

QR_Result D3D11Decoder::Initialize(const QR_DecoderConfig& config, ID3D11Device* device) {
    if (!device) {
        return QR_Error_InvalidParam;  // D3D11 decoder requires D3D11 device
    }

    impl_->config = config;
    impl_->device = device;
    device->GetImmediateContext(&impl_->device_context);

    // In a real implementation, we would create ID3D11VideoDevice and
    // ID3D11VideoContext here

    impl_->initialized = true;
    return QR_Success;
}

QR_Result D3D11Decoder::Decode(QR_Packet* packet, QR_Frame** frame) {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }

    // Placeholder implementation
    // Real implementation would use D3D11 Video Decoder API
    *frame = nullptr;
    return QR_Error_DecoderDecodeFailed;
}

QR_Result D3D11Decoder::Reset() {
    if (!impl_->initialized) {
        return QR_Error_NotInitialized;
    }
    impl_->frames_decoded = 0;
    impl_->total_latency_us = 0;
    impl_->recent_latencies.clear();
    return QR_Success;
}

void D3D11Decoder::GetStats(int* fps, float* latency_ms) {
    if (fps) *fps = 0;
    if (latency_ms) *latency_ms = 0.0f;
}

void D3D11Decoder::Shutdown() {
    impl_->initialized = false;
    impl_->device.Reset();
    impl_->device_context.Reset();
}

// ============================================================================
// DecoderFactory Implementation
// ============================================================================

QR_Result DecoderFactory::DetectBestDecoder() {
    // In a real implementation, this would check for available hardware
    // accelerators and return the best option
    return QR_Success;
}

std::unique_ptr<IDecoder> DecoderFactory::Create(bool hardware_accelerated) {
    if (hardware_accelerated) {
        // Try NVDEC first (NVIDIA)
        auto decoder = CreateNVDECDecoder();
        if (decoder) {
            return decoder;
        }

        // Try D3D11 Video Decoder (Intel/AMD)
        decoder = CreateD3D11Decoder();
        if (decoder) {
            return decoder;
        }

        // Fall back to software decoder
        return CreateSoftwareDecoder();
    }

    return CreateSoftwareDecoder();
}

std::unique_ptr<IDecoder> DecoderFactory::CreateNVDECDecoder() {
    // Check for NVIDIA GPU
    // In a real implementation, we would check for CUDA capability
    // For now, return nullptr to indicate unavailability
    return nullptr;
}

std::unique_ptr<IDecoder> DecoderFactory::CreateD3D11Decoder() {
    // D3D11 Video Decoder is available on most Windows systems
    // but requires proper setup
    return nullptr;
}

std::unique_ptr<IDecoder> DecoderFactory::CreateQSVDecoder() {
    // Intel Quick Sync Video decoder
    // Requires Intel GPU with QSV support
    return nullptr;
}

std::unique_ptr<IDecoder> DecoderFactory::CreateSoftwareDecoder() {
    return std::make_unique<SoftwareDecoder>();
}

// ============================================================================
// DecoderManager Implementation
// ============================================================================

struct DecoderManager::Impl {
    ComPtr<ID3D11Device> shared_device;
    ComPtr<ID3D11DeviceContext> shared_context;
    std::mutex mutex;

    Impl() {
        // Create shared D3D11 device if not provided
        HRESULT hr = Utils::D3DUtils::CreateD3D11Device(
            &shared_device, &shared_context, false);
        if (FAILED(hr)) {
            // Device creation failed, decoders will need external device
        }
    }
};

DecoderManager::DecoderManager() : impl_(std::make_unique<Impl>()) {}
DecoderManager::~DecoderManager() = default;

QR_Result DecoderManager::Create(const QR_DecoderConfig& config, DecoderHandle** handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    std::lock_guard<std::mutex> lock(impl_->mutex);

    auto decoder_handle = std::make_unique<DecoderHandle>();
    decoder_handle->config = config;
    decoder_handle->frames_decoded = 0;
    decoder_handle->total_latency_us = 0;
    decoder_handle->last_fps = 0;
    decoder_handle->last_latency_ms = 0.0f;

    // Determine which device to use
    ID3D11Device* device = static_cast<ID3D11Device*>(config.device);
    if (!device) {
        device = impl_->shared_device.Get();
    }

    // Create decoder
    decoder_handle->decoder = DecoderFactory::Create(config.hardware_accelerated != 0);
    if (!decoder_handle->decoder) {
        return QR_Error_DecoderCreateFailed;
    }

    // Initialize decoder
    QR_Result result = decoder_handle->decoder->Initialize(config, device);
    if (result != QR_Success) {
        return result;
    }

    *handle = decoder_handle.release();
    return QR_Success;
}

QR_Result DecoderManager::Decode(DecoderHandle* handle, QR_Packet* packet, QR_Frame** frame) {
    if (!handle || !handle->decoder || !packet || !frame) {
        return QR_Error_InvalidParam;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    QR_Result result = handle->decoder->Decode(packet, frame);

    auto end_time = std::chrono::high_resolution_clock::now();
    int64_t latency_us = std::chrono::duration_cast<std::chrono::microseconds>(
        end_time - start_time).count();

    if (result == QR_Success && *frame) {
        handle->frames_decoded++;
        handle->total_latency_us += latency_us;

        // Update cached stats
        if (handle->frames_decoded > 0) {
            handle->last_latency_ms = static_cast<float>(handle->total_latency_us) /
                                      static_cast<float>(handle->frames_decoded) / 1000.0f;

            // Simple FPS estimation (inverse of average latency)
            if (handle->last_latency_ms > 0) {
                handle->last_fps = static_cast<int>(1000.0f / handle->last_latency_ms);
            }
        }
    }

    return result;
}

QR_Result DecoderManager::Reset(DecoderHandle* handle) {
    if (!handle || !handle->decoder) {
        return QR_Error_InvalidParam;
    }

    QR_Result result = handle->decoder->Reset();
    if (result == QR_Success) {
        handle->frames_decoded = 0;
        handle->total_latency_us = 0;
        handle->last_fps = 0;
        handle->last_latency_ms = 0.0f;
    }

    return result;
}

QR_Result DecoderManager::GetStats(DecoderHandle* handle, int* fps, float* latency_ms) {
    if (!handle || !handle->decoder) {
        return QR_Error_InvalidParam;
    }

    handle->decoder->GetStats(fps, latency_ms);

    // Use cached values if decoder returns zeros
    if (fps && *fps == 0) {
        *fps = handle->last_fps;
    }
    if (latency_ms && *latency_ms == 0.0f) {
        *latency_ms = handle->last_latency_ms;
    }

    return QR_Success;
}

QR_Result DecoderManager::Destroy(DecoderHandle* handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    if (handle->decoder) {
        handle->decoder->Shutdown();
    }

    delete handle;
    return QR_Success;
}

// Global instance
DecoderManager& GetDecoderManager() {
    static DecoderManager instance;
    return instance;
}

} // namespace Decoder
} // namespace QuicRemote

// ============================================================================
// C API Implementation
// ============================================================================

extern "C" {

QR_API QR_Result QR_Decoder_Create(QR_DecoderConfig* config, QR_DecoderHandle* handle) {
    if (!config || !handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Decoder::GetDecoderManager();
    QuicRemote::Decoder::DecoderHandle* internal_handle = nullptr;

    QR_Result result = manager.Create(*config, &internal_handle);
    if (result == QR_Success) {
        *handle = reinterpret_cast<QR_DecoderHandle>(internal_handle);
    }

    return result;
}

QR_API QR_Result QR_Decoder_Decode(QR_DecoderHandle handle, QR_Packet* packet, QR_Frame** frame) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Decoder::GetDecoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle);

    return manager.Decode(internal_handle, packet, frame);
}

QR_API QR_Result QR_Decoder_Reset(QR_DecoderHandle handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Decoder::GetDecoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle);

    return manager.Reset(internal_handle);
}

QR_API QR_Result QR_Decoder_GetStats(QR_DecoderHandle handle, int* fps, float* latency_ms) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Decoder::GetDecoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle);

    return manager.GetStats(internal_handle, fps, latency_ms);
}

QR_API QR_Result QR_Decoder_Destroy(QR_DecoderHandle handle) {
    if (!handle) {
        return QR_Error_InvalidParam;
    }

    auto& manager = QuicRemote::Decoder::GetDecoderManager();
    auto* internal_handle = reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle);

    return manager.Destroy(internal_handle);
}

} // extern "C"
