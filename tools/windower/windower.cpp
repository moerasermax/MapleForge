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

// ── Hook Device methods ──────────────────────────────────────────────────────

HRESULT WINAPI HookedReset(IDirect3DDevice8* pThis, D3DPRESENT_PARAMETERS* pPP)
{
    if (InterlockedCompareExchange(&g_resetLoggedOnce, 1, 0) == 0)
        WriteLog("[Windower] IDirect3DDevice8::Reset first call");
    return g_origReset ? g_origReset(pThis, pPP) : E_FAIL;
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

    if (pPP)
    {
        WriteLog("[Windower] CreateDevice: force Windowed=TRUE");
        pPP->Windowed                   = TRUE;
        pPP->FullScreen_RefreshRateInHz = 0;
        if (pPP->BackBufferWidth  == 0) pPP->BackBufferWidth  = 800;
        if (pPP->BackBufferHeight == 0) pPP->BackBufferHeight = 600;
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
        HookDeviceVtable(*ppDevice);

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
