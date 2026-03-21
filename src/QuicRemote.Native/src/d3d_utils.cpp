#include "../internal/d3d_utils.h"
#include "../include/quicremote.h"

#include <Windows.h>
#include <dxgi1_6.h>
#include <string>
#include <sstream>

namespace QuicRemote {
namespace Utils {

// ============================================================================
// D3D11 Device Creation
// ============================================================================

HRESULT D3DUtils::CreateD3D11Device(
    ComPtr<ID3D11Device>* device,
    ComPtr<ID3D11DeviceContext>* context,
    bool debug
)
{
    UINT create_flags = 0;

    if (debug) {
        create_flags |= D3D11_CREATE_DEVICE_DEBUG;
    }

    // Feature levels to try
    D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1,
        D3D_FEATURE_LEVEL_10_0,
    };

    D3D_FEATURE_LEVEL selected_feature_level;
    HRESULT hr = D3D11CreateDevice(
        nullptr,  // Default adapter
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,  // No software rasterizer
        create_flags,
        feature_levels,
        _countof(feature_levels),
        D3D11_SDK_VERSION,
        &(*device),
        &selected_feature_level,
        &(*context)
    );

    if (FAILED(hr)) {
        // Try WARP (software) driver if hardware fails
        hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_WARP,
            nullptr,
            create_flags,
            feature_levels,
            _countof(feature_levels),
            D3D11_SDK_VERSION,
            &(*device),
            &selected_feature_level,
            &(*context)
        );
    }

    return hr;
}

// ============================================================================
// Texture Creation
// ============================================================================

HRESULT D3DUtils::CreateTexture2D(
    ID3D11Device* device,
    UINT width,
    UINT height,
    DXGI_FORMAT format,
    UINT bind_flags,
    D3D11_USAGE usage,
    ComPtr<ID3D11Texture2D>* texture
)
{
    if (!device || !texture) {
        return E_INVALIDARG;
    }

    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.SampleDesc.Quality = 0;
    desc.Usage = usage;
    desc.BindFlags = bind_flags;
    desc.CPUAccessFlags = 0;
    desc.MiscFlags = 0;

    return device->CreateTexture2D(&desc, nullptr, &(*texture));
}

HRESULT D3DUtils::CreateStagingTexture(
    ID3D11Device* device,
    UINT width,
    UINT height,
    DXGI_FORMAT format,
    ComPtr<ID3D11Texture2D>* texture
)
{
    if (!device || !texture) {
        return E_INVALIDARG;
    }

    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.SampleDesc.Quality = 0;
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.MiscFlags = 0;

    return device->CreateTexture2D(&desc, nullptr, &(*texture));
}

// ============================================================================
// Adapter Enumeration
// ============================================================================

UINT D3DUtils::GetAdapterCount()
{
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) {
        return 0;
    }

    UINT count = 0;
    for (UINT i = 0; ; ++i) {
        ComPtr<IDXGIAdapter1> adapter;
        hr = factory->EnumAdapters1(i, &adapter);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        if (SUCCEEDED(hr)) {
            count++;
        }
    }

    return count;
}

HRESULT D3DUtils::GetAdapterInfo(UINT index, std::string* name, size_t* dedicated_memory)
{
    if (!name || !dedicated_memory) {
        return E_INVALIDARG;
    }

    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) {
        return hr;
    }

    ComPtr<IDXGIAdapter1> adapter;
    hr = factory->EnumAdapters1(index, &adapter);
    if (FAILED(hr)) {
        return hr;
    }

    DXGI_ADAPTER_DESC1 desc;
    hr = adapter->GetDesc1(&desc);
    if (FAILED(hr)) {
        return hr;
    }

    // Convert wide string to narrow string
    char narrow_name[128] = {};
    WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, narrow_name, sizeof(narrow_name) - 1, nullptr, nullptr);
    *name = narrow_name;

    *dedicated_memory = desc.DedicatedVideoMemory;

    return S_OK;
}

// ============================================================================
// Output Enumeration
// ============================================================================

UINT D3DUtils::GetOutputCount(IDXGIAdapter* adapter)
{
    if (!adapter) {
        return 0;
    }

    UINT count = 0;
    for (UINT i = 0; ; ++i) {
        ComPtr<IDXGIOutput> output;
        HRESULT hr = adapter->EnumOutputs(i, &output);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        if (SUCCEEDED(hr)) {
            count++;
        }
    }

    return count;
}

HRESULT D3DUtils::GetOutputDesc(IDXGIOutput* output, DXGI_OUTPUT_DESC* desc)
{
    if (!output || !desc) {
        return E_INVALIDARG;
    }

    return output->GetDesc(desc);
}

