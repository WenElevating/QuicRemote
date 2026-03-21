#include "../internal/input.h"
#include <Windows.h>
#include <hidusage.h>
#include <mutex>
#include <algorithm>

namespace QuicRemote {
namespace Input {

// 获取全局输入注入器实例
InputInjector& GetInputInjector()
{
    static InputInjector instance;
    return instance;
}

// 构造函数
InputInjector::InputInjector()
    : initialized_(false)
    , dpi_scale_(1.0f)
    , last_mouse_x_(0)
    , last_mouse_y_(0)
    , virtual_desktop_left_(0)
    , virtual_desktop_top_(0)
    , virtual_desktop_width_(0)
    , virtual_desktop_height_(0)
{
}

// 析构函数
InputInjector::~InputInjector()
{
    if (initialized_)
    {
        Shutdown();
    }
}

// 初始化
QR_Result InputInjector::Initialize()
{
    if (initialized_)
    {
        return QR_Error_AlreadyInitialized;
    }

    // 检查输入访问权限
    if (!CheckAccess())
    {
        return QR_Error_InputAccessDenied;
    }

    // 获取虚拟桌面信息
    virtual_desktop_left_ = GetSystemMetrics(SM_XVIRTUALSCREEN);
    virtual_desktop_top_ = GetSystemMetrics(SM_YVIRTUALSCREEN);
    virtual_desktop_width_ = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    virtual_desktop_height_ = GetSystemMetrics(SM_CYVIRTUALSCREEN);

    // 获取 DPI 缩放比例
    // 使用主显示器
    HDC hdc = GetDC(nullptr);
    if (hdc)
    {
        int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
        ReleaseDC(nullptr, hdc);
        dpi_scale_ = static_cast<float>(dpi) / 96.0f;
    }

    // 获取当前鼠标位置
    POINT pt;
    if (GetCursorPos(&pt))
    {
        last_mouse_x_ = pt.x;
        last_mouse_y_ = pt.y;
    }

    initialized_ = true;
    return QR_Success;
}

// 关闭
QR_Result InputInjector::Shutdown()
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    initialized_ = false;
    return QR_Success;
}

// 检查访问权限
bool InputInjector::CheckAccess()
{
    // 检查是否具有输入注入权限
    // UIPI (User Interface Privilege Isolation) 会阻止较低权限进程向较高权限进程发送输入
    // 我们可以通过尝试发送一个空的输入来检测是否有权限

    // 在 Windows Vista+ 上，需要确保进程有适当的权限
    // 对于服务进程，可能需要设置 UI Access
    return true;
}

// 设置 DPI 缩放
void InputInjector::SetDPIScale(float scale)
{
    if (scale > 0.0f)
    {
        dpi_scale_ = scale;
    }
}

// 发送鼠标输入
QR_Result InputInjector::SendMouseInput(DWORD flags, DWORD mouse_data)
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    INPUT input = {};
    input.type = INPUT_MOUSE;
    input.mi.dwFlags = flags;
    input.mi.mouseData = mouse_data;
    input.mi.time = 0;
    input.mi.dwExtraInfo = 0;

    UINT result = SendInput(1, &input, sizeof(INPUT));
    if (result != 1)
    {
        DWORD error = GetLastError();
        if (error == ERROR_ACCESS_DENIED)
        {
            return QR_Error_InputAccessDenied;
        }
        return QR_Error_InputBlocked;
    }

    return QR_Success;
}

// 发送键盘输入
QR_Result InputInjector::SendKeyboardInput(WORD vk, DWORD flags)
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    INPUT input = {};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = vk;
    input.ki.wScan = 0;
    input.ki.dwFlags = flags;
    input.ki.time = 0;
    input.ki.dwExtraInfo = 0;

    UINT result = SendInput(1, &input, sizeof(INPUT));
    if (result != 1)
    {
        DWORD error = GetLastError();
        if (error == ERROR_ACCESS_DENIED)
        {
            return QR_Error_InputAccessDenied;
        }
        return QR_Error_InputBlocked;
    }

    return QR_Success;
}

