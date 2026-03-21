#pragma once

#include "../include/quicremote.h"
#include <Windows.h>
#include <memory>

namespace QuicRemote {
namespace Input {

// 输入注入器
class InputInjector {
public:
    InputInjector();
    ~InputInjector();

    // 初始化和关闭
    QR_Result Initialize();
    QR_Result Shutdown();

    // 鼠标操作
    QR_Result MouseMove(int x, int y, int absolute);
    QR_Result MouseButton(QR_MouseButton button, QR_ButtonAction action);
    QR_Result MouseWheel(int delta, int is_horizontal);

    // 键盘操作
    QR_Result Key(QR_KeyCode key, QR_KeyAction action);

    // DPI 缩放
    void SetDPIScale(float scale);
    float GetDPIScale() const { return dpi_scale_; }

private:
    // 内部方法
    QR_Result SendMouseInput(DWORD flags, DWORD mouse_data);
    QR_Result SendKeyboardInput(WORD vk, DWORD flags);
    bool CheckAccess();

    // 状态
    bool initialized_;
    float dpi_scale_;
    int last_mouse_x_;
    int last_mouse_y_;

    // 虚拟桌面信息
    int virtual_desktop_left_;
    int virtual_desktop_top_;
    int virtual_desktop_width_;
    int virtual_desktop_height_;
};

// 全局实例
InputInjector& GetInputInjector();

} // namespace Input
} // namespace QuicRemote