// ============================================================================
// Texture Operations
// ============================================================================

HRESULT D3DUtils::CopyTexture(
    ID3D11DeviceContext* context,
    ID3D11Texture2D* src,
    ID3D11Texture2D* dst,
    const D3D11_BOX* box
)
{
    if (!context || !src || !dst) {
        return E_INVALIDARG;
    }

    if (box) {
        context->CopySubresourceRegion(dst, 0, 0, 0, 0, src, 0, box);
    } else {
        context->CopyResource(dst, src);
    }

    return S_OK;
}

HRESULT D3DUtils::MapTexture(
    ID3D11DeviceContext* context,
    ID3D11Texture2D* texture,
    D3D11_MAPPED_SUBRESOURCE* mapped,
    UINT subresource
)
{
    if (!context || !texture || !mapped) {
        return E_INVALIDARG;
    }

    return context->Map(texture, subresource, D3D11_MAP_READ, 0, mapped);
}

void D3DUtils::UnmapTexture(
    ID3D11DeviceContext* context,
    ID3D11Texture2D* texture,
    UINT subresource
)
{
    if (context && texture) {
        context->Unmap(texture, subresource);
    }
}

// ============================================================================
// Format Conversion
// ============================================================================

DXGI_FORMAT D3DUtils::PixelFormatToDXGI(QR_PixelFormat format)
{
    switch (format) {
        case QR_PixelFormat_NV12:
            return DXGI_FORMAT_NV12;
        case QR_PixelFormat_RGB32:
            return DXGI_FORMAT_R8G8B8A8_UNORM;
        case QR_PixelFormat_RGBA:
            return DXGI_FORMAT_R8G8B8A8_UNORM;
        default:
            return DXGI_FORMAT_UNKNOWN;
    }
}

QR_PixelFormat D3DUtils::DXGIToPixelFormat(DXGI_FORMAT format)
{
    switch (format) {
        case DXGI_FORMAT_NV12:
            return QR_PixelFormat_NV12;
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
            return QR_PixelFormat_RGBA;
        default:
            return QR_PixelFormat_NV12;
    }
}

// ============================================================================
// Error Handling
// ============================================================================

std::string D3DUtils::GetErrorString(HRESULT hr)
{
    if (hr == S_OK) {
        return "Success";
    }

    // Common DXGI/D3D errors
    switch (hr) {
        case DXGI_ERROR_DEVICE_REMOVED:
            return "DXGI_ERROR_DEVICE_REMOVED";
        case DXGI_ERROR_DEVICE_HUNG:
            return "DXGI_ERROR_DEVICE_HUNG";
        case DXGI_ERROR_DEVICE_RESET:
            return "DXGI_ERROR_DEVICE_RESET";
        case DXGI_ERROR_DRIVER_INTERNAL_ERROR:
            return "DXGI_ERROR_DRIVER_INTERNAL_ERROR";
        case DXGI_ERROR_INVALID_CALL:
            return "DXGI_ERROR_INVALID_CALL";
        case DXGI_ERROR_WAS_STILL_DRAWING:
            return "DXGI_ERROR_WAS_STILL_DRAWING";
        case DXGI_ERROR_NOT_FOUND:
            return "DXGI_ERROR_NOT_FOUND";
        case DXGI_ERROR_MORE_DATA:
            return "DXGI_ERROR_MORE_DATA";
        case DXGI_ERROR_UNSUPPORTED:
            return "DXGI_ERROR_UNSUPPORTED";
        case DXGI_ERROR_ACCESS_LOST:
            return "DXGI_ERROR_ACCESS_LOST";
        case DXGI_ERROR_WAIT_TIMEOUT:
            return "DXGI_ERROR_WAIT_TIMEOUT";
        case DXGI_ERROR_SESSION_DISCONNECTED:
            return "DXGI_ERROR_SESSION_DISCONNECTED";
        case DXGI_ERROR_ACCESS_DENIED:
            return "DXGI_ERROR_ACCESS_DENIED";
        case E_INVALIDARG:
            return "E_INVALIDARG";
        case E_OUTOFMEMORY:
            return "E_OUTOFMEMORY";
        case E_FAIL:
            return "E_FAIL";
        case E_ACCESSDENIED:
            return "E_ACCESSDENIED";
        case E_NOINTERFACE:
            return "E_NOINTERFACE";
        default:
            break;
    }

    // Format as hex string
    std::ostringstream oss;
    oss << "HRESULT 0x" << std::hex << hr;
    return oss.str();
}

} // namespace Utils
} // namespace QuicRemote
