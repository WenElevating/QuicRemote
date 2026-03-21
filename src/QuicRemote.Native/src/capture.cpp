#include "quicremote.h"
#include "../internal/capture.h"
#include "../internal/d3d_utils.h"

#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

#include <chrono>
#include <cstring>
#include <algorithm>

namespace QuicRemote {
namespace Capture {

using Microsoft::WRL::ComPtr;

// ============================================================================
// Constructor / Destructor
// ============================================================================

CaptureManager::CaptureManager()
    : monitor_index_(-1)
    , width_(0)
    , height_(0)
    , output_index_(0)
    , running_(false)
    , frame_counter_(0)
{
    cursor_ = std::make_unique<CursorInfo>();
    cursor_->visible = false;
    cursor_->x = 0;
    cursor_->y = 0;
}

CaptureManager::~CaptureManager()
{
    Shutdown();
}

// ============================================================================
// Lifecycle Management
// ============================================================================

QR_Result CaptureManager::Initialize(int monitor_index)
{
    if (running_) {
        return QR_Error_AlreadyInitialized;
    }

    if (monitor_index < 0) {
        return QR_Error_InvalidParam;
    }

    monitor_index_ = monitor_index;

    // Initialize D3D11 device
    QR_Result result = InitializeD3D();
    if (result != QR_Success) {
        Shutdown();
        return result;
    }

    // Initialize Desktop Duplication
    result = InitializeDuplication();
    if (result != QR_Success) {
        Shutdown();
        return result;
    }

    // Create staging texture for CPU access
    HRESULT hr = Utils::D3DUtils::CreateStagingTexture(
        device_.Get(),
        width_,
        height_,
        DXGI_FORMAT_B8G8R8A8_UNORM,
        &staging_texture_
    );

    if (FAILED(hr)) {
        Shutdown();
        return QR_Error_OutOfMemory;
    }

    // Initialize texture pool
    texture_pool_.reserve(kMaxTexturePoolSize);
    for (int i = 0; i < kMaxTexturePoolSize; ++i) {
        auto item = std::make_unique<TexturePoolItem>();
        item->in_use = false;
        item->frame_number = 0;

        hr = Utils::D3DUtils::CreateTexture2D(
            device_.Get(),
            width_,
            height_,
            DXGI_FORMAT_B8G8R8A8_UNORM,
            0,  // No bind flags for staging
            D3D11_USAGE_DEFAULT,
            &item->texture
        );

        if (FAILED(hr)) {
            Shutdown();
            return QR_Error_OutOfMemory;
        }

        texture_pool_.push_back(std::move(item));
    }

    running_ = true;
    frame_counter_ = 0;

    return QR_Success;
}

void CaptureManager::Shutdown()
{
    running_ = false;

    // Release all frames
    current_frame_.reset();

    // Clear texture pool
    texture_pool_.clear();

    // Release staging texture
    staging_texture_.Reset();

    // Release duplication
    CleanupDuplication();

    // Release D3D device
    context_.Reset();
    device_.Reset();

    // Clear dirty rects
    dirty_rects_.clear();

    // Reset cursor info
    if (cursor_) {
        cursor_->shape_texture.Reset();
        cursor_->shape_data.clear();
    }

    monitor_index_ = -1;
    width_ = 0;
    height_ = 0;
    frame_counter_ = 0;
}

// ============================================================================
// D3D11 Initialization
// ============================================================================

QR_Result CaptureManager::InitializeD3D()
{
    HRESULT hr = Utils::D3DUtils::CreateD3D11Device(&device_, &context_, false);
    if (FAILED(hr)) {
        return QR_Error_DeviceNotFound;
    }

    return QR_Success;
}

// ============================================================================
// Desktop Duplication Initialization
// ============================================================================

QR_Result CaptureManager::InitializeDuplication()
{
    if (!device_) {
        return QR_Error_NotInitialized;
    }

    // Get DXGI device
    ComPtr<IDXGIDevice> dxgi_device;
    HRESULT hr = device_.As(&dxgi_device);
    if (FAILED(hr)) {
        return QR_Error_DeviceNotFound;
    }

    // Get DXGI adapter
    ComPtr<IDXGIAdapter> adapter;
    hr = dxgi_device->GetAdapter(&adapter);
    if (FAILED(hr)) {
        return QR_Error_DeviceNotFound;
    }

    // Enumerate outputs to find the requested monitor
    ComPtr<IDXGIOutput> output;
    ComPtr<IDXGIOutput1> output1;
    int output_count = 0;

    for (int i = 0; ; ++i) {
        ComPtr<IDXGIOutput> current_output;
        hr = adapter->EnumOutputs(i, &current_output);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        if (FAILED(hr)) {
            continue;
        }

        if (output_count == monitor_index_) {
            output = current_output;
            break;
        }
        output_count++;
    }

    if (!output) {
        return QR_Error_DeviceNotFound;
    }

    // Get output description
    DXGI_OUTPUT_DESC output_desc;
    hr = output->GetDesc(&output_desc);
    if (FAILED(hr)) {
        return QR_Error_CaptureFailed;
    }

    width_ = output_desc.DesktopCoordinates.right - output_desc.DesktopCoordinates.left;
    height_ = output_desc.DesktopCoordinates.bottom - output_desc.DesktopCoordinates.top;

    // Get IDXGIOutput1
    hr = output.As(&output1);
    if (FAILED(hr)) {
        return QR_Error_CaptureFailed;
    }

    // Duplicate the desktop output
    hr = output1->DuplicateOutput(device_.Get(), &duplication_);
    if (hr == E_ACCESSDENIED) {
        return QR_Error_CaptureAccessDenied;
    }
    if (FAILED(hr)) {
        return QR_Error_CaptureFailed;
    }

    return QR_Success;
}

void CaptureManager::CleanupDuplication()
{
    if (duplication_) {
        duplication_->ReleaseFrame();
        duplication_.Reset();
    }
}

// ============================================================================
// Frame Acquisition
// ============================================================================

QR_Result CaptureManager::AcquireNextFrame(int timeout_ms)
{
    if (!duplication_) {
        return QR_Error_NotInitialized;
    }

    DXGI_OUTDUPL_FRAME_INFO frame_info;
    ComPtr<IDXGIResource> resource;

    // Release previous frame first
    duplication_->ReleaseFrame();

    // Acquire next frame
    HRESULT hr = duplication_->AcquireNextFrame(timeout_ms, &frame_info, &resource);

    if (hr == DXGI_ERROR_WAIT_TIMEOUT) {
        return QR_Error_Timeout;
    }

    if (hr == DXGI_ERROR_ACCESS_LOST) {
        // Desktop switch or mode change
        return QR_Error_CaptureDesktopSwitched;
    }

    if (FAILED(hr)) {
        return QR_Error_CaptureFailed;
    }

    // Check if we have new frame data
    if (frame_info.LastPresentTime.QuadPart == 0) {
        // No new frame, but might have cursor update
        if (cursor_ && frame_info.LastMouseUpdateTime.QuadPart != 0) {
            cursor_->x = frame_info.PointerPosition.Position.x;
            cursor_->y = frame_info.PointerPosition.Position.y;
            cursor_->visible = frame_info.PointerPosition.Visible != FALSE;
        }
        ProcessCursorInfo();
        duplication_->ReleaseFrame();
        return QR_Error_CaptureNoFrame;
    }

    // Get the texture from the resource
    ComPtr<ID3D11Texture2D> desktop_texture;
    hr = resource.As(&desktop_texture);
    if (FAILED(hr)) {
        duplication_->ReleaseFrame();
        return QR_Error_CaptureFailed;
    }

    // Get texture description
    D3D11_TEXTURE2D_DESC desc;
    desktop_texture->GetDesc(&desc);

    // Update cursor position from frame info
    if (cursor_ && frame_info.LastMouseUpdateTime.QuadPart != 0) {
        cursor_->x = frame_info.PointerPosition.Position.x;
        cursor_->y = frame_info.PointerPosition.Position.y;
        cursor_->visible = frame_info.PointerPosition.Visible != FALSE;
    }

    // Process dirty rects
    ProcessDirtyRects(duplication_.Get());

    // Process cursor shape info
    ProcessCursorInfo();

    // Find an available texture in the pool
    TexturePoolItem* pool_item = nullptr;
    for (auto& item : texture_pool_) {
        if (!item->in_use) {
            pool_item = item.get();
            break;
        }
    }

    if (!pool_item) {
        // All textures in use, wait for one to be released
        duplication_->ReleaseFrame();
        return QR_Error_DeviceBusy;
    }

    // Copy desktop texture to pool texture
    if (desc.Width != static_cast<UINT>(width_) || desc.Height != static_cast<UINT>(height_)) {
        // Resolution changed
        duplication_->ReleaseFrame();
        return QR_Error_CaptureDesktopSwitched;
    }

    // Copy the entire texture
    context_->CopyResource(pool_item->texture.Get(), desktop_texture.Get());

    // Also copy to staging texture for CPU access
    context_->CopyResource(staging_texture_.Get(), desktop_texture.Get());

    pool_item->in_use = true;
    pool_item->frame_number = ++frame_counter_;

    duplication_->ReleaseFrame();

    return QR_Success;
}

void CaptureManager::ProcessDirtyRects(IDXGIOutputDuplication* duplication)
{
    if (!duplication) {
        return;
    }

    dirty_rects_.clear();

    // Get dirty rectangles count
    UINT dirty_rects_buffer_size = 0;
    UINT dirty_rects_count = 0;

    HRESULT hr = duplication->GetFrameDirtyRects(
        0, nullptr, &dirty_rects_buffer_size, &dirty_rects_count
    );

    if (hr != DXGI_ERROR_MORE_DATA && FAILED(hr)) {
        return;
    }

    if (dirty_rects_buffer_size == 0) {
        return;
    }

    // Allocate buffer and get dirty rects
    std::vector<RECT> rects(dirty_rects_count);
    hr = duplication->GetFrameDirtyRects(
        dirty_rects_buffer_size, rects.data(), &dirty_rects_buffer_size, &dirty_rects_count
    );

    if (FAILED(hr)) {
        return;
    }

    // Convert to QR_DirtyRect
    dirty_rects_.reserve(dirty_rects_count);
    for (UINT i = 0; i < dirty_rects_count; ++i) {
        QR_DirtyRect dirty;
        dirty.left = rects[i].left;
        dirty.top = rects[i].top;
        dirty.right = rects[i].right;
        dirty.bottom = rects[i].bottom;
        dirty_rects_.push_back(dirty);
    }
}

void CaptureManager::ProcessCursorInfo()
{
    if (!duplication_ || !cursor_) {
        return;
    }

    // Get cursor shape info
    DXGI_OUTDUPL_POINTER_SHAPE_INFO shape_info;
    UINT shape_size = 0;
    HRESULT hr = duplication_->GetFramePointerShape(0, nullptr, &shape_size, &shape_info);

    std::vector<uint8_t> shape_buffer;
    if (hr == DXGI_ERROR_MORE_DATA && shape_size > 0) {
        shape_buffer.resize(shape_size);
        hr = duplication_->GetFramePointerShape(
            shape_size, shape_buffer.data(), &shape_size, &shape_info
        );

        if (SUCCEEDED(hr)) {
            // Process cursor shape based on type
            int width = shape_info.Width;
            int height = shape_info.Height;
            int x_hotspot = shape_info.HotSpot.x;
            int y_hotspot = shape_info.HotSpot.y;

            cursor_->width = width;
            cursor_->height = height;
            cursor_->x_hotspot = x_hotspot;
            cursor_->y_hotspot = y_hotspot;

            // Convert shape data based on type
            if (shape_info.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME) {
                // Monochrome cursor: AND mask followed by XOR mask
                int and_mask_size = ((width + 15) / 16) * 2 * height;
                int total_size = and_mask_size * 2;

                if (shape_buffer.size() >= static_cast<size_t>(total_size)) {
                    cursor_->shape_data.resize(width * height * 4);

                    const uint8_t* and_mask = shape_buffer.data();
                    const uint8_t* xor_mask = shape_buffer.data() + and_mask_size;

                    for (int y = 0; y < height; ++y) {
                        for (int x = 0; x < width; ++x) {
                            int byte_idx = ((width + 15) / 16) * 2 * y + (x / 8);
                            int bit_idx = 7 - (x % 8);

                            int and_val = (and_mask[byte_idx] >> bit_idx) & 1;
                            int xor_val = (xor_mask[byte_idx] >> bit_idx) & 1;

                            uint8_t* pixel = &cursor_->shape_data[(y * width + x) * 4];

                            if (and_val) {
                                if (xor_val) {
                                    // Invert screen (use white for visibility)
                                    pixel[0] = 255; pixel[1] = 255; pixel[2] = 255; pixel[3] = 255;
                                } else {
                                    // Transparent
                                    pixel[0] = 0; pixel[1] = 0; pixel[2] = 0; pixel[3] = 0;
                                }
                            } else {
                                if (xor_val) {
                                    // White
                                    pixel[0] = 255; pixel[1] = 255; pixel[2] = 255; pixel[3] = 255;
                                } else {
                                    // Black
                                    pixel[0] = 0; pixel[1] = 0; pixel[2] = 0; pixel[3] = 255;
                                }
                            }
                        }
                    }
                }
            } else if (shape_info.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_COLOR) {
                // 32-bit BGRA cursor
                cursor_->shape_data = std::move(shape_buffer);
            } else if (shape_info.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR) {
                // 32-bit BGRA cursor with mask
                cursor_->shape_data = std::move(shape_buffer);
                // Apply mask - set alpha to 0 for masked pixels
                for (size_t i = 0; i + 3 < cursor_->shape_data.size(); i += 4) {
                    if (cursor_->shape_data[i + 3] == 0) {
                        // Use the high bit of the reserved byte as a mask indicator
                        // For masked color, transparent pixels have A=0 and non-zero reserved
                    }
                }
            }
        }
    }
}

// ============================================================================
// Frame Management
// ============================================================================

QR_Result CaptureManager::GetFrame(QR_Frame** frame, int timeout_ms)
{
    if (!running_) {
        return QR_Error_NotInitialized;
    }

    if (!frame) {
        return QR_Error_InvalidParam;
    }

    QR_Result result = AcquireNextFrame(timeout_ms);

    if (result == QR_Error_Timeout || result == QR_Error_CaptureNoFrame) {
        return result;
    }

    if (result == QR_Error_CaptureDesktopSwitched) {
        // Try to reinitialize
        CleanupDuplication();
        result = InitializeDuplication();
        if (result != QR_Success) {
            return result;
        }
        // Try again
        result = AcquireNextFrame(timeout_ms);
        if (result != QR_Success) {
            return result;
        }
    }

    if (result != QR_Success) {
        return result;
    }

    // Find the most recent texture that's in use
    TexturePoolItem* best_item = nullptr;
    int64_t best_frame = 0;

    for (auto& item : texture_pool_) {
        if (item->in_use && item->frame_number > best_frame) {
            best_item = item.get();
            best_frame = item->frame_number;
        }
    }

    if (!best_item) {
        return QR_Error_CaptureNoFrame;
    }

    // Create frame structure
    QR_Frame* new_frame = CreateFrameFromTexture(best_item);
    if (!new_frame) {
        best_item->in_use = false;
        return QR_Error_OutOfMemory;
    }

    *frame = new_frame;
    return QR_Success;
}

QR_Result CaptureManager::ReleaseFrame(QR_Frame* frame)
{
    if (!frame) {
        return QR_Error_InvalidParam;
    }

    // Find the texture pool item and mark it as available
    if (frame->internal) {
        TexturePoolItem* item = static_cast<TexturePoolItem*>(frame->internal);
        item->in_use = false;
    }

    // Free dirty rects if allocated
    if (frame->dirty_rects) {
        delete[] frame->dirty_rects;
        frame->dirty_rects = nullptr;
    }

    // Free cursor shape if allocated
    if (frame->cursor_shape) {
        if (frame->cursor_shape->data) {
            delete[] static_cast<uint8_t*>(frame->cursor_shape->data);
        }
        delete frame->cursor_shape;
        frame->cursor_shape = nullptr;
    }

    // Free CPU data if allocated
    if (frame->data) {
        delete[] static_cast<uint8_t*>(frame->data);
        frame->data = nullptr;
    }

    delete frame;

    return QR_Success;
}

QR_Frame* CaptureManager::CreateFrameFromTexture(TexturePoolItem* item)
{
    if (!item || !item->texture) {
        return nullptr;
    }

    QR_Frame* frame = new (std::nothrow) QR_Frame();
    if (!frame) {
        return nullptr;
    }

    memset(frame, 0, sizeof(QR_Frame));

    // Basic info
    frame->width = width_;
    frame->height = height_;
    frame->format = QR_PixelFormat_RGBA;  // Desktop Duplication uses BGRA (stored as RGBA)
    frame->timestamp_us = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()
    ).count();

