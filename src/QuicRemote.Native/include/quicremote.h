#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ============================================================================
// 版本信息
// ============================================================================
#define QR_VERSION_MAJOR 0
#define QR_VERSION_MINOR 2
#define QR_VERSION_PATCH 0

// ============================================================================
// 导出宏
// ============================================================================
#ifdef _WIN32
    #ifdef QR_EXPORTS
        #define QR_API __declspec(dllexport)
    #else
        #define QR_API __declspec(dllimport)
    #endif
#else
    #define QR_API
#endif

// ============================================================================
// 错误码定义
// ============================================================================
typedef enum QR_Result {
    QR_Success = 0,
    QR_Error_Unknown = -1,
    QR_Error_InvalidParam = -2,
    QR_Error_OutOfMemory = -3,
    QR_Error_NotInitialized = -4,
    QR_Error_AlreadyInitialized = -5,
    QR_Error_Timeout = -6,
    QR_Error_OperationCancelled = -7,
    QR_Error_BufferTooSmall = -8,
    QR_Error_NotSupported = -9,

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
    QR_Error_CaptureNoFrame = -403,

    // 输入错误 (-500 ~ -599)
    QR_Error_InputAccessDenied = -500,
    QR_Error_InputBlocked = -501,

    // 音频错误 (-600 ~ -699)
    QR_Error_AudioDeviceNotFound = -600,
    QR_Error_AudioCaptureFailed = -601,

    // 网络错误 (-700 ~ -799)
    QR_Error_ConnectionLost = -700,
    QR_Error_ConnectionRefused = -701,
    QR_Error_TimeoutConnect = -702,
    QR_Error_ProtocolError = -703,
} QR_Result;

// ============================================================================
// 枚举类型定义
// ============================================================================

// 编码器类型
typedef enum QR_EncoderType {
    QR_EncoderType_Auto = 0,        // 自动选择
    QR_EncoderType_NVENC = 1,       // NVIDIA NVENC
    QR_EncoderType_AMF = 2,         // AMD AMF
    QR_EncoderType_QSV = 3,         // Intel Quick Sync Video
    QR_EncoderType_Software = 4,    // FFmpeg 软件编码
} QR_EncoderType;

// 编解码器类型
typedef enum QR_Codec {
    QR_Codec_H264 = 0,
    QR_Codec_H265 = 1,
    QR_Codec_VP9 = 2,
} QR_Codec;

// 像素格式
typedef enum QR_PixelFormat {
    QR_PixelFormat_NV12 = 0,
    QR_PixelFormat_RGB32 = 1,
    QR_PixelFormat_RGBA = 2,
} QR_PixelFormat;

// 码率控制模式
typedef enum QR_RateControlMode {
    QR_RateControl_CBR = 0,         // 恒定码率
    QR_RateControl_VBR = 1,         // 可变码率
    QR_RateControl_CQP = 2,         // 恒定QP
} QR_RateControlMode;

// 鼠标按钮
typedef enum QR_MouseButton {
    QR_MouseButton_Left = 0,
    QR_MouseButton_Right = 1,
    QR_MouseButton_Middle = 2,
    QR_MouseButton_X1 = 3,
    QR_MouseButton_X2 = 4,
} QR_MouseButton;

// 按钮动作
typedef enum QR_ButtonAction {
    QR_ButtonAction_Press = 0,
    QR_ButtonAction_Release = 1,
} QR_ButtonAction;

// 键盘动作
typedef enum QR_KeyAction {
    QR_KeyAction_Press = 0,
    QR_KeyAction_Release = 1,
} QR_KeyAction;

