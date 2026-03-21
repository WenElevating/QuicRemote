#pragma once

#include "../include/quicremote.h"
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <string>

namespace QuicRemote {
namespace Utils {

using Microsoft::WRL::ComPtr;

// D3D11 工具函数
class D3DUtils {
public:
    // 创建 D3D11 设备
    static HRESULT CreateD3D11Device(
        ComPtr<ID3D11Device>* device,
        ComPtr<ID3D11DeviceContext>* context,
        bool debug = false
    );

    // 创建纹理
    static HRESULT CreateTexture2D(
        ID3D11Device* device,
        UINT width,
        UINT height,
        DXGI_FORMAT format,
        UINT bind_flags,
        D3D11_USAGE usage,
        ComPtr<ID3D11Texture2D>* texture
    );

    // 创建 staging 纹理 (用于 CPU 访问)
    static HRESULT CreateStagingTexture(
        ID3D11Device* device,
        UINT width,
        UINT height,
        DXGI_FORMAT format,
        ComPtr<ID3D11Texture2D>* texture
    );

    // 枚举适配器
    static UINT GetAdapterCount();
    static HRESULT GetAdapterInfo(UINT index, std::string* name, size_t* dedicated_memory);

    // 枚举输出 (监视器)
    static UINT GetOutputCount(IDXGIAdapter* adapter);
    static HRESULT GetOutputDesc(IDXGIOutput* output, DXGI_OUTPUT_DESC* desc);

    // 纹理拷贝
    static HRESULT CopyTexture(
        ID3D11DeviceContext* context,
        ID3D11Texture2D* src,
        ID3D11Texture2D* dst,
        const D3D11_BOX* box = nullptr
    );

    // 纹理映射
    static HRESULT MapTexture(
        ID3D11DeviceContext* context,
        ID3D11Texture2D* texture,
        D3D11_MAPPED_SUBRESOURCE* mapped,
        UINT subresource = 0
    );

    static void UnmapTexture(
        ID3D11DeviceContext* context,
        ID3D11Texture2D* texture,
        UINT subresource = 0
    );

    // 格式转换
    static DXGI_FORMAT PixelFormatToDXGI(QR_PixelFormat format);
    static QR_PixelFormat DXGIToPixelFormat(DXGI_FORMAT format);

    // 错误处理
    static std::string GetErrorString(HRESULT hr);
};

// 获取像素格式每像素字节数
inline UINT GetBytesPerPixel(DXGI_FORMAT format) {
    switch (format) {
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
            return 4;
        case DXGI_FORMAT_NV12:
            return 1;  // Y plane, UV plane is separate
        default:
            return 0;
    }
}

// 计算纹理行字节数
inline UINT GetRowPitch(UINT width, DXGI_FORMAT format) {
    switch (format) {
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
            return width * 4;
        case DXGI_FORMAT_NV12:
            return width;  // Y plane
        default:
            return 0;
    }
}

} // namespace Utils
} // namespace QuicRemote
