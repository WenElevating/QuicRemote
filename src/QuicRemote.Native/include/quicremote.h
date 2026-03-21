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
    QR_Success = 0,
    QR_Error_Unknown = -1,
    QR_Error_InvalidParam = -2,
    QR_Error_OutOfMemory = -3,
    QR_Error_NotInitialized = -4,
    QR_Error_AlreadyInitialized = -5,
    QR_Error_Timeout = -6,
    QR_Error_OperationCancelled = -7,
    QR_Error_DeviceNotFound = -100,
    QR_Error_DeviceLost = -101,
    QR_Error_DeviceBusy = -102,
    QR_Error_EncoderNotSupported = -200,
    QR_Error_EncoderCreateFailed = -201,
    QR_Error_EncoderEncodeFailed = -202,
    QR_Error_EncoderReconfigureFailed = -203,
    QR_Error_DecoderNotSupported = -300,
    QR_Error_DecoderCreateFailed = -301,
    QR_Error_DecoderDecodeFailed = -302,
    QR_Error_CaptureFailed = -400,
    QR_Error_CaptureAccessDenied = -401,
    QR_Error_CaptureDesktopSwitched = -402,
    QR_Error_InputAccessDenied = -500,
    QR_Error_InputBlocked = -501,
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