// 键盘虚拟键码 (Windows VK_* 子集)
typedef enum QR_KeyCode {
    QR_Key_None = 0,

    // 功能键
    QR_Key_Back = 0x08,        // Backspace
    QR_Key_Tab = 0x09,
    QR_Key_Return = 0x0D,      // Enter
    QR_Key_Shift = 0x10,
    QR_Key_Control = 0x11,
    QR_Key_Alt = 0x12,
    QR_Key_Escape = 0x1B,
    QR_Key_Space = 0x20,
    QR_Key_Delete = 0x2E,

    // 方向键
    QR_Key_Left = 0x25,
    QR_Key_Up = 0x26,
    QR_Key_Right = 0x27,
    QR_Key_Down = 0x28,

    // 字母键 A-Z (0x41-0x5A)
    QR_Key_A = 0x41,
    QR_Key_B = 0x42,
    QR_Key_C = 0x43,
    QR_Key_D = 0x44,
    QR_Key_E = 0x45,
    QR_Key_F = 0x46,
    QR_Key_G = 0x47,
    QR_Key_H = 0x48,
    QR_Key_I = 0x49,
    QR_Key_J = 0x4A,
    QR_Key_K = 0x4B,
    QR_Key_L = 0x4C,
    QR_Key_M = 0x4D,
    QR_Key_N = 0x4E,
    QR_Key_O = 0x4F,
    QR_Key_P = 0x50,
    QR_Key_Q = 0x51,
    QR_Key_R = 0x52,
    QR_Key_S = 0x53,
    QR_Key_T = 0x54,
    QR_Key_U = 0x55,
    QR_Key_V = 0x56,
    QR_Key_W = 0x57,
    QR_Key_X = 0x58,
    QR_Key_Y = 0x59,
    QR_Key_Z = 0x5A,

    // 数字键 0-9 (0x30-0x39)
    QR_Key_0 = 0x30,
    QR_Key_1 = 0x31,
    QR_Key_2 = 0x32,
    QR_Key_3 = 0x33,
    QR_Key_4 = 0x34,
    QR_Key_5 = 0x35,
    QR_Key_6 = 0x36,
    QR_Key_7 = 0x37,
    QR_Key_8 = 0x38,
    QR_Key_9 = 0x39,

    // F1-F12
    QR_Key_F1 = 0x70,
    QR_Key_F2 = 0x71,
    QR_Key_F3 = 0x72,
    QR_Key_F4 = 0x73,
    QR_Key_F5 = 0x74,
    QR_Key_F6 = 0x75,
    QR_Key_F7 = 0x76,
    QR_Key_F8 = 0x77,
    QR_Key_F9 = 0x78,
    QR_Key_F10 = 0x79,
    QR_Key_F11 = 0x7A,
    QR_Key_F12 = 0x7B,

    // 其他常用键
    QR_Key_Insert = 0x2D,
    QR_Key_Home = 0x24,
    QR_Key_End = 0x23,
    QR_Key_PageUp = 0x21,
    QR_Key_PageDown = 0x22,
    QR_Key_CapsLock = 0x14,
    QR_Key_NumLock = 0x90,
    QR_Key_ScrollLock = 0x91,

    // 小键盘
    QR_Key_NumPad0 = 0x60,
    QR_Key_NumPad1 = 0x61,
    QR_Key_NumPad2 = 0x62,
    QR_Key_NumPad3 = 0x63,
    QR_Key_NumPad4 = 0x64,
    QR_Key_NumPad5 = 0x65,
    QR_Key_NumPad6 = 0x66,
    QR_Key_NumPad7 = 0x67,
    QR_Key_NumPad8 = 0x68,
    QR_Key_NumPad9 = 0x69,
    QR_Key_Multiply = 0x6A,    // *
    QR_Key_Add = 0x6B,         // +
    QR_Key_Subtract = 0x6D,    // -
    QR_Key_Decimal = 0x6E,     // .
    QR_Key_Divide = 0x6F,      // /
} QR_KeyCode;

// ============================================================================
// 结构体定义
// ============================================================================

// 初始化配置
typedef struct QR_Config {
    int log_level;              // 日志级别 (0=Trace, 1=Debug, 2=Info, 3=Warning, 4=Error)
    int max_frame_pool_size;    // 最大帧池大小
} QR_Config;

// 脏区域
typedef struct QR_DirtyRect {
    int left;
    int top;
    int right;
    int bottom;
} QR_DirtyRect;

// 光标形状信息
typedef struct QR_CursorShape {
    int x_hotspot;
    int y_hotspot;
    int width;
    int height;
    void* data;                 // 光标像素数据 (BGRA)
    int data_size;
} QR_CursorShape;

// 帧数据
typedef struct QR_Frame {
    // 基本信息
    int width;
    int height;
    QR_PixelFormat format;
    int64_t timestamp_us;       // 微秒时间戳

    // D3D11 纹理 (GPU)
    void* texture;              // ID3D11Texture2D*
    void* device;               // ID3D11Device* (用于共享)

    // 系统内存 (CPU)
    void* data;                 // 像素数据指针
    int stride;                 // 行字节数

    // 脏区域
    QR_DirtyRect* dirty_rects;
    int dirty_rect_count;

    // 光标信息
    int cursor_x;
    int cursor_y;
    int cursor_visible;
    QR_CursorShape* cursor_shape;  // 可为 NULL

    // 内部使用
    void* internal;
} QR_Frame;

// 编码包
typedef struct QR_Packet {
    void* data;                 // 编码数据指针
    int size;                   // 数据大小
    int64_t timestamp_us;       // 微秒时间戳
    int is_keyframe;            // 是否为关键帧
    int frame_num;              // 帧序号

    // 内部使用
    void* internal;
} QR_Packet;

// 监视器信息
typedef struct QR_MonitorInfo {
    int index;
    int width;
    int height;
    int x;
    int y;
    int is_primary;
    char name[128];
} QR_MonitorInfo;

