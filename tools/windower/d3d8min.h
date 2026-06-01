/**
 * d3d8min.h - 最小型 D3D8 定義（只含 windower 需要的部分）
 * 不依賴 DirectX SDK，自行定義足夠的型別。
 */
#pragma once
#include <windows.h>

#define D3D_SDK_VERSION 120

typedef enum D3DDEVTYPE  { D3DDEVTYPE_HAL = 1 } D3DDEVTYPE;
typedef enum D3DFORMAT   { D3DFMT_UNKNOWN = 0 } D3DFORMAT;
typedef enum D3DSWAPEFFECT { D3DSWAPEFFECT_DISCARD = 1 } D3DSWAPEFFECT;
typedef enum D3DMULTISAMPLE_TYPE { D3DMULTISAMPLE_NONE = 0 } D3DMULTISAMPLE_TYPE;

typedef struct IDirect3DDevice8 IDirect3DDevice8;
typedef struct IDirect3D8       IDirect3D8;

typedef struct D3DPRESENT_PARAMETERS {
    UINT                BackBufferWidth;
    UINT                BackBufferHeight;
    D3DFORMAT           BackBufferFormat;
    UINT                BackBufferCount;
    D3DMULTISAMPLE_TYPE MultiSampleType;
    D3DSWAPEFFECT       SwapEffect;
    HWND                hDeviceWindow;
    BOOL                Windowed;
    BOOL                EnableAutoDepthStencil;
    D3DFORMAT           AutoDepthStencilFormat;
    DWORD               Flags;
    UINT                FullScreen_RefreshRateInHz;
    UINT                FullScreen_PresentationInterval;
} D3DPRESENT_PARAMETERS;

// IDirect3D8 vtable（我們只需要 Release=2, CreateDevice=15）
typedef struct IDirect3D8Vtbl {
    // IUnknown
    void* QueryInterface;   // 0
    void* AddRef;           // 1
    void* Release;          // 2
    // IDirect3D8
    void* RegisterSoftwareDevice; // 3
    void* GetAdapterCount;         // 4
    void* GetAdapterIdentifier;    // 5
    void* GetAdapterModeCount;     // 6
    void* EnumAdapterModes;        // 7
    void* GetAdapterDisplayMode;   // 8
    void* CheckDeviceType;         // 9
    void* CheckDeviceFormat;       // 10
    void* CheckDeviceMultiSampleType; // 11
    void* CheckDepthStencilMatch;  // 12
    void* GetDeviceCaps;           // 13
    void* GetAdapterMonitor;       // 14
    void* CreateDevice;            // 15  ← 我們要 hook 的
} IDirect3D8Vtbl;

struct IDirect3D8 {
    IDirect3D8Vtbl* lpVtbl;
    ULONG WINAPI AddRef()  { return ((ULONG(WINAPI*)(IDirect3D8*))lpVtbl->AddRef)(this); }
    ULONG WINAPI Release() { return ((ULONG(WINAPI*)(IDirect3D8*))lpVtbl->Release)(this); }
};

typedef IDirect3D8* (WINAPI* Direct3DCreate8_t)(UINT SDKVersion);

typedef HRESULT (WINAPI* CreateDevice_t)(
    IDirect3D8*,
    UINT, D3DDEVTYPE, HWND, DWORD,
    D3DPRESENT_PARAMETERS*,
    IDirect3DDevice8**);
