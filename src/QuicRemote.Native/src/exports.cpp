#include "quicremote.h"
#include <atomic>
#include <string>
#include <mutex>

namespace {
    std::atomic<bool> g_initialized{false};
    std::string g_versionString;
    std::once_flag g_versionOnce;
}

extern "C" {

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
    return QR_Success;
}

} // extern "C"
