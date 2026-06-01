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
#include <winsock2.h>
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <stdint.h>
#include "d3d8min.h"

static void EnsureCaptureDirReady();
static char           g_captureDirA[MAX_PATH] = {};
static bool           g_captureDirReady = false;
static volatile LONG  g_logResolvingCaptureDir = 0;

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

    if (!g_captureDirReady &&
        InterlockedCompareExchange(&g_logResolvingCaptureDir, 1, 0) == 0)
    {
        EnsureCaptureDirReady();
        InterlockedExchange(&g_logResolvingCaptureDir, 0);
    }

    if (!g_captureDirReady || !g_captureDirA[0])
        return;

    char logPath[MAX_PATH] = {};
    _snprintf_s(logPath, _countof(logPath), _TRUNCATE, "%s\\windower_inject.log", g_captureDirA);

    FILE* f = nullptr;
    fopen_s(&f, logPath, "a");
    if (f) { fputs(line, f); fclose(f); }
}

typedef int (WSAAPI* Send_t)(SOCKET, const char*, int, int);
typedef int (WSAAPI* Recv_t)(SOCKET, char*, int, int);
typedef int (WSAAPI* WSASend_t)(
    SOCKET, LPWSABUF, DWORD, LPDWORD, DWORD, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
typedef int (WSAAPI* WSARecv_t)(
    SOCKET, LPWSABUF, DWORD, LPDWORD, LPDWORD, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
typedef BOOL (WSAAPI* WSAGetOverlappedResult_t)(
    SOCKET, LPWSAOVERLAPPED, LPDWORD, BOOL, LPDWORD);
typedef BOOL (WINAPI* GetQueuedCompletionStatus_t)(
    HANDLE, LPDWORD, PULONG_PTR, LPOVERLAPPED*, DWORD);

typedef struct InlineDetour {
    const char* name;
    void* target;
    void* detour;
    BYTE saved[5];
    BYTE* trampoline;
    bool installed;
} InlineDetour;

typedef struct PacketSession {
    SOCKET socketValue;
    FILE* file;
    ULONGLONG tsStart;
    unsigned long long seq;
    struct PacketSession* next;
} PacketSession;

typedef struct PendingRecvOp {
    LPWSAOVERLAPPED overlapped;
    SOCKET socketValue;
    WSABUF* buffers;
    DWORD bufferCount;
    ULONGLONG postedAt;
    LPWSAOVERLAPPED_COMPLETION_ROUTINE appCompletionRoutine;
    bool completionRoutineWrapped;
    bool captured;
    struct PendingRecvOp* next;
} PendingRecvOp;

static int WSAAPI HookedSend(SOCKET s, const char* buf, int len, int flags);
static int WSAAPI HookedRecv(SOCKET s, char* buf, int len, int flags);
static int WSAAPI HookedWSASend(
    SOCKET s, LPWSABUF lpBuffers, DWORD dwBufferCount, LPDWORD lpNumberOfBytesSent,
    DWORD dwFlags, LPWSAOVERLAPPED lpOverlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE lpCompletionRoutine);
static int WSAAPI HookedWSARecv(
    SOCKET s, LPWSABUF lpBuffers, DWORD dwBufferCount, LPDWORD lpNumberOfBytesRecvd,
    LPDWORD lpFlags, LPWSAOVERLAPPED lpOverlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE lpCompletionRoutine);
static BOOL WSAAPI HookedWSAGetOverlappedResult(
    SOCKET s, LPWSAOVERLAPPED lpOverlapped, LPDWORD lpcbTransfer, BOOL fWait, LPDWORD lpdwFlags);
static BOOL WINAPI HookedGetQueuedCompletionStatus(
    HANDLE CompletionPort, LPDWORD lpNumberOfBytesTransferred,
    PULONG_PTR lpCompletionKey, LPOVERLAPPED* lpOverlapped, DWORD dwMilliseconds);
static void CALLBACK HookedWSARecvCompletionRoutine(
    DWORD dwError, DWORD cbTransferred, LPWSAOVERLAPPED lpOverlapped, DWORD dwFlags);

// ── 全域狀態 ─────────────────────────────────────────────────────────────────

static CreateDevice_t g_origCreateDevice = nullptr;
static DeviceReset_t  g_origReset        = nullptr;
static DevicePresent_t g_origPresent     = nullptr;
static Direct3DCreate8_t g_origDirect3DCreate8 = nullptr;
static void*          g_direct3DCreate8Addr = nullptr;
static BYTE*          g_direct3DCreate8Trampoline = nullptr;
static BYTE           g_direct3DCreate8Saved[5] = {};

static Send_t         g_origSend = nullptr;
static Recv_t         g_origRecv = nullptr;
static WSASend_t      g_origWSASend = nullptr;
static WSARecv_t      g_origWSARecv = nullptr;
static WSAGetOverlappedResult_t g_origWSAGetOverlappedResult = nullptr;
static GetQueuedCompletionStatus_t g_origGetQueuedCompletionStatus = nullptr;

static InlineDetour   g_sendDetour = { "send", nullptr, (void*)HookedSend, {}, nullptr, false };
static InlineDetour   g_recvDetour = { "recv", nullptr, (void*)HookedRecv, {}, nullptr, false };
static InlineDetour   g_wsaSendDetour = { "WSASend", nullptr, (void*)HookedWSASend, {}, nullptr, false };
static InlineDetour   g_wsaRecvDetour = { "WSARecv", nullptr, (void*)HookedWSARecv, {}, nullptr, false };
static InlineDetour   g_wsaGetOverlappedResultDetour = { "WSAGetOverlappedResult", nullptr, (void*)HookedWSAGetOverlappedResult, {}, nullptr, false };
static InlineDetour   g_getQueuedCompletionStatusDetour = { "GetQueuedCompletionStatus", nullptr, (void*)HookedGetQueuedCompletionStatus, {}, nullptr, false };

static HHOOK          g_hHook            = nullptr;
static HINSTANCE      g_hInst            = nullptr;
static bool           g_d3d8EntryHooked  = false;
static bool           g_createDeviceHooked = false;
static bool           g_deviceVtableHooked = false;
static bool           g_ws2HooksAttempted = false;
static volatile LONG  g_presentLoggedOnce = 0;
static volatile LONG  g_resetLoggedOnce   = 0;
static HWND           g_gameWindow         = nullptr;
static UINT           g_backBufferWidth    = 800;
static UINT           g_backBufferHeight   = 600;
static D3DFORMAT      g_cachedDesktopFormat = D3DFMT_UNKNOWN;
static bool           g_hasCachedDesktopFormat = false;
static CRITICAL_SECTION g_packetLock = {};
static bool           g_packetLockReady = false;
static PacketSession* g_packetSessions = nullptr;
static PendingRecvOp* g_pendingRecvOps = nullptr;

enum { D3DADAPTER_DEFAULT_LOCAL = 0 };
enum { D3DFMT_X8R8G8B8_LOCAL = 22 };

typedef struct D3DDISPLAYMODE_LOCAL {
    UINT Width;
    UINT Height;
    UINT RefreshRate;
    D3DFORMAT Format;
} D3DDISPLAYMODE_LOCAL;

// ── inline detour / 封包擷取工具 ───────────────────────────────────────────────

static bool InstallInlineDetour(InlineDetour* hook)
{
    if (!hook || hook->installed || !hook->target || !hook->detour)
        return hook && hook->installed;

    BYTE* target = (BYTE*)hook->target;
    memcpy(hook->saved, target, sizeof(hook->saved));

    hook->trampoline = (BYTE*)VirtualAlloc(nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!hook->trampoline)
    {
        WriteLog("[Windower] %s detour trampoline alloc failed err=%lu",
            hook->name, (unsigned long)GetLastError());
        return false;
    }

    memcpy(hook->trampoline, target, 5);
    hook->trampoline[5] = 0xE9;
    *(int32_t*)(hook->trampoline + 6) = (int32_t)((target + 5) - (hook->trampoline + 10));

    DWORD oldProt = 0;
    if (!VirtualProtect(target, 5, PAGE_EXECUTE_READWRITE, &oldProt))
    {
        WriteLog("[Windower] %s detour VirtualProtect failed err=%lu",
            hook->name, (unsigned long)GetLastError());
        VirtualFree(hook->trampoline, 0, MEM_RELEASE);
        hook->trampoline = nullptr;
        return false;
    }

    target[0] = 0xE9;
    *(int32_t*)(target + 1) = (int32_t)((BYTE*)hook->detour - (target + 5));
    VirtualProtect(target, 5, oldProt, &oldProt);
    FlushInstructionCache(GetCurrentProcess(), target, 5);

    hook->installed = true;
    WriteLog("[Windower] %s detour installed target=%p detour=%p",
        hook->name, hook->target, hook->detour);
    return true;
}

static bool IsCaptureEnabled()
{
    char value[16] = {};
    DWORD n = GetEnvironmentVariableA("MAPLEFORGE_WINDOWER_CAPTURE", value, (DWORD)_countof(value));
    return (n > 0 && n < _countof(value) && strcmp(value, "1") == 0);
}

static void EnsureDirectoryW(const wchar_t* path)
{
    if (!path || !path[0]) return;
    if (!CreateDirectoryW(path, nullptr))
    {
        DWORD err = GetLastError();
        if (err != ERROR_ALREADY_EXISTS)
            WriteLog("[Windower] CreateDirectoryW failed path=%ws err=%lu", path, (unsigned long)err);
    }
}

static void EnsureCaptureDirReady()
{
    if (g_captureDirReady)
        return;

    char envDir[MAX_PATH] = {};
    DWORD envLen = GetEnvironmentVariableA(
        "MAPLEFORGE_WINDOWER_CAPTURE_DIR", envDir, (DWORD)_countof(envDir));
    if (envLen > 0 && envLen < _countof(envDir))
    {
        wchar_t envDirW[MAX_PATH] = {};
        if (MultiByteToWideChar(CP_ACP, 0, envDir, -1, envDirW, (int)_countof(envDirW)) > 0)
            EnsureDirectoryW(envDirW);
        _snprintf_s(g_captureDirA, _countof(g_captureDirA), _TRUNCATE, "%s", envDir);
        g_captureDirReady = true;
        return;
    }

    wchar_t dllPathW[MAX_PATH] = {};
    DWORD got = GetModuleFileNameW(g_hInst, dllPathW, (DWORD)_countof(dllPathW));
    if (got > 0 && got < _countof(dllPathW))
    {
        wchar_t* slash = wcsrchr(dllPathW, L'\\');
        if (slash) *slash = L'\0';

        wchar_t capturesDirW[MAX_PATH] = {};
        _snwprintf_s(capturesDirW, _countof(capturesDirW), _TRUNCATE, L"%s\\captures", dllPathW);
        EnsureDirectoryW(capturesDirW);

        if (WideCharToMultiByte(CP_ACP, 0, capturesDirW, -1, g_captureDirA, (int)_countof(g_captureDirA), nullptr, nullptr) > 0)
        {
            g_captureDirReady = true;
            return;
        }
    }

    g_captureDirReady = false;
}

static DWORD ExtractBytesForSend(int ret, DWORD fallbackTotal)
{
    if (ret > 0) return (DWORD)ret;
    if (ret == 0) return fallbackTotal;
    return 0;
}

static PacketSession* GetOrCreatePacketSessionLocked(SOCKET s)
{
    EnsureCaptureDirReady();
    if (!g_captureDirReady || !g_captureDirA[0])
        return nullptr;

    PacketSession* cur = g_packetSessions;
    while (cur)
    {
        if (cur->socketValue == s)
            return cur;
        cur = cur->next;
    }

    PacketSession* session = (PacketSession*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(PacketSession));
    if (!session) return nullptr;

    session->socketValue = s;
    session->tsStart = GetTickCount64();

    char path[MAX_PATH] = {};
    _snprintf_s(
        path, _countof(path), _TRUNCATE,
        "%s\\windower_packets_%lu_%llu.ndjson",
        g_captureDirA,
        (unsigned long)GetCurrentProcessId(),
        (unsigned long long)(uintptr_t)s);

    fopen_s(&session->file, path, "w");
    if (!session->file)
    {
        HeapFree(GetProcessHeap(), 0, session);
        return nullptr;
    }

    fprintf(
        session->file,
        "{\"type\":\"session\",\"pid\":%lu,\"socket\":%llu,\"ts_start\":%llu}\n",
        (unsigned long)GetCurrentProcessId(),
        (unsigned long long)(uintptr_t)s,
        (unsigned long long)session->tsStart);
    fflush(session->file);

    session->next = g_packetSessions;
    g_packetSessions = session;
    return session;
}

static void WriteHexToFile(FILE* file, const unsigned char* data, DWORD len)
{
    static const char kHex[] = "0123456789abcdef";
    if (!file || !data || len == 0) return;
    for (DWORD i = 0; i < len; ++i)
    {
        unsigned char b = data[i];
        fputc(kHex[b >> 4], file);
        fputc(kHex[b & 0x0F], file);
    }
}

static DWORD SumWSABuffers(const WSABUF* bufs, DWORD count)
{
    DWORD total = 0;
    if (!bufs) return 0;
    for (DWORD i = 0; i < count; ++i)
        total += bufs[i].len;
    return total;
}

static void WriteChunkSingleBuffer(
    SOCKET s, const char* dir, const char* api, int ret, const unsigned char* data, DWORD len, bool pendingTodo)
{
    if (!IsCaptureEnabled() || !g_packetLockReady)
        return;

    EnterCriticalSection(&g_packetLock);
    PacketSession* session = GetOrCreatePacketSessionLocked(s);
    if (session && session->file)
    {
        const unsigned long long seq = ++session->seq;
        const unsigned long long ts = (unsigned long long)GetTickCount64();
        fprintf(
            session->file,
            "{\"type\":\"chunk\",\"seq\":%llu,\"dir\":\"%s\",\"ts\":%llu,\"raw_hex\":\"",
            seq, dir, ts);
        WriteHexToFile(session->file, data, len);
        fprintf(session->file, "\",\"api\":\"%s\",\"ret\":%d", api, ret);
        if (pendingTodo)
            fprintf(session->file, ",\"todo\":\"overlapped_pending_not_captured\"");
        fprintf(session->file, "}\n");
        fflush(session->file);
    }
    LeaveCriticalSection(&g_packetLock);
}

static void WriteChunkWSABuffersLocked(
    SOCKET s, const char* dir, const char* api, int ret, const WSABUF* bufs, DWORD count, DWORD bytesToWrite, bool pendingTodo)
{
    PacketSession* session = GetOrCreatePacketSessionLocked(s);
    if (session && session->file)
    {
        const unsigned long long seq = ++session->seq;
        const unsigned long long ts = (unsigned long long)GetTickCount64();
        fprintf(
            session->file,
            "{\"type\":\"chunk\",\"seq\":%llu,\"dir\":\"%s\",\"ts\":%llu,\"raw_hex\":\"",
            seq, dir, ts);

        DWORD remaining = bytesToWrite;
        for (DWORD i = 0; bufs && i < count && remaining > 0; ++i)
        {
            const DWORD part = (bufs[i].len < remaining) ? bufs[i].len : remaining;
            WriteHexToFile(session->file, (const unsigned char*)bufs[i].buf, part);
            remaining -= part;
        }

        fprintf(session->file, "\",\"api\":\"%s\",\"ret\":%d", api, ret);
        if (pendingTodo)
            fprintf(session->file, ",\"todo\":\"overlapped_pending_not_captured\"");
        fprintf(session->file, "}\n");
        fflush(session->file);
    }
}

static void WriteChunkWSABuffers(
    SOCKET s, const char* dir, const char* api, int ret, const WSABUF* bufs, DWORD count, DWORD bytesToWrite, bool pendingTodo)
{
    if (!IsCaptureEnabled() || !g_packetLockReady)
        return;

    EnterCriticalSection(&g_packetLock);
    WriteChunkWSABuffersLocked(s, dir, api, ret, bufs, count, bytesToWrite, pendingTodo);
    LeaveCriticalSection(&g_packetLock);
}

static int RetFromBytes(DWORD bytes)
{
    return (bytes > 0x7FFFFFFFUL) ? 0x7FFFFFFF : (int)bytes;
}

static void FreePendingRecvOp(PendingRecvOp* op)
{
    if (!op) return;
    if (op->buffers) HeapFree(GetProcessHeap(), 0, op->buffers);
    HeapFree(GetProcessHeap(), 0, op);
}

static PendingRecvOp* FindPendingRecvLocked(LPWSAOVERLAPPED overlapped, PendingRecvOp** prevOut)
{
    if (prevOut) *prevOut = nullptr;
    PendingRecvOp* prev = nullptr;
    PendingRecvOp* cur = g_pendingRecvOps;
    while (cur)
    {
        if (cur->overlapped == overlapped)
        {
            if (prevOut) *prevOut = prev;
            return cur;
        }
        prev = cur;
        cur = cur->next;
    }
    return nullptr;
}

static void RemovePendingRecvLocked(PendingRecvOp* op, PendingRecvOp* prev)
{
    if (!op) return;
    if (prev) prev->next = op->next;
    else g_pendingRecvOps = op->next;
    op->next = nullptr;
    FreePendingRecvOp(op);
}

static void RemovePendingRecvByOverlappedLocked(LPWSAOVERLAPPED overlapped)
{
    PendingRecvOp* prev = nullptr;
    PendingRecvOp* op = FindPendingRecvLocked(overlapped, &prev);
    if (op)
        RemovePendingRecvLocked(op, prev);
}

static bool TrackPendingWSARecv(
    SOCKET s,
    LPWSABUF lpBuffers,
    DWORD dwBufferCount,
    LPWSAOVERLAPPED lpOverlapped,
    LPWSAOVERLAPPED_COMPLETION_ROUTINE lpCompletionRoutine)
{
    if (!IsCaptureEnabled() || !g_packetLockReady || !lpOverlapped || !lpBuffers || dwBufferCount == 0)
        return false;

    if (dwBufferCount > (0x7FFFFFFFUL / sizeof(WSABUF)))
        return false;

    const SIZE_T bytes = (SIZE_T)dwBufferCount * sizeof(WSABUF);
    WSABUF* copy = (WSABUF*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, bytes);
    PendingRecvOp* op = (PendingRecvOp*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(PendingRecvOp));
    if (!copy || !op)
    {
        if (copy) HeapFree(GetProcessHeap(), 0, copy);
        if (op) HeapFree(GetProcessHeap(), 0, op);
        return false;
    }

    memcpy(copy, lpBuffers, bytes);
    op->overlapped = lpOverlapped;
    op->socketValue = s;
    op->buffers = copy;
    op->bufferCount = dwBufferCount;
    op->postedAt = GetTickCount64();
    op->appCompletionRoutine = lpCompletionRoutine;
    op->completionRoutineWrapped = (lpCompletionRoutine != nullptr);
    op->captured = false;

    EnterCriticalSection(&g_packetLock);
    RemovePendingRecvByOverlappedLocked(lpOverlapped);
    op->next = g_pendingRecvOps;
    g_pendingRecvOps = op;
    LeaveCriticalSection(&g_packetLock);
    return true;
}

static bool CompletePendingWSARecvFromPoll(
    LPWSAOVERLAPPED lpOverlapped,
    DWORD bytesTransferred,
    const char* api,
    bool success,
    bool terminal)
{
    if (!g_packetLockReady || !lpOverlapped)
        return false;

    bool found = false;
    EnterCriticalSection(&g_packetLock);
    PendingRecvOp* prev = nullptr;
    PendingRecvOp* op = FindPendingRecvLocked(lpOverlapped, &prev);
    if (op)
    {
        found = true;
        if (success && !op->captured)
        {
            WriteChunkWSABuffersLocked(
                op->socketValue, "s2c", api, RetFromBytes(bytesTransferred),
                op->buffers, op->bufferCount, bytesTransferred, false);
            op->captured = true;
        }

        if (terminal && !op->completionRoutineWrapped)
            RemovePendingRecvLocked(op, prev);
    }
    LeaveCriticalSection(&g_packetLock);
    return found;
}

static LPWSAOVERLAPPED_COMPLETION_ROUTINE CompletePendingWSARecvFromRoutine(
    LPWSAOVERLAPPED lpOverlapped,
    DWORD dwError,
    DWORD bytesTransferred)
{
    if (!g_packetLockReady || !lpOverlapped)
        return nullptr;

    LPWSAOVERLAPPED_COMPLETION_ROUTINE appRoutine = nullptr;

    EnterCriticalSection(&g_packetLock);
    PendingRecvOp* prev = nullptr;
    PendingRecvOp* op = FindPendingRecvLocked(lpOverlapped, &prev);
    if (op)
    {
        appRoutine = op->appCompletionRoutine;
        if (dwError == 0 && !op->captured)
        {
            WriteChunkWSABuffersLocked(
                op->socketValue, "s2c", "WSARecv-complete-routine", RetFromBytes(bytesTransferred),
                op->buffers, op->bufferCount, bytesTransferred, false);
            op->captured = true;
        }
        RemovePendingRecvLocked(op, prev);
    }
    LeaveCriticalSection(&g_packetLock);

    return appRoutine;
}

static void CleanupPacketSessions()
{
    if (!g_packetLockReady)
        return;

    EnterCriticalSection(&g_packetLock);
    PendingRecvOp* pending = g_pendingRecvOps;
    while (pending)
    {
        PendingRecvOp* next = pending->next;
        FreePendingRecvOp(pending);
        pending = next;
    }
    g_pendingRecvOps = nullptr;

    PacketSession* cur = g_packetSessions;
    while (cur)
    {
        PacketSession* next = cur->next;
        if (cur->file) fclose(cur->file);
        HeapFree(GetProcessHeap(), 0, cur);
        cur = next;
    }
    g_packetSessions = nullptr;
    LeaveCriticalSection(&g_packetLock);
}

static void InstallWs2Hooks()
{
    if (g_ws2HooksAttempted)
        return;
    g_ws2HooksAttempted = true;

    HMODULE hWs2 = GetModuleHandleA("ws2_32.dll");
    if (!hWs2) hWs2 = LoadLibraryA("ws2_32.dll");
    if (!hWs2)
    {
        WriteLog("[Windower] ws2_32.dll not found");
        return;
    }

    g_sendDetour.target = (void*)GetProcAddress(hWs2, "send");
    g_recvDetour.target = (void*)GetProcAddress(hWs2, "recv");
    g_wsaSendDetour.target = (void*)GetProcAddress(hWs2, "WSASend");
    g_wsaRecvDetour.target = (void*)GetProcAddress(hWs2, "WSARecv");
    g_wsaGetOverlappedResultDetour.target = (void*)GetProcAddress(hWs2, "WSAGetOverlappedResult");

    if (g_sendDetour.target)
    {
        if (InstallInlineDetour(&g_sendDetour))
            g_origSend = (Send_t)g_sendDetour.trampoline;
        else
            g_origSend = (Send_t)g_sendDetour.target;
    }
    if (g_recvDetour.target)
    {
        if (InstallInlineDetour(&g_recvDetour))
            g_origRecv = (Recv_t)g_recvDetour.trampoline;
        else
            g_origRecv = (Recv_t)g_recvDetour.target;
    }
    if (g_wsaSendDetour.target)
    {
        if (InstallInlineDetour(&g_wsaSendDetour))
            g_origWSASend = (WSASend_t)g_wsaSendDetour.trampoline;
        else
            g_origWSASend = (WSASend_t)g_wsaSendDetour.target;
    }
    if (g_wsaRecvDetour.target)
    {
        if (InstallInlineDetour(&g_wsaRecvDetour))
            g_origWSARecv = (WSARecv_t)g_wsaRecvDetour.trampoline;
        else
            g_origWSARecv = (WSARecv_t)g_wsaRecvDetour.target;
    }
    if (g_wsaGetOverlappedResultDetour.target)
    {
        if (InstallInlineDetour(&g_wsaGetOverlappedResultDetour))
            g_origWSAGetOverlappedResult = (WSAGetOverlappedResult_t)g_wsaGetOverlappedResultDetour.trampoline;
        else
            g_origWSAGetOverlappedResult = (WSAGetOverlappedResult_t)g_wsaGetOverlappedResultDetour.target;
    }

    HMODULE hKernel32 = GetModuleHandleA("kernel32.dll");
    if (hKernel32)
    {
        g_getQueuedCompletionStatusDetour.target = (void*)GetProcAddress(hKernel32, "GetQueuedCompletionStatus");
        if (g_getQueuedCompletionStatusDetour.target)
        {
            if (InstallInlineDetour(&g_getQueuedCompletionStatusDetour))
                g_origGetQueuedCompletionStatus = (GetQueuedCompletionStatus_t)g_getQueuedCompletionStatusDetour.trampoline;
            else
                g_origGetQueuedCompletionStatus = (GetQueuedCompletionStatus_t)g_getQueuedCompletionStatusDetour.target;
        }
    }

    WriteLog(
        "[Windower] hooks status send=%d recv=%d WSASend=%d WSARecv=%d WSAGetOverlappedResult=%d GetQueuedCompletionStatus=%d",
        g_sendDetour.installed ? 1 : 0,
        g_recvDetour.installed ? 1 : 0,
        g_wsaSendDetour.installed ? 1 : 0,
        g_wsaRecvDetour.installed ? 1 : 0,
        g_wsaGetOverlappedResultDetour.installed ? 1 : 0,
        g_getQueuedCompletionStatusDetour.installed ? 1 : 0);
}

static int WSAAPI HookedSend(SOCKET s, const char* buf, int len, int flags)
{
    Send_t orig = g_origSend ? g_origSend : (Send_t)g_sendDetour.target;
    if (!orig) return SOCKET_ERROR;

    int ret = orig(s, buf, len, flags);
    DWORD fallback = (len > 0) ? (DWORD)len : 0;
    DWORD bytes = ExtractBytesForSend(ret, fallback);

    WriteChunkSingleBuffer(s, "c2s", "send", ret, (const unsigned char*)buf, bytes, false);
    return ret;
}

static int WSAAPI HookedRecv(SOCKET s, char* buf, int len, int flags)
{
    Recv_t orig = g_origRecv ? g_origRecv : (Recv_t)g_recvDetour.target;
    if (!orig) return SOCKET_ERROR;

    int ret = orig(s, buf, len, flags);
    DWORD bytes = (ret > 0) ? (DWORD)ret : 0;

    WriteChunkSingleBuffer(s, "s2c", "recv", ret, (const unsigned char*)buf, bytes, false);
    return ret;
}

static int WSAAPI HookedWSASend(
    SOCKET s, LPWSABUF lpBuffers, DWORD dwBufferCount, LPDWORD lpNumberOfBytesSent,
    DWORD dwFlags, LPWSAOVERLAPPED lpOverlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE lpCompletionRoutine)
{
    WSASend_t orig = g_origWSASend ? g_origWSASend : (WSASend_t)g_wsaSendDetour.target;
    if (!orig) return SOCKET_ERROR;

    int ret = orig(s, lpBuffers, dwBufferCount, lpNumberOfBytesSent, dwFlags, lpOverlapped, lpCompletionRoutine);
    bool pendingTodo = false;
    DWORD bytes = 0;

    if (ret == 0)
    {
        if (lpNumberOfBytesSent)
            bytes = *lpNumberOfBytesSent;
        else
            bytes = SumWSABuffers(lpBuffers, dwBufferCount);
    }
    else if (ret == SOCKET_ERROR && lpOverlapped && WSAGetLastError() == WSA_IO_PENDING)
    {
        pendingTodo = true;
    }

    if (pendingTodo)
    {
        WriteChunkWSABuffers(s, "c2s", "WSASend", ret, lpBuffers, dwBufferCount, 0, true);
    }
    else
    {
        WriteChunkWSABuffers(s, "c2s", "WSASend", ret, lpBuffers, dwBufferCount, bytes, false);
    }
    return ret;
}

static int WSAAPI HookedWSARecv(
    SOCKET s, LPWSABUF lpBuffers, DWORD dwBufferCount, LPDWORD lpNumberOfBytesRecvd,
    LPDWORD lpFlags, LPWSAOVERLAPPED lpOverlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE lpCompletionRoutine)
{
    WSARecv_t orig = g_origWSARecv ? g_origWSARecv : (WSARecv_t)g_wsaRecvDetour.target;
    if (!orig) return SOCKET_ERROR;

    const bool tracked = TrackPendingWSARecv(s, lpBuffers, dwBufferCount, lpOverlapped, lpCompletionRoutine);
    LPWSAOVERLAPPED_COMPLETION_ROUTINE completionRoutineToPass =
        (tracked && lpCompletionRoutine) ? HookedWSARecvCompletionRoutine : lpCompletionRoutine;

    int ret = orig(s, lpBuffers, dwBufferCount, lpNumberOfBytesRecvd, lpFlags, lpOverlapped, completionRoutineToPass);
    const int wsaErr = (ret == SOCKET_ERROR) ? WSAGetLastError() : 0;
    bool pendingTodo = false;
    DWORD bytes = 0;
    bool wrote = false;

    if (ret == 0)
    {
        if (lpNumberOfBytesRecvd)
            bytes = *lpNumberOfBytesRecvd;
        else if (!lpOverlapped)
            bytes = SumWSABuffers(lpBuffers, dwBufferCount);

        if (tracked && lpOverlapped)
        {
            if (lpNumberOfBytesRecvd)
            {
                CompletePendingWSARecvFromPoll(lpOverlapped, bytes, "WSARecv-complete", true, true);
                wrote = true;
                WriteLog(
                    "[Windower] WSARecv completed at submit tracked=1 s=%llu ov=%p bytes=%lu completionRoutine=%p hEvent=%p",
                    (unsigned long long)(uintptr_t)s, (void*)lpOverlapped, (unsigned long)bytes,
                    (void*)lpCompletionRoutine, lpOverlapped ? (void*)lpOverlapped->hEvent : nullptr);
            }
            else
            {
                WriteChunkWSABuffers(s, "s2c", "WSARecv", ret, lpBuffers, dwBufferCount, 0, false);
                wrote = true;
                WriteLog(
                    "[Windower] WSARecv submitted/complete without byte count tracked=1 s=%llu ov=%p completionRoutine=%p hEvent=%p",
                    (unsigned long long)(uintptr_t)s, (void*)lpOverlapped,
                    (void*)lpCompletionRoutine, lpOverlapped ? (void*)lpOverlapped->hEvent : nullptr);
            }
        }
    }
    else if (ret == SOCKET_ERROR && lpOverlapped && wsaErr == WSA_IO_PENDING)
    {
        if (tracked)
        {
            WriteChunkWSABuffers(s, "s2c", "WSARecv", ret, lpBuffers, dwBufferCount, 0, false);
            wrote = true;
            WriteLog(
                "[Windower] WSARecv pending tracked=1 s=%llu ov=%p buffers=%lu completionRoutine=%p hEvent=%p",
                (unsigned long long)(uintptr_t)s, (void*)lpOverlapped, (unsigned long)dwBufferCount,
                (void*)lpCompletionRoutine, lpOverlapped ? (void*)lpOverlapped->hEvent : nullptr);
        }
        else
        {
            pendingTodo = true;
        }
    }
    else if (ret == SOCKET_ERROR && tracked && lpOverlapped)
    {
        EnterCriticalSection(&g_packetLock);
        RemovePendingRecvByOverlappedLocked(lpOverlapped);
        LeaveCriticalSection(&g_packetLock);
    }

    if (!wrote && pendingTodo)
    {
        WriteChunkWSABuffers(s, "s2c", "WSARecv", ret, lpBuffers, dwBufferCount, 0, true);
    }
    else if (!wrote)
    {
        WriteChunkWSABuffers(s, "s2c", "WSARecv", ret, lpBuffers, dwBufferCount, bytes, false);
    }

    if (ret == SOCKET_ERROR)
        WSASetLastError(wsaErr);
    return ret;
}

static BOOL WSAAPI HookedWSAGetOverlappedResult(
    SOCKET s, LPWSAOVERLAPPED lpOverlapped, LPDWORD lpcbTransfer, BOOL fWait, LPDWORD lpdwFlags)
{
    WSAGetOverlappedResult_t orig =
        g_origWSAGetOverlappedResult ? g_origWSAGetOverlappedResult :
        (WSAGetOverlappedResult_t)g_wsaGetOverlappedResultDetour.target;
    if (!orig) return FALSE;

    BOOL ret = orig(s, lpOverlapped, lpcbTransfer, fWait, lpdwFlags);
    const int wsaErr = ret ? 0 : WSAGetLastError();
    const DWORD lastErr = GetLastError();
    const DWORD bytes = (ret && lpcbTransfer) ? *lpcbTransfer : 0;

    if (ret)
    {
        if (CompletePendingWSARecvFromPoll(lpOverlapped, bytes, "WSARecv-complete", true, true))
        {
            WriteLog(
                "[Windower] WSARecv completion via WSAGetOverlappedResult s=%llu ov=%p bytes=%lu fWait=%d",
                (unsigned long long)(uintptr_t)s, (void*)lpOverlapped, (unsigned long)bytes, (int)fWait);
        }
    }
    else if (wsaErr != WSA_IO_INCOMPLETE)
    {
        if (CompletePendingWSARecvFromPoll(lpOverlapped, 0, "WSARecv-complete", false, true))
        {
            WriteLog(
                "[Windower] WSARecv terminal error via WSAGetOverlappedResult s=%llu ov=%p err=%d fWait=%d",
                (unsigned long long)(uintptr_t)s, (void*)lpOverlapped, wsaErr, (int)fWait);
        }
    }

    if (!ret)
        WSASetLastError(wsaErr);
    SetLastError(lastErr);
    return ret;
}

static BOOL WINAPI HookedGetQueuedCompletionStatus(
    HANDLE CompletionPort, LPDWORD lpNumberOfBytesTransferred,
    PULONG_PTR lpCompletionKey, LPOVERLAPPED* lpOverlapped, DWORD dwMilliseconds)
{
    GetQueuedCompletionStatus_t orig =
        g_origGetQueuedCompletionStatus ? g_origGetQueuedCompletionStatus :
        (GetQueuedCompletionStatus_t)g_getQueuedCompletionStatusDetour.target;
    if (!orig) return FALSE;

    BOOL ret = orig(CompletionPort, lpNumberOfBytesTransferred, lpCompletionKey, lpOverlapped, dwMilliseconds);
    const DWORD lastErr = GetLastError();
    LPOVERLAPPED completedOverlapped = lpOverlapped ? *lpOverlapped : nullptr;
    const DWORD bytes = (ret && lpNumberOfBytesTransferred) ? *lpNumberOfBytesTransferred : 0;

    if (completedOverlapped)
    {
        if (ret)
        {
            if (CompletePendingWSARecvFromPoll((LPWSAOVERLAPPED)completedOverlapped, bytes, "WSARecv-complete-iocp", true, true))
            {
                WriteLog(
                    "[Windower] WSARecv completion via GetQueuedCompletionStatus port=%p ov=%p bytes=%lu",
                    (void*)CompletionPort, (void*)completedOverlapped, (unsigned long)bytes);
            }
        }
        else
        {
            if (CompletePendingWSARecvFromPoll((LPWSAOVERLAPPED)completedOverlapped, 0, "WSARecv-complete-iocp", false, true))
            {
                WriteLog(
                    "[Windower] WSARecv terminal error via GetQueuedCompletionStatus port=%p ov=%p err=%lu",
                    (void*)CompletionPort, (void*)completedOverlapped, (unsigned long)lastErr);
            }
        }
    }

    SetLastError(lastErr);
    return ret;
}

static void CALLBACK HookedWSARecvCompletionRoutine(
    DWORD dwError, DWORD cbTransferred, LPWSAOVERLAPPED lpOverlapped, DWORD dwFlags)
{
    const DWORD lastErr = GetLastError();
    const int wsaErr = WSAGetLastError();

    LPWSAOVERLAPPED_COMPLETION_ROUTINE appRoutine =
        CompletePendingWSARecvFromRoutine(lpOverlapped, dwError, cbTransferred);

    if (dwError == 0)
    {
        WriteLog(
            "[Windower] WSARecv completion via completion routine ov=%p bytes=%lu flags=0x%08lX appRoutine=%p",
            (void*)lpOverlapped, (unsigned long)cbTransferred, (unsigned long)dwFlags, (void*)appRoutine);
    }
    else
    {
        WriteLog(
            "[Windower] WSARecv terminal error via completion routine ov=%p err=%lu appRoutine=%p",
            (void*)lpOverlapped, (unsigned long)dwError, (void*)appRoutine);
    }

    WSASetLastError(wsaErr);
    SetLastError(lastErr);

    if (appRoutine)
        appRoutine(dwError, cbTransferred, lpOverlapped, dwFlags);
}

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
    InstallWs2Hooks();
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
        InitializeCriticalSection(&g_packetLock);
        g_packetLockReady = true;
        WriteLog("[Windower] DllMain DLL_PROCESS_ATTACH");
        EnsureHooksInstalled();
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        CleanupPacketSessions();
        if (g_packetLockReady)
        {
            DeleteCriticalSection(&g_packetLock);
            g_packetLockReady = false;
        }
    }
    return TRUE;
}
