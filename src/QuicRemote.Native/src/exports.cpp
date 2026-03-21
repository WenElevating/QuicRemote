#include "quicremote.h"
#include "../internal/capture.h"
#include "../internal/encoder.h"
#include "../internal/decoder.h"
#include "../internal/input.h"
#include <atomic>
#include <string>
#include <mutex>

namespace {
    std::atomic<bool> g_initialized{false};
    std::string g_versionString;
    std::once_flag g_versionOnce;
}

extern "C" {

// ============================================================================
// 版本 API
// ============================================================================

QR_API uint32_t QR_GetVersion(void)
{
    return (QR_VERSION_MAJOR << 16) | (QR_VERSION_MINOR << 8) | QR_VERSION_PATCH;
}

QR_API const char* QR_GetVersionString(void)
{
    std::call_once(g_versionOnce, []() {
        g_versionString = std::to_string(QR_VERSION_MAJOR) + "." +
                          std::to_string(QR_VERSION_MINOR) + "." +
                          std::to_string(QR_VERSION_PATCH);
    });
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
    case QR_Error_BufferTooSmall: return "Buffer too small";
    case QR_Error_NotSupported: return "Operation not supported";
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
    case QR_Error_CaptureNoFrame: return "No frame available";
    case QR_Error_InputAccessDenied: return "Input access denied";
    case QR_Error_InputBlocked: return "Input blocked";
    case QR_Error_AudioDeviceNotFound: return "Audio device not found";
    case QR_Error_AudioCaptureFailed: return "Audio capture failed";
    case QR_Error_ConnectionLost: return "Connection lost";
    case QR_Error_ConnectionRefused: return "Connection refused";
    case QR_Error_TimeoutConnect: return "Connection timeout";
    case QR_Error_ProtocolError: return "Protocol error";
    default: return "Unknown error code";
    }
}

// ============================================================================
// 初始化/销毁 API
// ============================================================================

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
    return QR_Success;
}

QR_API QR_Result QR_Shutdown(void)
{
    if (!g_initialized.exchange(false))
    {
        return QR_Error_NotInitialized;
    }

    // 停止所有模块
    QuicRemote::Capture::GetCaptureManager().Shutdown();
    QuicRemote::Input::GetInputInjector().Shutdown();

    return QR_Success;
}

// ============================================================================
// 屏幕捕获 API
// ============================================================================

QR_API int QR_Capture_GetMonitorCount(void)
{
    return QuicRemote::Capture::CaptureManager::GetMonitorCount();
}

QR_API QR_Result QR_Capture_GetMonitorInfo(int index, QR_MonitorInfo* info)
{
    if (!info)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Capture::CaptureManager::GetMonitorInfo(index, info);
}

QR_API QR_Result QR_Capture_Start(int monitor_index)
{
    if (!g_initialized)
    {
        return QR_Error_NotInitialized;
    }
    return QuicRemote::Capture::GetCaptureManager().Initialize(monitor_index);
}

QR_API QR_Result QR_Capture_GetFrame(QR_Frame** frame, int timeout_ms)
{
    if (!frame)
    {
        return QR_Error_InvalidParam;
    }
    if (!g_initialized)
    {
        return QR_Error_NotInitialized;
    }
    return QuicRemote::Capture::GetCaptureManager().GetFrame(frame, timeout_ms);
}

QR_API QR_Result QR_Capture_ReleaseFrame(QR_Frame* frame)
{
    if (!frame)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Capture::GetCaptureManager().ReleaseFrame(frame);
}

QR_API QR_Result QR_Capture_Stop(void)
{
    QuicRemote::Capture::GetCaptureManager().Shutdown();
    return QR_Success;
}

// ============================================================================
// 编码器 API
// ============================================================================

QR_API int QR_Encoder_GetAvailableCount(QR_EncoderType type)
{
    return QuicRemote::Encoder::EncoderFactory::GetAvailableCount(type);
}

QR_API QR_Result QR_Encoder_Create(QR_EncoderConfig* config, QR_EncoderHandle* handle)
{
    if (!config || !handle)
    {
        return QR_Error_InvalidParam;
    }
    if (!g_initialized)
    {
        return QR_Error_NotInitialized;
    }
    return QuicRemote::Encoder::GetEncoderManager().Create(config,
        reinterpret_cast<QuicRemote::Encoder::EncoderHandle**>(handle));
}

QR_API QR_Result QR_Encoder_Encode(QR_EncoderHandle handle, QR_Frame* frame, QR_Packet** packet)
{
    if (!handle || !frame || !packet)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Encoder::GetEncoderManager().Encode(
        reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle), frame, packet);
}

