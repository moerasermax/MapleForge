/**
 * windower_host.exe - 安裝全域 hook，保持存活直到被殺
 * 不使用 getchar()，改用 WaitForSingleObject 讓進程持續存在。
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

typedef HHOOK (*InstallHook_t)();
typedef void  (*RemoveHook_t)();

int WINAPI WinMain(HINSTANCE, HINSTANCE, LPSTR, int)
{
    wchar_t dllPath[MAX_PATH];
    GetModuleFileNameW(nullptr, dllPath, MAX_PATH);

    wchar_t* last = wcsrchr(dllPath, L'\\');
    if (last) *(last + 1) = L'\0';
    wcscat_s(dllPath, L"windower.dll");

    HMODULE hDll = LoadLibraryW(dllPath);
    if (!hDll) return 1;

    auto fnInstall = (InstallHook_t)GetProcAddress(hDll, "InstallHook");
    auto fnRemove  = (RemoveHook_t) GetProcAddress(hDll, "RemoveHook");
    if (!fnInstall) { FreeLibrary(hDll); return 1; }

    HHOOK hHook = fnInstall();
    if (!hHook) { FreeLibrary(hDll); return 1; }

    // 寫 log 讓外部確認 hook 安裝成功
    FILE* f = nullptr;
    fopen_s(&f, "windower_host.log", "w");
    if (f) { fprintf(f, "Hook installed: %p\n", hHook); fclose(f); }

    // 用訊息迴圈保持存活（直到 WM_QUIT 或被外部 TerminateProcess）
    MSG msg;
    while (GetMessageA(&msg, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&msg);
        DispatchMessageA(&msg);
    }

    if (fnRemove) fnRemove();
    FreeLibrary(hDll);
    return 0;
}
