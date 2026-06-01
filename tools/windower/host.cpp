/**
 * windower_host.exe - 安裝全域 hook，讓 windower.dll 注入進 MapleStory
 *
 * 用法：windower_host.exe
 *   → 安裝 hook，然後啟動 MapleStory.exe
 *   → 遊戲結束後 Ctrl+C 或直接關閉本視窗
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

typedef HHOOK (*InstallHook_t)();
typedef void  (*RemoveHook_t)();

int main()
{
    wchar_t dllPath[MAX_PATH];
    GetModuleFileNameW(nullptr, dllPath, MAX_PATH);

    // 取得 windower.dll 的路徑（與 host.exe 同目錄）
    wchar_t* last = wcsrchr(dllPath, L'\\');
    if (last) *(last + 1) = L'\0';
    wcscat_s(dllPath, L"windower.dll");

    printf("[Windower Host] 載入 DLL: ");
    wprintf(dllPath);
    printf("\n");

    HMODULE hDll = LoadLibraryW(dllPath);
    if (!hDll)
    {
        printf("[Windower Host] 無法載入 DLL，錯誤碼=%lu\n", GetLastError());
        return 1;
    }

    auto fnInstall = (InstallHook_t)GetProcAddress(hDll, "InstallHook");
    auto fnRemove  = (RemoveHook_t) GetProcAddress(hDll, "RemoveHook");

    if (!fnInstall)
    {
        printf("[Windower Host] 找不到 InstallHook 函式\n");
        FreeLibrary(hDll);
        return 1;
    }

    HHOOK hHook = fnInstall();
    if (!hHook)
    {
        printf("[Windower Host] SetWindowsHookEx 失敗，錯誤碼=%lu\n", GetLastError());
        FreeLibrary(hDll);
        return 1;
    }

    printf("[Windower Host] Hook 安裝成功！可以啟動 MapleStory.exe 了\n");
    printf("[Windower Host] 按 Enter 解除 Hook 並退出...\n");
    getchar();

    if (fnRemove) fnRemove();
    FreeLibrary(hDll);
    printf("[Windower Host] Hook 已移除\n");
    return 0;
}