QR_API QR_Result QR_Encoder_RequestKeyframe(QR_EncoderHandle handle)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Encoder::GetEncoderManager().RequestKeyframe(
        reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle));
}

QR_API QR_Result QR_Encoder_Reconfigure(QR_EncoderHandle handle, int bitrate_kbps)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Encoder::GetEncoderManager().Reconfigure(
        reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle), bitrate_kbps);
}

QR_API QR_Result QR_Encoder_GetStats(QR_EncoderHandle handle, int* bitrate, int* fps, float* latency_ms)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Encoder::GetEncoderManager().GetStats(
        reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle), bitrate, fps, latency_ms);
}

QR_API QR_Result QR_Encoder_Destroy(QR_EncoderHandle handle)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Encoder::GetEncoderManager().Destroy(
        reinterpret_cast<QuicRemote::Encoder::EncoderHandle*>(handle));
}

// ============================================================================
// 解码器 API
// ============================================================================

QR_API QR_Result QR_Decoder_Create(QR_DecoderConfig* config, QR_DecoderHandle* handle)
{
    if (!config || !handle)
    {
        return QR_Error_InvalidParam;
    }
    if (!g_initialized)
    {
        return QR_Error_NotInitialized;
    }
    return QuicRemote::Decoder::GetDecoderManager().Create(config,
        reinterpret_cast<QuicRemote::Decoder::DecoderHandle**>(handle));
}

QR_API QR_Result QR_Decoder_Decode(QR_DecoderHandle handle, QR_Packet* packet, QR_Frame** frame)
{
    if (!handle || !packet || !frame)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Decoder::GetDecoderManager().Decode(
        reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle), packet, frame);
}

QR_API QR_Result QR_Decoder_Reset(QR_DecoderHandle handle)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Decoder::GetDecoderManager().Reset(
        reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle));
}

QR_API QR_Result QR_Decoder_GetStats(QR_DecoderHandle handle, int* fps, float* latency_ms)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Decoder::GetDecoderManager().GetStats(
        reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle), fps, latency_ms);
}

QR_API QR_Result QR_Decoder_Destroy(QR_DecoderHandle handle)
{
    if (!handle)
    {
        return QR_Error_InvalidParam;
    }
    return QuicRemote::Decoder::GetDecoderManager().Destroy(
        reinterpret_cast<QuicRemote::Decoder::DecoderHandle*>(handle));
}

// ============================================================================
// 输入注入 API
// ============================================================================

QR_API QR_Result QR_Input_Initialize(void)
{
    if (!g_initialized)
    {
        return QR_Error_NotInitialized;
    }
    return QuicRemote::Input::GetInputInjector().Initialize();
}

QR_API QR_Result QR_Input_MouseMove(int x, int y, int absolute)
{
    return QuicRemote::Input::GetInputInjector().MouseMove(x, y, absolute);
}

QR_API QR_Result QR_Input_MouseButton(QR_MouseButton button, QR_ButtonAction action)
{
    return QuicRemote::Input::GetInputInjector().MouseButton(button, action);
}

QR_API QR_Result QR_Input_MouseWheel(int delta, int is_horizontal)
{
    return QuicRemote::Input::GetInputInjector().MouseWheel(delta, is_horizontal);
}

QR_API QR_Result QR_Input_Key(QR_KeyCode key, QR_KeyAction action)
{
    return QuicRemote::Input::GetInputInjector().Key(key, action);
}

QR_API QR_Result QR_Input_Shutdown(void)
{
    QuicRemote::Input::GetInputInjector().Shutdown();
    return QR_Success;
}

// ============================================================================
// 音频 API (Phase 3 - 占位实现)
// ============================================================================

QR_API QR_Result QR_Audio_StartCapture(void)
{
    return QR_Error_NotSupported;
}

QR_API QR_Result QR_Audio_GetData(void** data, int* size)
{
    (void)data;
    (void)size;
    return QR_Error_NotSupported;
}

QR_API QR_Result QR_Audio_StopCapture(void)
{
    return QR_Error_NotSupported;
}

// ============================================================================
// 回调注册 API (占位实现)
// ============================================================================

QR_API QR_Result QR_SetFrameCallback(QR_FrameCallback callback, void* context)
{
    (void)callback;
    (void)context;
    return QR_Error_NotSupported;
}

QR_API QR_Result QR_SetPacketCallback(QR_PacketCallback callback, void* context)
{
    (void)callback;
    (void)context;
    return QR_Error_NotSupported;
}

} // extern "C"