    // D3D11 texture (GPU)
    frame->texture = item->texture.Get();
    frame->device = device_.Get();
    frame->internal = item;

    // Copy dirty rects
    if (!dirty_rects_.empty()) {
        frame->dirty_rects = new (std::nothrow) QR_DirtyRect[dirty_rects_.size()];
        if (frame->dirty_rects) {
            frame->dirty_rect_count = static_cast<int>(dirty_rects_.size());
            for (size_t i = 0; i < dirty_rects_.size(); ++i) {
                frame->dirty_rects[i] = dirty_rects_[i];
            }
        }
    }

    // Cursor info
    if (cursor_) {
        frame->cursor_x = cursor_->x;
        frame->cursor_y = cursor_->y;
        frame->cursor_visible = cursor_->visible ? 1 : 0;

        // Copy cursor shape if available
        if (!cursor_->shape_data.empty()) {
            frame->cursor_shape = new (std::nothrow) QR_CursorShape();
            if (frame->cursor_shape) {
                frame->cursor_shape->x_hotspot = cursor_->x_hotspot;
                frame->cursor_shape->y_hotspot = cursor_->y_hotspot;
                frame->cursor_shape->width = cursor_->width;
                frame->cursor_shape->height = cursor_->height;
                frame->cursor_shape->data_size = static_cast<int>(cursor_->shape_data.size());

                frame->cursor_shape->data = new (std::nothrow) uint8_t[cursor_->shape_data.size()];
                if (frame->cursor_shape->data) {
                    memcpy(frame->cursor_shape->data, cursor_->shape_data.data(),
                           cursor_->shape_data.size());
                }
            }
        }
    }

