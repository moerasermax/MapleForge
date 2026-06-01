/**
 * MapleForge Windower - D3D8 視窗化 hook
 *
 * 原理：
 *  1. windower_host.exe 呼叫 InstallHook()，用 SetWindowsHookEx 注入本 DLL
 *  2. DLL 進入目標 process 後，在第一個視窗訊息時 PatchD3D8()
 *  3. 建立暫時 IDirect3D8 物件，把 vtable[15](CreateDevice) 換成我們的 hook
 *  4. 遊戲呼叫 CreateDevice → 我們把 Windowed 改成 TRUE
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include "d3d8min.h"

static void WriteLog(const char* msg)
{
    OutputDebugStringA(msg);
    FILE* f = nullptr;
    fopen_s(&f, "C:\\windower_inject.log", "a");
    if (f) { fprintf(f, "%s", msg); fclose(f); }
}

// ── 全域狀態 ─────────────────────────────────────────────────────────────────

static CreateDevice_t g_origCreateDevice = nullptr;
static HHOOK          g_hHook            = nullptr;
static HINSTANCE      g_hInst            = nullptr;
static bool           g_hooked           = false;

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
    if (pPP)
    {
        WriteLog("[Windower] CreateDevice: 強制 Windowed=TRUE!\n");
        pPP->Windowed                   = TRUE;
        pPP->FullScreen_RefreshRateInHz = 0;
        if (pPP->BackBufferWidth  == 0) pPP->BackBufferWidth  = 800;
        if (pPP->BackBufferHeight == 0) pPP->BackBufferHeight = 600;
    }
    return g_origCreateDevice(pThis, Adapter, DeviceType,
                              hFocusWindow, BehaviorFlags, pPP, ppDevice);
}

// ── 安裝 vtable patch ────────────────────────────────────────────────────────

static void PatchD3D8()
{
    if (g_hooked) return;

    WriteLog("[Windower] PatchD3D8 called\n");

    HMODULE hD3D8 = GetModuleHandleA("d3d8.dll");
    if (!hD3D8) hD3D8 = LoadLibraryA("d3d8.dll");
    if (!hD3D8)
    {
        WriteLog("[Windower] d3d8.dll 未找到\n");
        return;
    }

    auto pfnCreate = (Direct3DCreate8_t)GetProcAddress(hD3D8, "Direct3DCreate8");
    if (!pfnCreate)
    {
        OutputDebugStringA("[Windower] Direct3DCreate8 未找到\n");
        return;
    }

    IDirect3D8* pD3D = pfnCreate(D3D_SDK_VERSION);
    if (!pD3D)
    {
        OutputDebugStringA("[Windower] Direct3DCreate8 回傳 null\n");
        return;
    }

    // vtable[15] = CreateDevice
    void** vtable = (void**)pD3D->lpVtbl;
    g_origCreateDevice = (CreateDevice_t)vtable[15];

    DWORD oldProt;
    if (VirtualProtect(&vtable[15], sizeof(void*), PAGE_READWRITE, &oldProt))
    {
        vtable[15] = (void*)HookedCreateDevice;
        VirtualProtect(&vtable[15], sizeof(void*), oldProt, &oldProt);
        g_hooked = true;
        WriteLog("[Windower] D3D8 vtable hook 完成!\n");
    }
    else
    {
        WriteLog("[Windower] VirtualProtect 失敗\n");
    }

    pD3D->Release();
}

// ── SetWindowsHookEx callback ────────────────────────────────────────────────

extern "C" __declspec(dllexport)
LRESULT CALLBACK CallWndProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0 && !g_hooked)
        PatchD3D8();
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
        PatchD3D8();
    }
    return TRUE;
}