// 编码器配置
typedef struct QR_EncoderConfig {
    QR_EncoderType encoder_type;    // 编码器类型
    QR_Codec codec;                 // 编解码器
    int width;                      // 输出宽度
    int height;                     // 输出高度
    int bitrate_kbps;               // 码率 (kbps)
    int framerate;                  // 帧率
    int gop_size;                   // GOP 大小
    QR_RateControlMode rate_control;// 码率控制模式
    int quality_preset;             // 质量预设 (0=慢速高质量, 4=快速低质量)
    int low_latency;                // 低延迟模式
    int hardware_accelerated;       // 是否硬件加速
} QR_EncoderConfig;

// 解码器配置
typedef struct QR_DecoderConfig {
    QR_Codec codec;                 // 编解码器
    int max_width;                  // 最大宽度
    int max_height;                 // 最大高度
    int hardware_accelerated;       // 是否硬件加速
    void* device;                   // ID3D11Device* (用于共享)
} QR_DecoderConfig;

// 编码器句柄
typedef struct QR_EncoderHandle_* QR_EncoderHandle;

// 解码器句柄
typedef struct QR_DecoderHandle_* QR_DecoderHandle;

// 帧回调函数类型
typedef void (*QR_FrameCallback)(QR_Frame* frame, void* context);
typedef void (*QR_PacketCallback)(QR_Packet* packet, void* context);

// ============================================================================
// 初始化/销毁 API
// ============================================================================
QR_API uint32_t QR_GetVersion(void);
QR_API const char* QR_GetVersionString(void);
QR_API const char* QR_GetErrorDescription(QR_Result result);

QR_API QR_Result QR_Init(QR_Config* config);
QR_API QR_Result QR_Shutdown(void);

// ============================================================================
// 屏幕捕获 API
// ============================================================================
QR_API int QR_Capture_GetMonitorCount(void);
QR_API QR_Result QR_Capture_GetMonitorInfo(int index, QR_MonitorInfo* info);
QR_API QR_Result QR_Capture_Start(int monitor_index);
QR_API QR_Result QR_Capture_GetFrame(QR_Frame** frame, int timeout_ms);
QR_API QR_Result QR_Capture_ReleaseFrame(QR_Frame* frame);
QR_API QR_Result QR_Capture_Stop(void);

// ============================================================================
// 编码器 API
// ============================================================================
QR_API int QR_Encoder_GetAvailableCount(QR_EncoderType type);
QR_API QR_Result QR_Encoder_Create(QR_EncoderConfig* config, QR_EncoderHandle* handle);
QR_API QR_Result QR_Encoder_Encode(QR_EncoderHandle handle, QR_Frame* frame, QR_Packet** packet);
QR_API QR_Result QR_Encoder_RequestKeyframe(QR_EncoderHandle handle);
QR_API QR_Result QR_Encoder_Reconfigure(QR_EncoderHandle handle, int bitrate_kbps);
QR_API QR_Result QR_Encoder_GetStats(QR_EncoderHandle handle, int* bitrate, int* fps, float* latency_ms);
QR_API QR_Result QR_Encoder_Destroy(QR_EncoderHandle handle);

// ============================================================================
// 解码器 API
// ============================================================================
QR_API QR_Result QR_Decoder_Create(QR_DecoderConfig* config, QR_DecoderHandle* handle);
QR_API QR_Result QR_Decoder_Decode(QR_DecoderHandle handle, QR_Packet* packet, QR_Frame** frame);
QR_API QR_Result QR_Decoder_Reset(QR_DecoderHandle handle);
QR_API QR_Result QR_Decoder_GetStats(QR_DecoderHandle handle, int* fps, float* latency_ms);
QR_API QR_Result QR_Decoder_Destroy(QR_DecoderHandle handle);

// ============================================================================
// 输入注入 API
// ============================================================================
QR_API QR_Result QR_Input_Initialize(void);
QR_API QR_Result QR_Input_MouseMove(int x, int y, int absolute);
QR_API QR_Result QR_Input_MouseButton(QR_MouseButton button, QR_ButtonAction action);
QR_API QR_Result QR_Input_MouseWheel(int delta, int is_horizontal);
QR_API QR_Result QR_Input_Key(QR_KeyCode key, QR_KeyAction action);
QR_API QR_Result QR_Input_Shutdown(void);

// ============================================================================
// 音频 API (Phase 3)
// ============================================================================
QR_API QR_Result QR_Audio_StartCapture(void);
QR_API QR_Result QR_Audio_GetData(void** data, int* size);
QR_API QR_Result QR_Audio_StopCapture(void);

// ============================================================================
// 回调注册 API
// ============================================================================
QR_API QR_Result QR_SetFrameCallback(QR_FrameCallback callback, void* context);
QR_API QR_Result QR_SetPacketCallback(QR_PacketCallback callback, void* context);

#ifdef __cplusplus
}
#endif