    // CPU data - map staging texture
    if (staging_texture_) {
        D3D11_MAPPED_SUBRESOURCE mapped;
        HRESULT hr = context_->Map(staging_texture_.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (SUCCEEDED(hr)) {
            int data_size = mapped.RowPitch * height_;
            frame->data = new (std::nothrow) uint8_t[data_size];
            frame->stride = mapped.RowPitch;

            if (frame->data) {
                // Copy row by row to handle potential stride differences
                uint8_t* src = static_cast<uint8_t*>(mapped.pData);
                uint8_t* dst = static_cast<uint8_t*>(frame->data);
                for (int y = 0; y < height_; ++y) {
                    memcpy(dst + y * mapped.RowPitch, src + y * mapped.RowPitch, width_ * 4);
                }
            }

            context_->Unmap(staging_texture_.Get(), 0);
        }
    }

    return frame;
}

// ============================================================================
// Monitor Enumeration
// ============================================================================

int CaptureManager::GetMonitorCount()
{
    // Create a temporary device to enumerate outputs
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    HRESULT hr = Utils::D3DUtils::CreateD3D11Device(&device, &context, false);
    if (FAILED(hr)) {
        return 0;
    }

    ComPtr<IDXGIDevice> dxgi_device;
    hr = device.As(&dxgi_device);
    if (FAILED(hr)) {
        return 0;
    }

    ComPtr<IDXGIAdapter> adapter;
    hr = dxgi_device->GetAdapter(&adapter);
    if (FAILED(hr)) {
        return 0;
    }

    int count = 0;
    for (int i = 0; ; ++i) {
        ComPtr<IDXGIOutput> output;
        hr = adapter->EnumOutputs(i, &output);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        if (SUCCEEDED(hr)) {
            count++;
        }
    }

    return count;
}

QR_Result CaptureManager::GetMonitorInfo(int index, QR_MonitorInfo* info)
{
    if (!info) {
        return QR_Error_InvalidParam;
    }

    memset(info, 0, sizeof(QR_MonitorInfo));

    // Create a temporary device to enumerate outputs
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    HRESULT hr = Utils::D3DUtils::CreateD3D11Device(&device, &context, false);
    if (FAILED(hr)) {
        return QR_Error_DeviceNotFound;
    }

    ComPtr<IDXGIDevice> dxgi_device;
    hr = device.As(&dxgi_device);
    if (FAILED(hr)) {
        return QR_Error_DeviceNotFound;
    }

    ComPtr<IDXGIAdapter> adapter;
    hr = dxgi_device->GetAdapter(&adapter);
    if (FAILED(hr)) {
        return QR_Error_DeviceNotFound;
    }

    // Enumerate outputs to find the requested one
    ComPtr<IDXGIOutput> output;
    int current_idx = 0;

    for (int i = 0; ; ++i) {
        ComPtr<IDXGIOutput> current_output;
        hr = adapter->EnumOutputs(i, &current_output);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        if (FAILED(hr)) {
            continue;
        }

        if (current_idx == index) {
            output = current_output;
            break;
        }
        current_idx++;
    }

    if (!output) {
        return QR_Error_DeviceNotFound;
    }

    DXGI_OUTPUT_DESC desc;
    hr = output->GetDesc(&desc);
    if (FAILED(hr)) {
        return QR_Error_CaptureFailed;
    }

    info->index = index;
    info->width = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
    info->height = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;
    info->x = desc.DesktopCoordinates.left;
    info->y = desc.DesktopCoordinates.top;
    info->is_primary = (desc.AttachedToDesktop != FALSE);

    // Convert wide string device name to narrow string
    if (desc.DeviceName[0] != L'\0') {
        WideCharToMultiByte(CP_UTF8, 0, desc.DeviceName, -1,
                           info->name, sizeof(info->name) - 1, nullptr, nullptr);
    }

    return QR_Success;
}

// ============================================================================
// Global Instance
// ============================================================================

CaptureManager& GetCaptureManager()
{
    static CaptureManager instance;
    return instance;
}

} // namespace Capture
} // namespace QuicRemote

// ============================================================================
// C API Implementation
// ============================================================================

extern "C" {

QR_API int QR_Capture_GetMonitorCount(void)
{
    return QuicRemote::Capture::CaptureManager::GetMonitorCount();
}

QR_API QR_Result QR_Capture_GetMonitorInfo(int index, QR_MonitorInfo* info)
{
    return QuicRemote::Capture::CaptureManager::GetMonitorInfo(index, info);
}

QR_API QR_Result QR_Capture_Start(int monitor_index)
{
    return QuicRemote::Capture::GetCaptureManager().Initialize(monitor_index);
}

QR_API QR_Result QR_Capture_GetFrame(QR_Frame** frame, int timeout_ms)
{
    return QuicRemote::Capture::GetCaptureManager().GetFrame(frame, timeout_ms);
}

QR_API QR_Result QR_Capture_ReleaseFrame(QR_Frame* frame)
{
    return QuicRemote::Capture::GetCaptureManager().ReleaseFrame(frame);
}

QR_API QR_Result QR_Capture_Stop(void)
{
    QuicRemote::Capture::GetCaptureManager().Shutdown();
    return QR_Success;
}

} // extern "C"