// 鼠标移动
QR_Result InputInjector::MouseMove(int x, int y, int absolute)
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    DWORD flags = MOUSEEVENTF_MOVE;

    // 应用 DPI 缩放（客户端坐标）
    int scaled_x = static_cast<int>(x * dpi_scale_);
    int scaled_y = static_cast<int>(y * dpi_scale_);

    if (absolute)
    {
        // 绝对坐标 - 需要转换为虚拟桌面坐标
        flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

        // 获取目标显示器或使用虚拟桌面
        int target_x = scaled_x;
        int target_y = scaled_y;

        // 计算绝对坐标 (0-65535 范围)
        // SendInput 使用虚拟桌面坐标
        int abs_x = MulDiv(target_x - virtual_desktop_left_, 65536, virtual_desktop_width_);
        int abs_y = MulDiv(target_y - virtual_desktop_top_, 65536, virtual_desktop_height_);

        // 确保在有效范围内
        abs_x = std::max(0, std::min(65535, abs_x));
        abs_y = std::max(0, std::min(65535, abs_y));

        INPUT input = {};
        input.type = INPUT_MOUSE;
        input.mi.dx = abs_x;
        input.mi.dy = abs_y;
        input.mi.dwFlags = flags;
        input.mi.time = 0;
        input.mi.dwExtraInfo = 0;

        UINT result = SendInput(1, &input, sizeof(INPUT));
        if (result != 1)
        {
            DWORD error = GetLastError();
            if (error == ERROR_ACCESS_DENIED)
            {
                return QR_Error_InputAccessDenied;
            }
            return QR_Error_InputBlocked;
        }

        last_mouse_x_ = target_x;
        last_mouse_y_ = target_y;
    }
    else
    {
        // 相对坐标移动
        INPUT input = {};
        input.type = INPUT_MOUSE;
        input.mi.dx = scaled_x;
        input.mi.dy = scaled_y;
        input.mi.dwFlags = flags;
        input.mi.time = 0;
        input.mi.dwExtraInfo = 0;

        UINT result = SendInput(1, &input, sizeof(INPUT));
        if (result != 1)
        {
            DWORD error = GetLastError();
            if (error == ERROR_ACCESS_DENIED)
            {
                return QR_Error_InputAccessDenied;
            }
            return QR_Error_InputBlocked;
        }

        // 更新最后位置（相对移动）
        last_mouse_x_ += scaled_x;
        last_mouse_y_ += scaled_y;
    }

    return QR_Success;
}

// 鼠标按钮
QR_Result InputInjector::MouseButton(QR_MouseButton button, QR_ButtonAction action)
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    DWORD flags = 0;
    DWORD mouse_data = 0;

    // 根据按钮和动作设置标志
    switch (button)
    {
    case QR_MouseButton_Left:
        flags = (action == QR_ButtonAction_Press) ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
        break;

    case QR_MouseButton_Right:
        flags = (action == QR_ButtonAction_Press) ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
        break;

    case QR_MouseButton_Middle:
        flags = (action == QR_ButtonAction_Press) ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
        break;

    case QR_MouseButton_X1:
        mouse_data = XBUTTON1;
        flags = (action == QR_ButtonAction_Press) ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
        break;

    case QR_MouseButton_X2:
        mouse_data = XBUTTON2;
        flags = (action == QR_ButtonAction_Press) ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
        break;

    default:
        return QR_Error_InvalidParam;
    }

    return SendMouseInput(flags, mouse_data);
}

// 鼠标滚轮
QR_Result InputInjector::MouseWheel(int delta, int is_horizontal)
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    DWORD flags = is_horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;
    DWORD mouse_data = static_cast<DWORD>(delta);

    return SendMouseInput(flags, mouse_data);
}

// 键盘按键
QR_Result InputInjector::Key(QR_KeyCode key, QR_KeyAction action)
{
    if (!initialized_)
    {
        return QR_Error_NotInitialized;
    }

    if (key == QR_Key_None)
    {
        return QR_Error_InvalidParam;
    }

    // QR_KeyCode 已经是 Windows VK_* 值，可以直接使用
    WORD vk = static_cast<WORD>(key);

    DWORD flags = (action == QR_KeyAction_Release) ? KEYEVENTF_KEYUP : 0;

    // 对于扩展键（如方向键、Insert、Delete 等），需要设置 KEYEVENTF_EXTENDEDKEY
    switch (key)
    {
    case QR_Key_Left:
    case QR_Key_Up:
    case QR_Key_Right:
    case QR_Key_Down:
    case QR_Key_Insert:
    case QR_Key_Delete:
    case QR_Key_Home:
    case QR_Key_End:
    case QR_Key_PageUp:
    case QR_Key_PageDown:
    case QR_Key_NumLock:
    case QR_Key_Divide:
        flags |= KEYEVENTF_EXTENDEDKEY;
        break;
    default:
        break;
    }

    return SendKeyboardInput(vk, flags);
}

} // namespace Input
} // namespace QuicRemote
