/**
 * MapleForge Windower - D3D8 視窗化 hook
 *
 * 原理：
 *  1. windower_host.exe 呼叫 InstallHook()，用 SetWindowsHookEx 注入本 DLL
 *  2. DLL 先 detour d3d8!Direct3DCreate8，提早攔截 D3D 初始化入口
 *  3. Direct3DCreate8 回傳 IDirect3D8 後，再 patch vtable[15](CreateDevice)
 *  4. CreateDevice 成功回傳 IDirect3DDevice8 後，再 patch Reset/Present
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <stdint.h>
#include "d3d8min.h"

static void WriteLog(const char* fmt, ...)
{
    char msg[1024] = {};
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(msg, _countof(msg), _TRUNCATE, fmt, ap);
    va_end(ap);

    char modulePath[MAX_PATH] = {};
    GetModuleFileNameA(nullptr, modulePath, MAX_PATH);
    const char* name = strrchr(modulePath, '\\');
    name = name ? (name + 1) : modulePath;

    SYSTEMTIME st = {};
    GetLocalTime(&st);

    char line[1400] = {};
    _snprintf_s(
        line, _countof(line), _TRUNCATE,
        "[%04u-%02u-%02u %02u:%02u:%02u.%03u] [%s:%lu] %s\n",
        (unsigned)st.wYear, (unsigned)st.wMonth, (unsigned)st.wDay,
        (unsigned)st.wHour, (unsigned)st.wMinute, (unsigned)st.wSecond, (unsigned)st.wMilliseconds,
        name, (unsigned long)GetCurrentProcessId(), msg);

    OutputDebugStringA(line);
    FILE* f = nullptr;
    fopen_s(&f, "C:\\windower_inject.log", "a");
    if (f) { fputs(line, f); fclose(f); }
}

// ── 全域狀態 ─────────────────────────────────────────────────────────────────

static CreateDevice_t g_origCreateDevice = nullptr;
static DeviceReset_t  g_origReset        = nullptr;
static DevicePresent_t g_origPresent     = nullptr;
static Direct3DCreate8_t g_origDirect3DCreate8 = nullptr;
static void*          g_direct3DCreate8Addr = nullptr;
static BYTE*          g_direct3DCreate8Trampoline = nullptr;
static BYTE           g_direct3DCreate8Saved[5] = {};

static HHOOK          g_hHook            = nullptr;
static HINSTANCE      g_hInst            = nullptr;
static bool           g_d3d8EntryHooked  = false;
static bool           g_createDeviceHooked = false;
static bool           g_deviceVtableHooked = false;
static volatile LONG  g_presentLoggedOnce = 0;
static volatile LONG  g_resetLoggedOnce   = 0;
static HWND           g_gameWindow         = nullptr;
static UINT           g_backBufferWidth    = 800;
static UINT           g_backBufferHeight   = 600;
static D3DFORMAT      g_cachedDesktopFormat = D3DFMT_UNKNOWN;
static bool           g_hasCachedDesktopFormat = false;

enum { D3DADAPTER_DEFAULT_LOCAL = 0 };
enum { D3DFMT_X8R8G8B8_LOCAL = 22 };

typedef struct D3DDISPLAYMODE_LOCAL {
    UINT Width;
    UINT Height;
    UINT RefreshRate;
    D3DFORMAT Format;
} D3DDISPLAYMODE_LOCAL;

// ── 視窗化接管工具 ─────────────────────────────────────────────────────────────

static HWND ResolveGameWindow(HWND ppWindow, HWND fallbackWindow, const char* tag)
{
    HWND hwnd = nullptr;
    const char* source = "none";

    if (ppWindow && IsWindow(ppWindow))
    {
        hwnd = ppWindow;
        source = "pPP->hDeviceWindow";
    }
    else if (fallbackWindow && IsWindow(fallbackWindow))
    {
        hwnd = fallbackWindow;
        source = "fallbackWindow";
    }
    else
    {
        HWND active = GetActiveWindow();
        if (active && IsWindow(active))
        {
            hwnd = active;
            source = "GetActiveWindow";
        }
    }

    if (!hwnd)
    {
        WriteLog("[Windower] %s resolve HWND failed (no valid window)", tag);
        return nullptr;
    }

    HWND root = GetAncestor(hwnd, GA_ROOT);
    if (root && IsWindow(root))
        hwnd = root;

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != GetCurrentProcessId())
    {
        WriteLog("[Windower] %s resolve HWND rejected (source=%s hwnd=%p pid=%lu current=%lu)",
            tag, source, (void*)hwnd, (unsigned long)pid, (unsigned long)GetCurrentProcessId());
        return nullptr;
    }

    WriteLog("[Windower] %s resolve HWND ok (source=%s hwnd=%p)", tag, source, (void*)hwnd);
    return hwnd;
}

static void ForceWindowedPresentParams(D3DPRESENT_PARAMETERS* pPP, const char* tag)
{
    if (!pPP)
    {
        WriteLog("[Windower] %s no present params", tag);
        return;
    }

    const BOOL oldWindowed = pPP->Windowed;
    const UINT oldRefreshRate = pPP->FullScreen_RefreshRateInHz;
    const UINT oldPresentInterval = pPP->FullScreen_PresentationInterval;
    const UINT oldWidth = pPP->BackBufferWidth;
    const UINT oldHeight = pPP->BackBufferHeight;

    pPP->Windowed = TRUE;
    pPP->FullScreen_RefreshRateInHz = 0;
    pPP->FullScreen_PresentationInterval = 0; // D3DPRESENT_INTERVAL_DEFAULT

    if (pPP->BackBufferWidth == 0) pPP->BackBufferWidth = g_backBufferWidth ? g_backBufferWidth : 800;
    if (pPP->BackBufferHeight == 0) pPP->BackBufferHeight = g_backBufferHeight ? g_backBufferHeight : 600;

    if (pPP->BackBufferWidth) g_backBufferWidth = pPP->BackBufferWidth;
    if (pPP->BackBufferHeight) g_backBufferHeight = pPP->BackBufferHeight;

    WriteLog(
        "[Windower] %s force windowed: Windowed %d->%d, Refresh %u->%u, Interval %u->%u, BackBuffer %ux%u->%ux%u",
        tag,
        (int)oldWindowed, (int)pPP->Windowed,
        (unsigned)oldRefreshRate, (unsigned)pPP->FullScreen_RefreshRateInHz,
        (unsigned)oldPresentInterval, (unsigned)pPP->FullScreen_PresentationInterval,
        (unsigned)oldWidth, (unsigned)oldHeight,
        (unsigned)pPP->BackBufferWidth, (unsigned)pPP->BackBufferHeight);
}

static D3DFORMAT ResolveDesktopBackBufferFormat(IDirect3D8* pD3D, const char* tag)
{
    if (!pD3D || !pD3D->lpVtbl)
    {
        WriteLog("[Windower] %s GetAdapterDisplayMode skipped (invalid IDirect3D8), fallback to 0x%X",
            tag, (unsigned)D3DFMT_X8R8G8B8_LOCAL);
        return (D3DFORMAT)D3DFMT_X8R8G8B8_LOCAL;
    }

    typedef HRESULT(WINAPI* GetAdapterDisplayMode_t)(
        IDirect3D8*,
        UINT,
        D3DDISPLAYMODE_LOCAL*);

    GetAdapterDisplayMode_t fnGetAdapterDisplayMode =
        (GetAdapterDisplayMode_t)pD3D->lpVtbl->GetAdapterDisplayMode;
    if (!fnGetAdapterDisplayMode)
    {
        WriteLog("[Windower] %s GetAdapterDisplayMode pointer null, fallback to 0x%X",
            tag, (unsigned)D3DFMT_X8R8G8B8_LOCAL);
        return (D3DFORMAT)D3DFMT_X8R8G8B8_LOCAL;
    }

    D3DDISPLAYMODE_LOCAL dm = {};
    HRESULT hr = fnGetAdapterDisplayMode(pD3D, D3DADAPTER_DEFAULT_LOCAL, &dm);
    if (SUCCEEDED(hr))
    {
        WriteLog("[Windower] %s GetAdapterDisplayMode ok: %ux%u @%uHz format=0x%X",
            tag, (unsigned)dm.Width, (unsigned)dm.Height, (unsigned)dm.RefreshRate, (unsigned)dm.Format);
        return dm.Format;
    }

    WriteLog("[Windower] %s GetAdapterDisplayMode failed hr=0x%08lX, fallback to 0x%X",
        tag, (unsigned long)hr, (unsigned)D3DFMT_X8R8G8B8_LOCAL);
    return (D3DFORMAT)D3DFMT_X8R8G8B8_LOCAL;
}

static void ForceWindowedBackBufferFormat(D3DPRESENT_PARAMETERS* pPP, D3DFORMAT fmt, const char* tag)
{
    if (!pPP)
    {
        WriteLog("[Windower] %s skip BackBufferFormat override (no present params)", tag);
        return;
    }

    const D3DFORMAT oldFmt = pPP->BackBufferFormat;
    pPP->BackBufferFormat = fmt;
    WriteLog("[Windower] %s BackBufferFormat 0x%X -> 0x%X",
        tag, (unsigned)oldFmt, (unsigned)pPP->BackBufferFormat);
}

static void ApplyManagedWindowFrame(HWND hwnd, UINT backBufferWidth, UINT backBufferHeight, const char* tag)
{
    if (!hwnd || !IsWindow(hwnd))
    {
        WriteLog("[Windower] %s apply frame skipped (invalid hwnd=%p)", tag, (void*)hwnd);
        return;
    }

    SetLastError(0);
    LONG_PTR oldStyle = GetWindowLongPtr(hwnd, GWL_STYLE);
    const DWORD styleErr = GetLastError();
    if (oldStyle == 0 && styleErr != 0)
    {
        WriteLog("[Windower] %s GetWindowLongPtr failed hwnd=%p err=%lu", tag, (void*)hwnd, (unsigned long)styleErr);
        return;
    }

    LONG_PTR newStyle = (oldStyle & ~((LONG_PTR)WS_POPUP)) | WS_OVERLAPPEDWINDOW | WS_VISIBLE;
    SetLastError(0);
    if (!SetWindowLongPtr(hwnd, GWL_STYLE, newStyle))
    {
        const DWORD err = GetLastError();
        if (err != 0)
            WriteLog("[Windower] %s SetWindowLongPtr style failed hwnd=%p err=%lu", tag, (void*)hwnd, (unsigned long)err);
    }

    SetWindowTextA(hwnd, "MapleForge");

    RECT frameRect = { 0, 0, (LONG)backBufferWidth, (LONG)backBufferHeight };
    BOOL hasMenu = (GetMenu(hwnd) != nullptr);
    if (!AdjustWindowRect(&frameRect, (DWORD)newStyle, hasMenu))
    {
        WriteLog("[Windower] %s AdjustWindowRect failed hwnd=%p err=%lu", tag, (void*)hwnd, (unsigned long)GetLastError());
        return;
    }

    RECT oldRect = {};
    if (!GetWindowRect(hwnd, &oldRect))
    {
        WriteLog("[Windower] %s GetWindowRect failed hwnd=%p err=%lu", tag, (void*)hwnd, (unsigned long)GetLastError());
        return;
    }

    const int frameW = frameRect.right - frameRect.left;
    const int frameH = frameRect.bottom - frameRect.top;

    if (!SetWindowPos(
            hwnd, nullptr, oldRect.left, oldRect.top, frameW, frameH,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED))
    {
        WriteLog("[Windower] %s SetWindowPos failed hwnd=%p err=%lu", tag, (void*)hwnd, (unsigned long)GetLastError());
        return;
    }

    WriteLog("[Windower] %s apply frame ok hwnd=%p style 0x%08lX -> 0x%08lX size=%dx%d",
        tag, (void*)hwnd, (unsigned long)oldStyle, (unsigned long)newStyle, frameW, frameH);
}

// ── Hook Device methods ──────────────────────────────────────────────────────

HRESULT WINAPI HookedReset(IDirect3DDevice8* pThis, D3DPRESENT_PARAMETERS* pPP)
{
    if (InterlockedCompareExchange(&g_resetLoggedOnce, 1, 0) == 0)
        WriteLog("[Windower] IDirect3DDevice8::Reset first call");

    ForceWindowedPresentParams(pPP, "Reset(pre)");
    if (g_hasCachedDesktopFormat)
    {
        ForceWindowedBackBufferFormat(pPP, g_cachedDesktopFormat, "Reset(pre)");
    }
    else
    {
        g_cachedDesktopFormat = (D3DFORMAT)D3DFMT_X8R8G8B8_LOCAL;
        g_hasCachedDesktopFormat = true;
        WriteLog("[Windower] Reset(pre) desktop format cache missing, fallback cache=0x%X",
            (unsigned)g_cachedDesktopFormat);
        ForceWindowedBackBufferFormat(pPP, g_cachedDesktopFormat, "Reset(pre)");
    }

    HWND hwnd = ResolveGameWindow(pPP ? pPP->hDeviceWindow : nullptr, g_gameWindow, "Reset(pre)");
    if (hwnd)
    {
        g_gameWindow = hwnd;
        if (pPP && !pPP->hDeviceWindow) pPP->hDeviceWindow = hwnd;
        ApplyManagedWindowFrame(hwnd, g_backBufferWidth, g_backBufferHeight, "Reset(pre)");
    }

    if (!g_origReset)
        return E_FAIL;

    HRESULT hr = g_origReset(pThis, pPP);
    WriteLog("[Windower] Reset returned hr=0x%08lX", (unsigned long)hr);

    if (SUCCEEDED(hr) && g_gameWindow)
        ApplyManagedWindowFrame(g_gameWindow, g_backBufferWidth, g_backBufferHeight, "Reset(post)");

    return hr;
}

HRESULT WINAPI HookedPresent(
    IDirect3DDevice8* pThis,
    const RECT* pSourceRect,
    const RECT* pDestRect,
    HWND hDestWindowOverride,
    const RGNDATA* pDirtyRegion)
{
    if (InterlockedCompareExchange(&g_presentLoggedOnce, 1, 0) == 0)
        WriteLog("[Windower] IDirect3DDevice8::Present first call");
    return g_origPresent ? g_origPresent(pThis, pSourceRect, pDestRect, hDestWindowOverride, pDirtyRegion) : E_FAIL;
}

static void HookDeviceVtable(IDirect3DDevice8* pDevice)
{
    if (!pDevice || !pDevice->lpVtbl) return;

    void** vtable = (void**)pDevice->lpVtbl;

    DWORD oldProt = 0;
    if (!g_origReset)   g_origReset = (DeviceReset_t)vtable[14];
    if (!g_origPresent) g_origPresent = (DevicePresent_t)vtable[15];

    if (VirtualProtect(&vtable[14], sizeof(void*) * 2, PAGE_READWRITE, &oldProt))
    {
        vtable[14] = (void*)HookedReset;
        vtable[15] = (void*)HookedPresent;
        VirtualProtect(&vtable[14], sizeof(void*) * 2, oldProt, &oldProt);
        g_deviceVtableHooked = true;
        WriteLog("[Windower] Device vtable hook done (Reset/Present)");
    }
    else
    {
        WriteLog("[Windower] Device vtable hook failed: VirtualProtect error=%lu", (unsigned long)GetLastError());
    }
}

// ── Hook CreateDevice ────────────────────────────────────────────────────────

HRESULT WINAPI HookedCreateDevice(
    IDirect3D8*            pThis,
    UINT                   Adapter,
    D3DDEVTYPE             DeviceType,
    HWND                   hFocusWindow,
    DWORD                  BehaviorFlags,
    D3DPRESENT_PARAMETERS* pPP,
    IDirect3DDevice8**     ppDevice)
{
    WriteLog("[Windower] IDirect3D8::CreateDevice called");

    ForceWindowedPresentParams(pPP, "CreateDevice(pre)");
    g_cachedDesktopFormat = ResolveDesktopBackBufferFormat(pThis, "CreateDevice(pre)");
    g_hasCachedDesktopFormat = true;
    ForceWindowedBackBufferFormat(pPP, g_cachedDesktopFormat, "CreateDevice(pre)");

    HWND candidateHwnd = ResolveGameWindow(pPP ? pPP->hDeviceWindow : nullptr, hFocusWindow, "CreateDevice(pre)");
    if (candidateHwnd && pPP && !pPP->hDeviceWindow)
    {
        pPP->hDeviceWindow = candidateHwnd;
        WriteLog("[Windower] CreateDevice(pre) filled pPP->hDeviceWindow with %p", (void*)candidateHwnd);
    }

    if (!g_origCreateDevice)
    {
        WriteLog("[Windower] CreateDevice original pointer is null");
        return E_FAIL;
    }

    HRESULT hr = g_origCreateDevice(
        pThis, Adapter, DeviceType, hFocusWindow, BehaviorFlags, pPP, ppDevice);

    WriteLog("[Windower] CreateDevice returned hr=0x%08lX device=%p",
             (unsigned long)hr, (ppDevice ? (void*)(*ppDevice) : nullptr));

    if (SUCCEEDED(hr) && ppDevice && *ppDevice)
    {
        HWND hwnd = ResolveGameWindow(pPP ? pPP->hDeviceWindow : nullptr, hFocusWindow, "CreateDevice(post)");
        if (hwnd)
        {
            g_gameWindow = hwnd;
            ApplyManagedWindowFrame(hwnd, g_backBufferWidth, g_backBufferHeight, "CreateDevice(post)");
        }
        HookDeviceVtable(*ppDevice);
    }

    return hr;
}

static void HookCreateDeviceVtable(IDirect3D8* pD3D)
{
    if (!pD3D || !pD3D->lpVtbl) return;

    void** vtable = (void**)pD3D->lpVtbl;
    if (!g_origCreateDevice)
        g_origCreateDevice = (CreateDevice_t)vtable[15];

    DWORD oldProt = 0;
    if (VirtualProtect(&vtable[15], sizeof(void*), PAGE_READWRITE, &oldProt))
    {
        vtable[15] = (void*)HookedCreateDevice;
        VirtualProtect(&vtable[15], sizeof(void*), oldProt, &oldProt);
        g_createDeviceHooked = true;
        WriteLog("[Windower] IDirect3D8 vtable hook done (CreateDevice)");
    }
    else
    {
        WriteLog("[Windower] IDirect3D8 vtable hook failed: VirtualProtect error=%lu", (unsigned long)GetLastError());
    }
}

IDirect3D8* WINAPI HookedDirect3DCreate8(UINT SDKVersion)
{
    WriteLog("[Windower] Direct3DCreate8 called (SDKVersion=%u)", (unsigned)SDKVersion);

    if (!g_origDirect3DCreate8)
    {
        WriteLog("[Windower] Direct3DCreate8 original pointer is null");
        return nullptr;
    }

    IDirect3D8* pD3D = g_origDirect3DCreate8(SDKVersion);
    WriteLog("[Windower] Direct3DCreate8 returned %p", (void*)pD3D);

    if (pD3D)
        HookCreateDeviceVtable(pD3D);

    return pD3D;
}

static bool InstallDirect3DCreate8Hook()
{
    if (g_d3d8EntryHooked)
        return true;

    HMODULE hD3D8 = GetModuleHandleA("d3d8.dll");
    if (!hD3D8) hD3D8 = LoadLibraryA("d3d8.dll");
    if (!hD3D8)
    {
        WriteLog("[Windower] d3d8.dll not found");
        return false;
    }

    g_direct3DCreate8Addr = (void*)GetProcAddress(hD3D8, "Direct3DCreate8");
    WriteLog("[Windower] GetProcAddress(Direct3DCreate8) = %p", g_direct3DCreate8Addr);

    if (!g_direct3DCreate8Addr)
        return false;

    BYTE* target = (BYTE*)g_direct3DCreate8Addr;
    memcpy(g_direct3DCreate8Saved, target, sizeof(g_direct3DCreate8Saved));

    g_direct3DCreate8Trampoline = (BYTE*)VirtualAlloc(
        nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!g_direct3DCreate8Trampoline)
    {
        WriteLog("[Windower] trampoline alloc failed, error=%lu", (unsigned long)GetLastError());
        return false;
    }

    memcpy(g_direct3DCreate8Trampoline, target, 5);
    g_direct3DCreate8Trampoline[5] = 0xE9;
    *(int32_t*)(g_direct3DCreate8Trampoline + 6) =
        (int32_t)((target + 5) - (g_direct3DCreate8Trampoline + 10));
    g_origDirect3DCreate8 = (Direct3DCreate8_t)g_direct3DCreate8Trampoline;

    DWORD oldProt = 0;
    if (!VirtualProtect(target, 5, PAGE_EXECUTE_READWRITE, &oldProt))
    {
        WriteLog("[Windower] VirtualProtect Direct3DCreate8 failed, error=%lu", (unsigned long)GetLastError());
        return false;
    }

    target[0] = 0xE9;
    *(int32_t*)(target + 1) = (int32_t)((BYTE*)HookedDirect3DCreate8 - (target + 5));
    VirtualProtect(target, 5, oldProt, &oldProt);
    FlushInstructionCache(GetCurrentProcess(), target, 5);

    g_d3d8EntryHooked = true;
    WriteLog("[Windower] Direct3DCreate8 detour installed");
    return true;
}

static void EnsureHooksInstalled()
{
    InstallDirect3DCreate8Hook();
}

// ── SetWindowsHookEx callback ────────────────────────────────────────────────

extern "C" __declspec(dllexport)
LRESULT CALLBACK CallWndProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0 && !g_d3d8EntryHooked)
        EnsureHooksInstalled();
    return CallNextHookEx(g_hHook, nCode, wParam, lParam);
}

extern "C" __declspec(dllexport)
HHOOK InstallHook()
{
    g_hHook = SetWindowsHookExA(WH_CALLWNDPROC, CallWndProc, g_hInst, 0);
    return g_hHook;
}

extern "C" __declspec(dllexport)
void RemoveHook()
{
    if (g_hHook) { UnhookWindowsHookEx(g_hHook); g_hHook = nullptr; }
}

// ── DllMain ──────────────────────────────────────────────────────────────────

BOOL WINAPI DllMain(HINSTANCE hInst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hInst = hInst;
        DisableThreadLibraryCalls(hInst);
        WriteLog("[Windower] DllMain DLL_PROCESS_ATTACH");
        EnsureHooksInstalled();
    }
    return TRUE;
}
