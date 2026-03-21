#pragma once

#include "../include/quicremote.h"
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <vector>
#include <memory>

namespace QuicRemote {
namespace Capture {

using Microsoft::WRL::ComPtr;

// 纹理池项
struct TexturePoolItem {
    ComPtr<ID3D11Texture2D> texture;
    bool in_use;
    int64_t frame_number;
};

// 光标信息
struct CursorInfo {
    int x;
    int y;
    bool visible;
    ComPtr<ID3D11Texture2D> shape_texture;
    int x_hotspot;
    int y_hotspot;
    int width;
    int height;
    std::vector<uint8_t> shape_data;
};

// 屏幕捕获管理器
class CaptureManager {
public:
    CaptureManager();
    ~CaptureManager();

    // 禁止拷贝
    CaptureManager(const CaptureManager&) = delete;
    CaptureManager& operator=(const CaptureManager&) = delete;

    // 生命周期管理
    QR_Result Initialize(int monitor_index);
    void Shutdown();

    // 帧获取
    QR_Result GetFrame(QR_Frame** frame, int timeout_ms);
    QR_Result ReleaseFrame(QR_Frame* frame);

    // 监视器枚举
    static int GetMonitorCount();
    static QR_Result GetMonitorInfo(int index, QR_MonitorInfo* info);

    // 状态
    bool IsRunning() const { return running_; }
    int GetWidth() const { return width_; }
    int GetHeight() const { return height_; }

private:
    // 内部方法
    QR_Result InitializeD3D();
    QR_Result InitializeDuplication();
    void CleanupDuplication();
    QR_Result AcquireNextFrame(int timeout_ms);
    void ProcessDirtyRects(IDXGIOutputDuplication* duplication);
    void ProcessCursorInfo();
    QR_Frame* CreateFrameFromTexture(TexturePoolItem* item);

    // D3D 设备
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGIOutputDuplication> duplication_;
    ComPtr<ID3D11Texture2D> staging_texture_;

    // 监视器信息
    int monitor_index_;
    int width_;
    int height_;
    int output_index_;

    // 纹理池
    std::vector<std::unique_ptr<TexturePoolItem>> texture_pool_;
    static constexpr int kMaxTexturePoolSize = 30;

    // 光标
    std::unique_ptr<CursorInfo> cursor_;

    // 状态
    bool running_;
    int64_t frame_counter_;

    // 脏区域
    std::vector<QR_DirtyRect> dirty_rects_;

    // 帧数据
    std::unique_ptr<QR_Frame> current_frame_;
};

// 全局实例
CaptureManager& GetCaptureManager();

} // namespace Capture
} // namespace QuicRemote
