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
typedef HRESULT (WINAPI* DirectInput8Create_t)(HINSTANCE, DWORD, REFIID, LPVOID*, void*);
typedef HRESULT (WINAPI* DirectInputCreateA_t)(HINSTANCE, DWORD, void**, void*);
typedef HRESULT (WINAPI* DirectInputCreateW_t)(HINSTANCE, DWORD, void**, void*);
typedef HRESULT (WINAPI* DI8CreateDevice_t)(void*, REFGUID, void**, void*);
typedef HRESULT (WINAPI* DIGetDeviceState_t)(void*, DWORD, LPVOID);
typedef HRESULT (WINAPI* DIGetDeviceData_t)(void*, DWORD, void*, LPDWORD, DWORD);
typedef SHORT (WINAPI* GetAsyncKeyState_t)(int);
typedef BOOL (WINAPI* GetKeyboardState_t)(PBYTE);

typedef struct InlineDetour {
    const char* name;
    void* target;
    void* detour;
    BYTE saved[32];
    BYTE* trampoline;
    bool installed;
    BYTE savedBefore[5];
    SIZE_T copiedLen;
    SIZE_T trampolineLen;
    SIZE_T patchLen;
    bool hotpatchMode;
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

typedef struct KbdInjectKey {
    BYTE dik;
    BYTE vk;
    bool shift;
} KbdInjectKey;

typedef struct DIDEVICEOBJECTDATA_LOCAL {
    DWORD dwOfs;
    DWORD dwData;
    DWORD dwTimeStamp;
    DWORD dwSequence;
    ULONG_PTR uAppData;
} DIDEVICEOBJECTDATA_LOCAL;

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
static HRESULT WINAPI HookedDirectInput8Create(HINSTANCE hinst, DWORD dwVersion, REFIID riidltf, LPVOID* ppvOut, void* punkOuter);
static HRESULT WINAPI HookedDirectInputCreateA(HINSTANCE hinst, DWORD dwVersion, void** ppvOut, void* punkOuter);
static HRESULT WINAPI HookedDirectInputCreateW(HINSTANCE hinst, DWORD dwVersion, void** ppvOut, void* punkOuter);
static HRESULT WINAPI HookedDI8CreateDevice(void* self, REFGUID rguid, void** deviceOut, void* punkOuter);
static HRESULT WINAPI HookedDIGetDeviceState(void* self, DWORD cbData, LPVOID lpvData);
static HRESULT WINAPI HookedDIGetDeviceData(void* self, DWORD cbObjectData, void* rgdod, LPDWORD pdwInOut, DWORD dwFlags);
static SHORT WINAPI HookedGetAsyncKeyState(int vKey);
static BOOL WINAPI HookedGetKeyboardState(PBYTE lpKeyState);

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
static DirectInput8Create_t g_origDirectInput8Create = nullptr;
static DirectInputCreateA_t g_origDirectInputCreateA = nullptr;
static DirectInputCreateW_t g_origDirectInputCreateW = nullptr;
static DI8CreateDevice_t g_origDI8CreateDevice = nullptr;
static DIGetDeviceState_t g_origDIGetDeviceState = nullptr;
static DIGetDeviceData_t g_origDIGetDeviceData = nullptr;
static GetAsyncKeyState_t g_origGetAsyncKeyState = nullptr;
static GetKeyboardState_t g_origGetKeyboardState = nullptr;

static InlineDetour   g_sendDetour = { "send", nullptr, (void*)HookedSend, {}, nullptr, false };
static InlineDetour   g_recvDetour = { "recv", nullptr, (void*)HookedRecv, {}, nullptr, false };
static InlineDetour   g_wsaSendDetour = { "WSASend", nullptr, (void*)HookedWSASend, {}, nullptr, false };
static InlineDetour   g_wsaRecvDetour = { "WSARecv", nullptr, (void*)HookedWSARecv, {}, nullptr, false };
static InlineDetour   g_wsaGetOverlappedResultDetour = { "WSAGetOverlappedResult", nullptr, (void*)HookedWSAGetOverlappedResult, {}, nullptr, false };
static InlineDetour   g_getQueuedCompletionStatusDetour = { "GetQueuedCompletionStatus", nullptr, (void*)HookedGetQueuedCompletionStatus, {}, nullptr, false };
static InlineDetour   g_directInput8CreateDetour = { "DirectInput8Create", nullptr, (void*)HookedDirectInput8Create, {}, nullptr, false };
static InlineDetour   g_directInputCreateADetour = { "DirectInputCreateA", nullptr, (void*)HookedDirectInputCreateA, {}, nullptr, false };
static InlineDetour   g_directInputCreateWDetour = { "DirectInputCreateW", nullptr, (void*)HookedDirectInputCreateW, {}, nullptr, false };
static InlineDetour   g_getAsyncKeyStateDetour = { "GetAsyncKeyState", nullptr, (void*)HookedGetAsyncKeyState, {}, nullptr, false };
static InlineDetour   g_getKeyboardStateDetour = { "GetKeyboardState", nullptr, (void*)HookedGetKeyboardState, {}, nullptr, false };

static HHOOK          g_hHook            = nullptr;
static HINSTANCE      g_hInst            = nullptr;
static bool           g_d3d8EntryHooked  = false;
static bool           g_createDeviceHooked = false;
static bool           g_deviceVtableHooked = false;
static bool           g_ws2HooksAttempted = false;
static bool           g_ws2HooksSkippedByEnv = false;
static bool           g_d3d8HookSkippedByEnv = false;
static bool           g_keyboardHooksAttempted = false;
static bool           g_keyboardDetectionLogged = false;
static bool           g_di8CreateDeviceHooked = false;
static bool           g_diKeyboardDeviceHooked = false;
static volatile LONG  g_presentLoggedOnce = 0;
static volatile LONG  g_resetLoggedOnce   = 0;
static HWND           g_gameWindow         = nullptr;
static UINT           g_backBufferWidth    = 800;
static UINT           g_backBufferHeight   = 600;
static D3DFORMAT      g_cachedDesktopFormat = D3DFMT_UNKNOWN;
static bool           g_hasCachedDesktopFormat = false;
static CRITICAL_SECTION g_packetLock = {};
static bool           g_packetLockReady = false;
static CRITICAL_SECTION g_keyboardLock = {};
static bool           g_keyboardLockReady = false;
static PacketSession* g_packetSessions = nullptr;
static PendingRecvOp* g_pendingRecvOps = nullptr;
static char           g_keyboardFileA[MAX_PATH] = {};
static bool           g_keyboardFileReady = false;
static bool           g_keyboardFileMissingLogged = false;
static ULONGLONG      g_keyboardLastFilePoll = 0;
static KbdInjectKey   g_keyboardQueue[1024] = {};
static DWORD          g_keyboardQueueCount = 0;
static DWORD          g_keyboardQueueIndex = 0;
static int            g_keyboardPhase = 0;
static int            g_keyboardPhaseTicks = 0;
static DWORD          g_keyboardSequence = 1;
static ULONGLONG      g_keyboardLastAsyncAdvance = 0;

enum { D3DADAPTER_DEFAULT_LOCAL = 0 };
enum { D3DFMT_X8R8G8B8_LOCAL = 22 };

typedef struct D3DDISPLAYMODE_LOCAL {
    UINT Width;
    UINT Height;
    UINT RefreshRate;
    D3DFORMAT Format;
} D3DDISPLAYMODE_LOCAL;

// ── inline detour / 封包擷取工具 ───────────────────────────────────────────────

enum
{
    kInlineDetourMinPatch = 5,
    kInlineDetourMaxCopy = 32,
    kInlineDetourTrampolineCapacity = 128
};

typedef struct X86DecodedInstruction {
    SIZE_T offset;
    SIZE_T length;
    SIZE_T relOffset;
    SIZE_T relSize;
    BYTE opcode;
    BYTE opcode2;
    bool hasRel;
} X86DecodedInstruction;

static bool IsReadableMemoryRange(const BYTE* ptr, SIZE_T len)
{
    if (!ptr || len == 0)
        return false;

    uintptr_t cur = (uintptr_t)ptr;
    uintptr_t end = cur + len;
    if (end < cur)
        return false;

    while (cur < end)
    {
        MEMORY_BASIC_INFORMATION mbi = {};
        if (VirtualQuery((const void*)cur, &mbi, sizeof(mbi)) != sizeof(mbi))
            return false;

        if (mbi.State != MEM_COMMIT)
            return false;

        const DWORD protect = mbi.Protect & 0xFF;
        if ((mbi.Protect & PAGE_GUARD) || protect == PAGE_NOACCESS)
            return false;

        uintptr_t regionEnd = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
        if (regionEnd <= cur)
            return false;
        cur = regionEnd;
    }

    return true;
}

static void SetDecodeReason(char* reason, SIZE_T reasonCount, const char* text)
{
    if (reason && reasonCount > 0)
        _snprintf_s(reason, reasonCount, _TRUNCATE, "%s", text ? text : "unknown");
}

static bool NeedInstructionBytes(SIZE_T index, SIZE_T need, SIZE_T maxLen)
{
    return need <= maxLen && index <= maxLen - need && index + need <= 15;
}

static bool AddModRmLength(const BYTE* code, SIZE_T maxLen, SIZE_T* index, bool address16, BYTE* modrmOut)
{
    if (!NeedInstructionBytes(*index, 1, maxLen))
        return false;

    BYTE modrm = code[*index];
    if (modrmOut) *modrmOut = modrm;
    ++(*index);

    const BYTE mod = (BYTE)(modrm >> 6);
    const BYTE rm = (BYTE)(modrm & 7);

    if (address16)
    {
        if (mod == 0 && rm == 6)
        {
            if (!NeedInstructionBytes(*index, 2, maxLen)) return false;
            *index += 2;
        }
        else if (mod == 1)
        {
            if (!NeedInstructionBytes(*index, 1, maxLen)) return false;
            *index += 1;
        }
        else if (mod == 2)
        {
            if (!NeedInstructionBytes(*index, 2, maxLen)) return false;
            *index += 2;
        }
        return true;
    }

    if (mod != 3 && rm == 4)
    {
        if (!NeedInstructionBytes(*index, 1, maxLen))
            return false;
        BYTE sib = code[*index];
        ++(*index);
        const BYTE base = (BYTE)(sib & 7);
        if (mod == 0 && base == 5)
        {
            if (!NeedInstructionBytes(*index, 4, maxLen)) return false;
            *index += 4;
        }
    }
    else if (mod == 0 && rm == 5)
    {
        if (!NeedInstructionBytes(*index, 4, maxLen)) return false;
        *index += 4;
    }

    if (mod == 1)
    {
        if (!NeedInstructionBytes(*index, 1, maxLen)) return false;
        *index += 1;
    }
    else if (mod == 2)
    {
        if (!NeedInstructionBytes(*index, 4, maxLen)) return false;
        *index += 4;
    }

    return true;
}

static bool IsOneByteModRmOpcode(BYTE op)
{
    if ((op >= 0x00 && op <= 0x03) || (op >= 0x08 && op <= 0x0B) ||
        (op >= 0x10 && op <= 0x13) || (op >= 0x18 && op <= 0x1B) ||
        (op >= 0x20 && op <= 0x23) || (op >= 0x28 && op <= 0x2B) ||
        (op >= 0x30 && op <= 0x33) || (op >= 0x38 && op <= 0x3B))
        return true;

    if ((op >= 0x80 && op <= 0x8F) ||
        (op >= 0xD0 && op <= 0xD3) ||
        (op >= 0xD8 && op <= 0xDF))
        return true;

    switch (op)
    {
        case 0x62: case 0x63: case 0x69: case 0x6B:
        case 0xC0: case 0xC1: case 0xC4: case 0xC5:
        case 0xC6: case 0xC7: case 0xF6: case 0xF7:
        case 0xFE: case 0xFF:
            return true;
    }
    return false;
}

static bool IsTwoByteModRmOpcode(BYTE op2)
{
    if ((op2 >= 0x10 && op2 <= 0x1F) ||
        (op2 >= 0x20 && op2 <= 0x2F) ||
        (op2 >= 0x40 && op2 <= 0x4F) ||
        (op2 >= 0x90 && op2 <= 0x9F) ||
        (op2 >= 0xB0 && op2 <= 0xBF) ||
        (op2 >= 0xC0 && op2 <= 0xCF) ||
        (op2 >= 0xD0 && op2 <= 0xFF))
        return true;

    switch (op2)
    {
        case 0x00: case 0x01: case 0x02: case 0x03:
        case 0x13: case 0x18: case 0x1F:
        case 0x38: case 0x3A:
        case 0xA3: case 0xA4: case 0xA5: case 0xAB:
        case 0xAC: case 0xAD: case 0xAE: case 0xAF:
            return true;
    }
    return false;
}

static bool IsTwoByteNoModRmOpcode(BYTE op2)
{
    switch (op2)
    {
        case 0x05: case 0x06: case 0x07: case 0x08: case 0x09: case 0x0B:
        case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: case 0x35:
        case 0x77: case 0xA0: case 0xA1: case 0xA2: case 0xA8: case 0xA9:
            return true;
    }
    return false;
}

static bool DecodeX86Instruction(const BYTE* code, SIZE_T maxLen, X86DecodedInstruction* out, char* reason, SIZE_T reasonCount)
{
    if (!code || !out || maxLen == 0)
    {
        SetDecodeReason(reason, reasonCount, "invalid input");
        return false;
    }

    memset(out, 0, sizeof(*out));

    SIZE_T i = 0;
    bool operand16 = false;
    bool address16 = false;

    for (;;)
    {
        if (!NeedInstructionBytes(i, 1, maxLen))
        {
            SetDecodeReason(reason, reasonCount, "truncated prefix/opcode");
            return false;
        }

        BYTE p = code[i];
        if (p == 0x66) { operand16 = true; ++i; continue; }
        if (p == 0x67) { address16 = true; ++i; continue; }
        if (p == 0xF0 || p == 0xF2 || p == 0xF3 ||
            p == 0x2E || p == 0x36 || p == 0x3E || p == 0x26 || p == 0x64 || p == 0x65)
        {
            ++i;
            continue;
        }
        break;
    }

    if (!NeedInstructionBytes(i, 1, maxLen))
    {
        SetDecodeReason(reason, reasonCount, "missing opcode");
        return false;
    }

    BYTE op = code[i++];
    out->opcode = op;
    const SIZE_T operandBytes = operand16 ? 2 : 4;
    const SIZE_T addressBytes = address16 ? 2 : 4;
    BYTE modrm = 0;
    bool hasModRm = false;
    SIZE_T immBytes = 0;

    if (op == 0x0F)
    {
        if (!NeedInstructionBytes(i, 1, maxLen))
        {
            SetDecodeReason(reason, reasonCount, "missing 0F opcode");
            return false;
        }

        BYTE op2 = code[i++];
        out->opcode2 = op2;

        if (op2 >= 0x80 && op2 <= 0x8F)
        {
            if (!NeedInstructionBytes(i, 4, maxLen))
            {
                SetDecodeReason(reason, reasonCount, "truncated rel32 jcc");
                return false;
            }
            out->hasRel = true;
            out->relOffset = i;
            out->relSize = 4;
            i += 4;
        }
        else if (IsTwoByteNoModRmOpcode(op2))
        {
            // no extra bytes
        }
        else if (IsTwoByteModRmOpcode(op2))
        {
            hasModRm = true;
            if (!AddModRmLength(code, maxLen, &i, address16, &modrm))
            {
                SetDecodeReason(reason, reasonCount, "truncated 0F ModRM");
                return false;
            }
            if (op2 == 0x3A || op2 == 0xA4 || op2 == 0xAC || op2 == 0xBA)
                immBytes = 1;
        }
        else
        {
            SetDecodeReason(reason, reasonCount, "unsupported 0F opcode");
            return false;
        }
    }
    else
    {
        if (op >= 0x70 && op <= 0x7F)
        {
            if (!NeedInstructionBytes(i, 1, maxLen))
            {
                SetDecodeReason(reason, reasonCount, "truncated rel8 jcc");
                return false;
            }
            out->hasRel = true;
            out->relOffset = i;
            out->relSize = 1;
            i += 1;
        }
        else if (op == 0xE8 || op == 0xE9)
        {
            if (!NeedInstructionBytes(i, 4, maxLen))
            {
                SetDecodeReason(reason, reasonCount, "truncated rel32 branch");
                return false;
            }
            out->hasRel = true;
            out->relOffset = i;
            out->relSize = 4;
            i += 4;
        }
        else if (op == 0xEB || (op >= 0xE0 && op <= 0xE3))
        {
            if (!NeedInstructionBytes(i, 1, maxLen))
            {
                SetDecodeReason(reason, reasonCount, "truncated rel8 branch");
                return false;
            }
            out->hasRel = true;
            out->relOffset = i;
            out->relSize = 1;
            i += 1;
        }
        else if (IsOneByteModRmOpcode(op))
        {
            hasModRm = true;
            if (!AddModRmLength(code, maxLen, &i, address16, &modrm))
            {
                SetDecodeReason(reason, reasonCount, "truncated ModRM");
                return false;
            }

            if (op == 0x69 || op == 0x81 || op == 0xC7)
                immBytes = operandBytes;
            else if (op == 0x6B || op == 0x80 || op == 0x82 || op == 0x83 ||
                     op == 0xC0 || op == 0xC1 || op == 0xC6)
                immBytes = 1;
            else if (op == 0xF6)
            {
                BYTE reg = (BYTE)((modrm >> 3) & 7);
                if (reg == 0 || reg == 1)
                    immBytes = 1;
            }
            else if (op == 0xF7)
            {
                BYTE reg = (BYTE)((modrm >> 3) & 7);
                if (reg == 0 || reg == 1)
                    immBytes = operandBytes;
            }
        }
        else if ((op >= 0xB0 && op <= 0xB7))
        {
            immBytes = 1;
        }
        else if ((op >= 0xB8 && op <= 0xBF))
        {
            immBytes = operandBytes;
        }
        else
        {
            switch (op)
            {
                case 0x04: case 0x0C: case 0x14: case 0x1C:
                case 0x24: case 0x2C: case 0x34: case 0x3C:
                case 0x6A: case 0xA8: case 0xCD:
                case 0xD4: case 0xD5: case 0xE4: case 0xE5:
                case 0xE6: case 0xE7:
                    immBytes = 1;
                    break;

                case 0x05: case 0x0D: case 0x15: case 0x1D:
                case 0x25: case 0x2D: case 0x35: case 0x3D:
                case 0x68: case 0xA9:
                    immBytes = operandBytes;
                    break;

                case 0xA0: case 0xA1: case 0xA2: case 0xA3:
                    immBytes = addressBytes;
                    break;

                case 0xC2: case 0xCA:
                    immBytes = 2;
                    break;

                case 0xC8:
                    immBytes = 3;
                    break;

                case 0x9A: case 0xEA:
                    immBytes = operandBytes + 2;
                    break;

                case 0x06: case 0x07: case 0x0E: case 0x16: case 0x17:
                case 0x1E: case 0x1F: case 0x27: case 0x2F: case 0x37:
                case 0x3F: case 0x40: case 0x41: case 0x42: case 0x43:
                case 0x44: case 0x45: case 0x46: case 0x47: case 0x48:
                case 0x49: case 0x4A: case 0x4B: case 0x4C: case 0x4D:
                case 0x4E: case 0x4F: case 0x50: case 0x51: case 0x52:
                case 0x53: case 0x54: case 0x55: case 0x56: case 0x57:
                case 0x58: case 0x59: case 0x5A: case 0x5B: case 0x5C:
                case 0x5D: case 0x5E: case 0x5F: case 0x60: case 0x61:
                case 0x6C: case 0x6D: case 0x6E: case 0x6F: case 0x90:
                case 0x91: case 0x92: case 0x93: case 0x94: case 0x95:
                case 0x96: case 0x97: case 0x98: case 0x99: case 0x9B:
                case 0x9C: case 0x9D: case 0x9E: case 0x9F: case 0xA4:
                case 0xA5: case 0xA6: case 0xA7: case 0xAA: case 0xAB:
                case 0xAC: case 0xAD: case 0xAE: case 0xAF: case 0xC3:
                case 0xC9: case 0xCB: case 0xCC: case 0xCE: case 0xCF:
                case 0xD6: case 0xD7: case 0xEC: case 0xED: case 0xEE:
                case 0xEF: case 0xF4: case 0xF5: case 0xF8: case 0xF9:
                case 0xFA: case 0xFB: case 0xFC: case 0xFD:
                    break;

                default:
                    SetDecodeReason(reason, reasonCount, "unsupported opcode");
                    return false;
            }
        }
    }

    (void)hasModRm;
    if (immBytes > 0)
    {
        if (!NeedInstructionBytes(i, immBytes, maxLen))
        {
            SetDecodeReason(reason, reasonCount, "truncated immediate");
            return false;
        }
        i += immBytes;
    }

    if (i == 0 || i > 15)
    {
        SetDecodeReason(reason, reasonCount, "invalid instruction length");
        return false;
    }

    out->length = i;
    return true;
}

static void FormatBytes(const BYTE* bytes, SIZE_T count, char* out, SIZE_T outCount)
{
    static const char kHex[] = "0123456789abcdef";
    if (!out || outCount == 0)
        return;
    out[0] = '\0';

    SIZE_T pos = 0;
    for (SIZE_T i = 0; bytes && i < count && pos + 3 < outCount; ++i)
    {
        if (i > 0)
            out[pos++] = ' ';
        BYTE b = bytes[i];
        out[pos++] = kHex[b >> 4];
        out[pos++] = kHex[b & 0x0F];
    }
    out[pos] = '\0';
}

static bool WriteRelative32(BYTE* operand, const BYTE* nextInstruction, const BYTE* destination)
{
    uint32_t disp =
        (uint32_t)(uintptr_t)destination - (uint32_t)(uintptr_t)nextInstruction;
    *(int32_t*)operand = (int32_t)disp;
    return true;
}

static const BYTE* GetRelativeDestination(const BYTE* instruction, const X86DecodedInstruction* decoded)
{
    if (!instruction || !decoded || !decoded->hasRel)
        return nullptr;

    const BYTE* next = instruction + decoded->length;
    if (decoded->relSize == 1)
    {
        int8_t rel = *(const int8_t*)(instruction + decoded->relOffset);
        return (const BYTE*)((uint32_t)(uintptr_t)next + (int32_t)rel);
    }

    if (decoded->relSize == 4)
    {
        int32_t rel = *(const int32_t*)(instruction + decoded->relOffset);
        return (const BYTE*)((uint32_t)(uintptr_t)next + rel);
    }

    return nullptr;
}

static bool IsHotpatchPaddingByte(BYTE b)
{
    return b == 0x90 || b == 0xCC;
}

static bool CanUseHotpatchSlot(const BYTE* target)
{
    if (!target || !IsReadableMemoryRange(target - 5, 7))
        return false;

    if (target[0] != 0x8B || target[1] != 0xFF)
        return false;

    for (int i = -5; i < 0; ++i)
    {
        if (!IsHotpatchPaddingByte(target[i]))
            return false;
    }

    return true;
}

static bool InstallHotpatchDetour(InlineDetour* hook)
{
    BYTE* target = (BYTE*)hook->target;
    if (!CanUseHotpatchSlot(target))
        return false;

    BYTE* trampoline = (BYTE*)VirtualAlloc(
        nullptr, kInlineDetourTrampolineCapacity, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!trampoline)
    {
        WriteLog("[Windower] %s hotpatch trampoline alloc failed err=%lu",
            hook->name, (unsigned long)GetLastError());
        return false;
    }

    trampoline[0] = target[0];
    trampoline[1] = target[1];
    trampoline[2] = 0xE9;
    if (!WriteRelative32(trampoline + 3, trampoline + 7, target + 2))
    {
        WriteLog("[Windower] %s hotpatch trampoline rel32 out of range", hook->name);
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }

    BYTE* patchStart = target - 5;
    memcpy(hook->savedBefore, patchStart, sizeof(hook->savedBefore));
    memcpy(hook->saved, target, 2);

    DWORD oldProt = 0;
    if (!VirtualProtect(patchStart, 7, PAGE_EXECUTE_READWRITE, &oldProt))
    {
        WriteLog("[Windower] %s hotpatch VirtualProtect failed err=%lu",
            hook->name, (unsigned long)GetLastError());
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }

    patchStart[0] = 0xE9;
    if (!WriteRelative32(patchStart + 1, target, (const BYTE*)hook->detour))
    {
        VirtualProtect(patchStart, 7, oldProt, &oldProt);
        VirtualFree(trampoline, 0, MEM_RELEASE);
        WriteLog("[Windower] %s hotpatch detour rel32 out of range", hook->name);
        return false;
    }
    target[0] = 0xEB;
    target[1] = 0xF9;

    VirtualProtect(patchStart, 7, oldProt, &oldProt);
    FlushInstructionCache(GetCurrentProcess(), patchStart, 7);
    FlushInstructionCache(GetCurrentProcess(), trampoline, 7);

    hook->trampoline = trampoline;
    hook->installed = true;
    hook->copiedLen = 2;
    hook->trampolineLen = 7;
    hook->patchLen = 7;
    hook->hotpatchMode = true;
    WriteLog("[Windower] %s detour installed target=%p detour=%p copied %lu bytes mode=hotpatch",
        hook->name, hook->target, hook->detour, (unsigned long)hook->copiedLen);
    return true;
}

static bool RelocateInstruction(
    const BYTE* target,
    SIZE_T copiedLen,
    const X86DecodedInstruction* decoded,
    BYTE* trampoline,
    SIZE_T trampolineOffset,
    SIZE_T trampolineCapacity,
    SIZE_T* outLen,
    const char* hookName)
{
    const BYTE* src = target + decoded->offset;
    BYTE* dst = trampoline + trampolineOffset;
    *outLen = 0;

    if (!decoded->hasRel)
    {
        if (trampolineOffset + decoded->length > trampolineCapacity)
            return false;
        memcpy(dst, src, decoded->length);
        *outLen = decoded->length;
        return true;
    }

    const BYTE* absDest = GetRelativeDestination(src, decoded);
    if (!absDest)
        return false;

    if (absDest >= target && absDest < target + copiedLen)
    {
        WriteLog("[Windower] %s detour unsupported internal relative branch at +%lu",
            hookName, (unsigned long)decoded->offset);
        return false;
    }

    if (decoded->relSize == 4)
    {
        if (trampolineOffset + decoded->length > trampolineCapacity)
            return false;
        memcpy(dst, src, decoded->length);
        if (!WriteRelative32(dst + decoded->relOffset, dst + decoded->length, absDest))
            return false;
        *outLen = decoded->length;
        return true;
    }

    if (decoded->relSize != 1)
        return false;

    if (decoded->opcode == 0xEB)
    {
        if (trampolineOffset + 5 > trampolineCapacity)
            return false;
        dst[0] = 0xE9;
        if (!WriteRelative32(dst + 1, dst + 5, absDest))
            return false;
        *outLen = 5;
        return true;
    }

    if (decoded->opcode >= 0x70 && decoded->opcode <= 0x7F)
    {
        if (trampolineOffset + 6 > trampolineCapacity)
            return false;
        dst[0] = 0x0F;
        dst[1] = (BYTE)(0x80 + (decoded->opcode - 0x70));
        if (!WriteRelative32(dst + 2, dst + 6, absDest))
            return false;
        *outLen = 6;
        return true;
    }

    int32_t newRel =
        (int32_t)((uint32_t)(uintptr_t)absDest - (uint32_t)(uintptr_t)(dst + decoded->length));
    if (newRel < -128 || newRel > 127)
    {
        WriteLog("[Windower] %s detour unsupported relocated rel8 opcode=0x%02X at +%lu",
            hookName, (unsigned)decoded->opcode, (unsigned long)decoded->offset);
        return false;
    }

    if (trampolineOffset + decoded->length > trampolineCapacity)
        return false;
    memcpy(dst, src, decoded->length);
    *(int8_t*)(dst + decoded->relOffset) = (int8_t)newRel;
    *outLen = decoded->length;
    return true;
}

static bool InstallInlineLdeDetour(InlineDetour* hook)
{
    BYTE* target = (BYTE*)hook->target;
    if (!IsReadableMemoryRange(target, kInlineDetourMaxCopy))
    {
        WriteLog("[Windower] %s detour target bytes unreadable", hook->name);
        return false;
    }

    X86DecodedInstruction insts[16] = {};
    SIZE_T instCount = 0;
    SIZE_T copied = 0;
    char reason[128] = {};

    while (copied < kInlineDetourMinPatch)
    {
        if (instCount >= _countof(insts))
        {
            WriteLog("[Windower] %s detour decode failed: too many instructions", hook->name);
            return false;
        }

        X86DecodedInstruction decoded = {};
        decoded.offset = copied;
        if (!DecodeX86Instruction(target + copied, kInlineDetourMaxCopy - copied, &decoded, reason, _countof(reason)))
        {
            char bytes[80] = {};
            FormatBytes(target + copied, 12, bytes, _countof(bytes));
            WriteLog("[Windower] %s detour decode failed at +%lu bytes=%s reason=%s",
                hook->name, (unsigned long)copied, bytes, reason);
            return false;
        }

        if (decoded.length == 0 || copied + decoded.length > kInlineDetourMaxCopy)
        {
            WriteLog("[Windower] %s detour decode produced invalid length", hook->name);
            return false;
        }

        insts[instCount++] = decoded;
        copied += decoded.length;
    }

    BYTE* trampoline = (BYTE*)VirtualAlloc(
        nullptr, kInlineDetourTrampolineCapacity, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!trampoline)
    {
        WriteLog("[Windower] %s detour trampoline alloc failed err=%lu",
            hook->name, (unsigned long)GetLastError());
        return false;
    }

    SIZE_T trampOff = 0;
    for (SIZE_T i = 0; i < instCount; ++i)
    {
        SIZE_T outLen = 0;
        if (!RelocateInstruction(target, copied, &insts[i], trampoline, trampOff, kInlineDetourTrampolineCapacity - 5, &outLen, hook->name))
        {
            WriteLog("[Windower] %s detour relocation failed at instruction +%lu",
                hook->name, (unsigned long)insts[i].offset);
            VirtualFree(trampoline, 0, MEM_RELEASE);
            return false;
        }
        trampOff += outLen;
    }

    if (trampOff + 5 > kInlineDetourTrampolineCapacity)
    {
        WriteLog("[Windower] %s detour trampoline overflow", hook->name);
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }

    trampoline[trampOff] = 0xE9;
    if (!WriteRelative32(trampoline + trampOff + 1, trampoline + trampOff + 5, target + copied))
    {
        WriteLog("[Windower] %s detour trampoline return rel32 out of range", hook->name);
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }
    trampOff += 5;

    memcpy(hook->saved, target, copied);

    DWORD oldProt = 0;
    if (!VirtualProtect(target, copied, PAGE_EXECUTE_READWRITE, &oldProt))
    {
        WriteLog("[Windower] %s detour VirtualProtect failed err=%lu",
            hook->name, (unsigned long)GetLastError());
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }

    target[0] = 0xE9;
    if (!WriteRelative32(target + 1, target + 5, (const BYTE*)hook->detour))
    {
        VirtualProtect(target, copied, oldProt, &oldProt);
        VirtualFree(trampoline, 0, MEM_RELEASE);
        WriteLog("[Windower] %s detour rel32 out of range", hook->name);
        return false;
    }
    for (SIZE_T i = kInlineDetourMinPatch; i < copied; ++i)
        target[i] = 0x90;

    VirtualProtect(target, copied, oldProt, &oldProt);
    FlushInstructionCache(GetCurrentProcess(), target, copied);
    FlushInstructionCache(GetCurrentProcess(), trampoline, trampOff);

    hook->trampoline = trampoline;
    hook->installed = true;
    hook->copiedLen = copied;
    hook->trampolineLen = trampOff;
    hook->patchLen = copied;
    hook->hotpatchMode = false;
    WriteLog("[Windower] %s detour installed target=%p detour=%p copied %lu bytes mode=inline-lde",
        hook->name, hook->target, hook->detour, (unsigned long)hook->copiedLen);
    return true;
}

static bool InstallInlineDetour(InlineDetour* hook)
{
    if (!hook || hook->installed || !hook->target || !hook->detour)
        return hook && hook->installed;

    if (InstallHotpatchDetour(hook))
        return true;

    return InstallInlineLdeDetour(hook);
}

static bool IsEnvFlagOne(const char* name)
{
    char value[16] = {};
    DWORD n = GetEnvironmentVariableA(name, value, (DWORD)_countof(value));
    return (n > 0 && n < _countof(value) && strcmp(value, "1") == 0);
}

static bool IsCaptureEnabled()
{
    return IsEnvFlagOne("MAPLEFORGE_WINDOWER_CAPTURE");
}

static bool IsHookEnabledByList(const char* hookName)
{
    char value[256] = {};
    DWORD n = GetEnvironmentVariableA("MAPLEFORGE_WINDOWER_HOOKS", value, (DWORD)_countof(value));
    if (n == 0)
        return true;
    if (n >= _countof(value))
        return false;

    const size_t hookNameLen = strlen(hookName);
    const char* p = value;
    while (*p)
    {
        while (*p == ' ' || *p == '\t' || *p == ',')
            ++p;

        const char* start = p;
        while (*p && *p != ',')
            ++p;

        const char* end = p;
        while (end > start && (end[-1] == ' ' || end[-1] == '\t' || end[-1] == '\r' || end[-1] == '\n'))
            --end;

        const size_t tokenLen = (size_t)(end - start);
        if (tokenLen == hookNameLen && _strnicmp(start, hookName, tokenLen) == 0)
            return true;

        if (*p == ',')
            ++p;
    }

    return false;
}

static void* InstallInlineDetourIfEnabled(InlineDetour* hook, void* target)
{
    if (!hook)
        return nullptr;

    hook->target = target;
    if (!IsHookEnabledByList(hook->name))
    {
        WriteLog("[Windower] hook %s skipped(hooks-env)", hook->name);
        return nullptr;
    }

    if (!hook->target)
    {
        WriteLog("[Windower] hook %s skipped(missing)", hook->name);
        return nullptr;
    }

    if (InstallInlineDetour(hook))
    {
        WriteLog("[Windower] hook %s installed copied %lu bytes mode=%s",
            hook->name, (unsigned long)hook->copiedLen, hook->hotpatchMode ? "hotpatch" : "inline-lde");
        return hook->trampoline;
    }

    WriteLog("[Windower] hook %s skipped(detour_failed)", hook->name);
    return hook->target;
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

    g_origSend = (Send_t)InstallInlineDetourIfEnabled(
        &g_sendDetour, (void*)GetProcAddress(hWs2, "send"));
    g_origRecv = (Recv_t)InstallInlineDetourIfEnabled(
        &g_recvDetour, (void*)GetProcAddress(hWs2, "recv"));
    g_origWSASend = (WSASend_t)InstallInlineDetourIfEnabled(
        &g_wsaSendDetour, (void*)GetProcAddress(hWs2, "WSASend"));
    g_origWSARecv = (WSARecv_t)InstallInlineDetourIfEnabled(
        &g_wsaRecvDetour, (void*)GetProcAddress(hWs2, "WSARecv"));
    g_origWSAGetOverlappedResult = (WSAGetOverlappedResult_t)InstallInlineDetourIfEnabled(
        &g_wsaGetOverlappedResultDetour, (void*)GetProcAddress(hWs2, "WSAGetOverlappedResult"));

    HMODULE hKernel32 = GetModuleHandleA("kernel32.dll");
    g_origGetQueuedCompletionStatus = (GetQueuedCompletionStatus_t)InstallInlineDetourIfEnabled(
        &g_getQueuedCompletionStatusDetour,
        hKernel32 ? (void*)GetProcAddress(hKernel32, "GetQueuedCompletionStatus") : nullptr);

    WriteLog(
        "[Windower] hooks status send=%d recv=%d WSASend=%d WSARecv=%d WSAGetOverlappedResult=%d GetQueuedCompletionStatus=%d",
        g_sendDetour.installed ? 1 : 0,
        g_recvDetour.installed ? 1 : 0,
        g_wsaSendDetour.installed ? 1 : 0,
        g_wsaRecvDetour.installed ? 1 : 0,
        g_wsaGetOverlappedResultDetour.installed ? 1 : 0,
        g_getQueuedCompletionStatusDetour.installed ? 1 : 0);
    WriteLog("[Windower] winsock hooks installed");
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

    // 只在「真有資料」(ret>0) 時才記錄。客戶端用非阻塞 recv 高頻輪詢，
    // ret<=0(WSAEWOULDBLOCK) 是無資料空輪詢；若每次都 fprintf+fflush(在全域鎖內)
    // 會造成 I/O 風暴拖垮客戶端網路 timing → 收 getHello 失敗而放棄連線(已 live A/B 確認)。
    if (ret > 0)
        WriteChunkSingleBuffer(s, "s2c", "recv", ret, (const unsigned char*)buf, (DWORD)ret, false);
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

// ── Keyboard injection / DirectInput instrumentation ─────────────────────────

enum
{
    DIK_ESCAPE_LOCAL = 0x01,
    DIK_1_LOCAL = 0x02,
    DIK_2_LOCAL = 0x03,
    DIK_3_LOCAL = 0x04,
    DIK_4_LOCAL = 0x05,
    DIK_5_LOCAL = 0x06,
    DIK_6_LOCAL = 0x07,
    DIK_7_LOCAL = 0x08,
    DIK_8_LOCAL = 0x09,
    DIK_9_LOCAL = 0x0A,
    DIK_0_LOCAL = 0x0B,
    DIK_MINUS_LOCAL = 0x0C,
    DIK_EQUALS_LOCAL = 0x0D,
    DIK_BACK_LOCAL = 0x0E,
    DIK_TAB_LOCAL = 0x0F,
    DIK_Q_LOCAL = 0x10,
    DIK_W_LOCAL = 0x11,
    DIK_E_LOCAL = 0x12,
    DIK_R_LOCAL = 0x13,
    DIK_T_LOCAL = 0x14,
    DIK_Y_LOCAL = 0x15,
    DIK_U_LOCAL = 0x16,
    DIK_I_LOCAL = 0x17,
    DIK_O_LOCAL = 0x18,
    DIK_P_LOCAL = 0x19,
    DIK_LBRACKET_LOCAL = 0x1A,
    DIK_RBRACKET_LOCAL = 0x1B,
    DIK_RETURN_LOCAL = 0x1C,
    DIK_A_LOCAL = 0x1E,
    DIK_S_LOCAL = 0x1F,
    DIK_D_LOCAL = 0x20,
    DIK_F_LOCAL = 0x21,
    DIK_G_LOCAL = 0x22,
    DIK_H_LOCAL = 0x23,
    DIK_J_LOCAL = 0x24,
    DIK_K_LOCAL = 0x25,
    DIK_L_LOCAL = 0x26,
    DIK_SEMICOLON_LOCAL = 0x27,
    DIK_APOSTROPHE_LOCAL = 0x28,
    DIK_GRAVE_LOCAL = 0x29,
    DIK_LSHIFT_LOCAL = 0x2A,
    DIK_BACKSLASH_LOCAL = 0x2B,
    DIK_Z_LOCAL = 0x2C,
    DIK_X_LOCAL = 0x2D,
    DIK_C_LOCAL = 0x2E,
    DIK_V_LOCAL = 0x2F,
    DIK_B_LOCAL = 0x30,
    DIK_N_LOCAL = 0x31,
    DIK_M_LOCAL = 0x32,
    DIK_COMMA_LOCAL = 0x33,
    DIK_PERIOD_LOCAL = 0x34,
    DIK_SLASH_LOCAL = 0x35,
    DIK_SPACE_LOCAL = 0x39,
    KBD_DOWN_POLLS = 4,
    KBD_UP_POLLS = 2
};

static const GUID GUID_SysKeyboard_Local =
    { 0x6F1D2B61, 0xD5A0, 0x11CF, { 0xBF, 0xC7, 0x44, 0x45, 0x53, 0x54, 0x00, 0x00 } };

static bool IsKeyboardInjectEnabled()
{
    return IsEnvFlagOne("MAPLEFORGE_WINDOWER_KBD_INJECT");
}

static bool GuidEquals(REFGUID a, REFGUID b)
{
    return memcmp(&a, &b, sizeof(GUID)) == 0;
}

static void* RvaToPtr(BYTE* base, DWORD rva)
{
    return rva ? (void*)(base + rva) : nullptr;
}

static bool ExeImports(const char* moduleName, const char* functionName)
{
    HMODULE hExe = GetModuleHandleA(nullptr);
    if (!hExe || !moduleName)
        return false;

    BYTE* base = (BYTE*)hExe;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        return false;

    IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE)
        return false;

    IMAGE_DATA_DIRECTORY dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress)
        return false;

    IMAGE_IMPORT_DESCRIPTOR* desc = (IMAGE_IMPORT_DESCRIPTOR*)RvaToPtr(base, dir.VirtualAddress);
    for (; desc && desc->Name; ++desc)
    {
        const char* dllName = (const char*)RvaToPtr(base, desc->Name);
        if (!dllName || _stricmp(dllName, moduleName) != 0)
            continue;

        if (!functionName)
            return true;

        DWORD thunkRva = desc->OriginalFirstThunk ? desc->OriginalFirstThunk : desc->FirstThunk;
        IMAGE_THUNK_DATA* thunk = (IMAGE_THUNK_DATA*)RvaToPtr(base, thunkRva);
        for (; thunk && thunk->u1.AddressOfData; ++thunk)
        {
            if (IMAGE_SNAP_BY_ORDINAL(thunk->u1.Ordinal))
                continue;

            IMAGE_IMPORT_BY_NAME* byName = (IMAGE_IMPORT_BY_NAME*)RvaToPtr(base, (DWORD)thunk->u1.AddressOfData);
            if (byName && _stricmp((const char*)byName->Name, functionName) == 0)
                return true;
        }
    }

    return false;
}

static void LogKeyboardApiDetection()
{
    if (g_keyboardDetectionLogged)
        return;
    g_keyboardDetectionLogged = true;

    const bool dinput8Loaded = (GetModuleHandleA("dinput8.dll") != nullptr);
    const bool dinputLoaded = (GetModuleHandleA("dinput.dll") != nullptr);
    const bool user32Loaded = (GetModuleHandleA("user32.dll") != nullptr);

    const bool importsDinput8 = ExeImports("dinput8.dll", nullptr);
    const bool importsDinput8Create = ExeImports("dinput8.dll", "DirectInput8Create");
    const bool importsDinput = ExeImports("dinput.dll", nullptr);
    const bool importsDirectInputCreateA = ExeImports("dinput.dll", "DirectInputCreateA");
    const bool importsDirectInputCreateW = ExeImports("dinput.dll", "DirectInputCreateW");
    const bool importsGetAsync = ExeImports("user32.dll", "GetAsyncKeyState");
    const bool importsGetKeyboardState = ExeImports("user32.dll", "GetKeyboardState");
    const bool importsRegisterRaw = ExeImports("user32.dll", "RegisterRawInputDevices");
    const bool importsGetRawInputData = ExeImports("user32.dll", "GetRawInputData");

    WriteLog(
        "[Windower] keyboard API detection modules dinput8=%d dinput=%d user32=%d imports dinput8=%d DirectInput8Create=%d dinput=%d DirectInputCreateA=%d DirectInputCreateW=%d GetAsyncKeyState=%d GetKeyboardState=%d RegisterRawInputDevices=%d GetRawInputData=%d",
        dinput8Loaded ? 1 : 0,
        dinputLoaded ? 1 : 0,
        user32Loaded ? 1 : 0,
        importsDinput8 ? 1 : 0,
        importsDinput8Create ? 1 : 0,
        importsDinput ? 1 : 0,
        importsDirectInputCreateA ? 1 : 0,
        importsDirectInputCreateW ? 1 : 0,
        importsGetAsync ? 1 : 0,
        importsGetKeyboardState ? 1 : 0,
        importsRegisterRaw ? 1 : 0,
        importsGetRawInputData ? 1 : 0);
}

static bool ResolveKeyboardFilePath()
{
    if (g_keyboardFileReady)
        return true;

    DWORD n = GetEnvironmentVariableA("MAPLEFORGE_WINDOWER_KBD_FILE", g_keyboardFileA, (DWORD)_countof(g_keyboardFileA));
    if (n > 0 && n < _countof(g_keyboardFileA))
    {
        g_keyboardFileReady = true;
        WriteLog("[Windower] keyboard injection file=%s", g_keyboardFileA);
        return true;
    }

    if (!g_keyboardFileMissingLogged)
    {
        g_keyboardFileMissingLogged = true;
        WriteLog("[Windower] keyboard injection enabled but MAPLEFORGE_WINDOWER_KBD_FILE is not set");
    }
    return false;
}

static bool MapCharToKey(char ch, KbdInjectKey* out)
{
    if (!out)
        return false;

    memset(out, 0, sizeof(*out));

    if (ch >= 'a' && ch <= 'z')
    {
        static const BYTE kLetterDik[26] = {
            DIK_A_LOCAL, DIK_B_LOCAL, DIK_C_LOCAL, DIK_D_LOCAL, DIK_E_LOCAL, DIK_F_LOCAL,
            DIK_G_LOCAL, DIK_H_LOCAL, DIK_I_LOCAL, DIK_J_LOCAL, DIK_K_LOCAL, DIK_L_LOCAL,
            DIK_M_LOCAL, DIK_N_LOCAL, DIK_O_LOCAL, DIK_P_LOCAL, DIK_Q_LOCAL, DIK_R_LOCAL,
            DIK_S_LOCAL, DIK_T_LOCAL, DIK_U_LOCAL, DIK_V_LOCAL, DIK_W_LOCAL, DIK_X_LOCAL,
            DIK_Y_LOCAL, DIK_Z_LOCAL
        };
        out->dik = kLetterDik[ch - 'a'];
        out->vk = (BYTE)('A' + (ch - 'a'));
        return true;
    }

    if (ch >= 'A' && ch <= 'Z')
    {
        static const BYTE kLetterDik[26] = {
            DIK_A_LOCAL, DIK_B_LOCAL, DIK_C_LOCAL, DIK_D_LOCAL, DIK_E_LOCAL, DIK_F_LOCAL,
            DIK_G_LOCAL, DIK_H_LOCAL, DIK_I_LOCAL, DIK_J_LOCAL, DIK_K_LOCAL, DIK_L_LOCAL,
            DIK_M_LOCAL, DIK_N_LOCAL, DIK_O_LOCAL, DIK_P_LOCAL, DIK_Q_LOCAL, DIK_R_LOCAL,
            DIK_S_LOCAL, DIK_T_LOCAL, DIK_U_LOCAL, DIK_V_LOCAL, DIK_W_LOCAL, DIK_X_LOCAL,
            DIK_Y_LOCAL, DIK_Z_LOCAL
        };
        out->dik = kLetterDik[ch - 'A'];
        out->vk = (BYTE)ch;
        out->shift = true;
        return true;
    }

    if (ch >= '1' && ch <= '9')
    {
        out->dik = (BYTE)(DIK_1_LOCAL + (ch - '1'));
        out->vk = (BYTE)ch;
        return true;
    }

    if (ch == '0')
    {
        out->dik = DIK_0_LOCAL;
        out->vk = '0';
        return true;
    }

    switch (ch)
    {
        case '\n': case '\r': out->dik = DIK_RETURN_LOCAL; out->vk = VK_RETURN; return true;
        case '\t': out->dik = DIK_TAB_LOCAL; out->vk = VK_TAB; return true;
        case '\b': out->dik = DIK_BACK_LOCAL; out->vk = VK_BACK; return true;
        case ' ': out->dik = DIK_SPACE_LOCAL; out->vk = VK_SPACE; return true;

        case '-': out->dik = DIK_MINUS_LOCAL; out->vk = VK_OEM_MINUS; return true;
        case '_': out->dik = DIK_MINUS_LOCAL; out->vk = VK_OEM_MINUS; out->shift = true; return true;
        case '=': out->dik = DIK_EQUALS_LOCAL; out->vk = VK_OEM_PLUS; return true;
        case '+': out->dik = DIK_EQUALS_LOCAL; out->vk = VK_OEM_PLUS; out->shift = true; return true;
        case '[': out->dik = DIK_LBRACKET_LOCAL; out->vk = VK_OEM_4; return true;
        case '{': out->dik = DIK_LBRACKET_LOCAL; out->vk = VK_OEM_4; out->shift = true; return true;
        case ']': out->dik = DIK_RBRACKET_LOCAL; out->vk = VK_OEM_6; return true;
        case '}': out->dik = DIK_RBRACKET_LOCAL; out->vk = VK_OEM_6; out->shift = true; return true;
        case ';': out->dik = DIK_SEMICOLON_LOCAL; out->vk = VK_OEM_1; return true;
        case ':': out->dik = DIK_SEMICOLON_LOCAL; out->vk = VK_OEM_1; out->shift = true; return true;
        case '\'': out->dik = DIK_APOSTROPHE_LOCAL; out->vk = VK_OEM_7; return true;
        case '"': out->dik = DIK_APOSTROPHE_LOCAL; out->vk = VK_OEM_7; out->shift = true; return true;
        case '`': out->dik = DIK_GRAVE_LOCAL; out->vk = VK_OEM_3; return true;
        case '~': out->dik = DIK_GRAVE_LOCAL; out->vk = VK_OEM_3; out->shift = true; return true;
        case '\\': out->dik = DIK_BACKSLASH_LOCAL; out->vk = VK_OEM_5; return true;
        case '|': out->dik = DIK_BACKSLASH_LOCAL; out->vk = VK_OEM_5; out->shift = true; return true;
        case ',': out->dik = DIK_COMMA_LOCAL; out->vk = VK_OEM_COMMA; return true;
        case '<': out->dik = DIK_COMMA_LOCAL; out->vk = VK_OEM_COMMA; out->shift = true; return true;
        case '.': out->dik = DIK_PERIOD_LOCAL; out->vk = VK_OEM_PERIOD; return true;
        case '>': out->dik = DIK_PERIOD_LOCAL; out->vk = VK_OEM_PERIOD; out->shift = true; return true;
        case '/': out->dik = DIK_SLASH_LOCAL; out->vk = VK_OEM_2; return true;
        case '?': out->dik = DIK_SLASH_LOCAL; out->vk = VK_OEM_2; out->shift = true; return true;

        case '!': out->dik = DIK_1_LOCAL; out->vk = '1'; out->shift = true; return true;
        case '@': out->dik = DIK_2_LOCAL; out->vk = '2'; out->shift = true; return true;
        case '#': out->dik = DIK_3_LOCAL; out->vk = '3'; out->shift = true; return true;
        case '$': out->dik = DIK_4_LOCAL; out->vk = '4'; out->shift = true; return true;
        case '%': out->dik = DIK_5_LOCAL; out->vk = '5'; out->shift = true; return true;
        case '^': out->dik = DIK_6_LOCAL; out->vk = '6'; out->shift = true; return true;
        case '&': out->dik = DIK_7_LOCAL; out->vk = '7'; out->shift = true; return true;
        case '*': out->dik = DIK_8_LOCAL; out->vk = '8'; out->shift = true; return true;
        case '(': out->dik = DIK_9_LOCAL; out->vk = '9'; out->shift = true; return true;
        case ')': out->dik = DIK_0_LOCAL; out->vk = '0'; out->shift = true; return true;
    }

    return false;
}

static void ClearKeyboardInjectionFileLocked()
{
    if (!g_keyboardFileReady || !g_keyboardFileA[0])
        return;

    HANDLE h = CreateFileA(
        g_keyboardFileA, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h != INVALID_HANDLE_VALUE)
        CloseHandle(h);
}

static void LoadKeyboardInjectionFileLocked()
{
    if (g_keyboardQueueIndex < g_keyboardQueueCount)
        return;

    ULONGLONG now = GetTickCount64();
    if (now - g_keyboardLastFilePoll < 50)
        return;
    g_keyboardLastFilePoll = now;

    if (!ResolveKeyboardFilePath())
        return;

    HANDLE h = CreateFileA(
        g_keyboardFileA, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE)
        return;

    DWORD size = GetFileSize(h, nullptr);
    if (size == INVALID_FILE_SIZE || size == 0)
    {
        CloseHandle(h);
        return;
    }
    if (size > 4095)
        size = 4095;

    char buf[4096] = {};
    DWORD read = 0;
    BOOL ok = ReadFile(h, buf, size, &read, nullptr);
    CloseHandle(h);
    if (!ok || read == 0)
        return;

    g_keyboardQueueCount = 0;
    g_keyboardQueueIndex = 0;
    g_keyboardPhase = 0;
    g_keyboardPhaseTicks = KBD_DOWN_POLLS;

    for (DWORD i = 0; i < read && g_keyboardQueueCount < _countof(g_keyboardQueue); ++i)
    {
        if (buf[i] == '\r' && i + 1 < read && buf[i + 1] == '\n')
            continue;

        KbdInjectKey key = {};
        if (MapCharToKey(buf[i], &key))
            g_keyboardQueue[g_keyboardQueueCount++] = key;
    }

    ClearKeyboardInjectionFileLocked();

    if (g_keyboardQueueCount > 0)
    {
        buf[read] = '\0';
        WriteLog("[Windower] keyboard injection loaded string=\"%s\" keys=%lu",
            buf, (unsigned long)g_keyboardQueueCount);
    }
}

static bool GetCurrentInjectedKeyLocked(KbdInjectKey* keyOut, bool* isDownOut)
{
    LoadKeyboardInjectionFileLocked();

    if (g_keyboardQueueIndex >= g_keyboardQueueCount)
        return false;

    if (keyOut)
        *keyOut = g_keyboardQueue[g_keyboardQueueIndex];
    if (isDownOut)
        *isDownOut = (g_keyboardPhase == 0);
    return true;
}

static void AdvanceKeyboardInjectionLocked()
{
    if (g_keyboardQueueIndex >= g_keyboardQueueCount)
        return;

    --g_keyboardPhaseTicks;
    if (g_keyboardPhaseTicks > 0)
        return;

    if (g_keyboardPhase == 0)
    {
        g_keyboardPhase = 1;
        g_keyboardPhaseTicks = KBD_UP_POLLS;
    }
    else
    {
        ++g_keyboardQueueIndex;
        g_keyboardPhase = 0;
        g_keyboardPhaseTicks = KBD_DOWN_POLLS;
        if (g_keyboardQueueIndex >= g_keyboardQueueCount)
        {
            WriteLog("[Windower] keyboard injection finished keys=%lu", (unsigned long)g_keyboardQueueCount);
            g_keyboardQueueCount = 0;
            g_keyboardQueueIndex = 0;
        }
    }
}

static void OverlayDirectInputKeyboardState(BYTE* state, DWORD cbData)
{
    if (!state || cbData < 256 || !g_keyboardLockReady)
        return;

    EnterCriticalSection(&g_keyboardLock);
    KbdInjectKey key = {};
    bool isDown = false;
    if (GetCurrentInjectedKeyLocked(&key, &isDown))
    {
        if (isDown)
        {
            state[key.dik] |= 0x80;
            if (key.shift)
                state[DIK_LSHIFT_LOCAL] |= 0x80;
        }
        AdvanceKeyboardInjectionLocked();
    }
    LeaveCriticalSection(&g_keyboardLock);
}

static bool CurrentVirtualKeyDownLocked(int vKey)
{
    KbdInjectKey key = {};
    bool isDown = false;
    if (!GetCurrentInjectedKeyLocked(&key, &isDown) || !isDown)
        return false;

    if (vKey == key.vk)
        return true;
    if (key.shift && (vKey == VK_SHIFT || vKey == VK_LSHIFT))
        return true;
    return false;
}

static void MaybeAdvanceKeyboardAsyncLocked()
{
    ULONGLONG now = GetTickCount64();
    if (now - g_keyboardLastAsyncAdvance >= 25)
    {
        g_keyboardLastAsyncAdvance = now;
        AdvanceKeyboardInjectionLocked();
    }
}

static void AppendDirectInputDeviceDataEvents(void* rgdod, DWORD cbObjectData, LPDWORD pdwInOut, DWORD capacity)
{
    if (!rgdod || !pdwInOut || cbObjectData < sizeof(DIDEVICEOBJECTDATA_LOCAL) || !g_keyboardLockReady)
        return;

    EnterCriticalSection(&g_keyboardLock);
    KbdInjectKey key = {};
    bool isDown = false;
    if (GetCurrentInjectedKeyLocked(&key, &isDown))
    {
        DWORD used = *pdwInOut;
        BYTE* base = (BYTE*)rgdod;
        DWORD time = GetTickCount();

        if (key.shift && used < capacity)
        {
            DIDEVICEOBJECTDATA_LOCAL* item = (DIDEVICEOBJECTDATA_LOCAL*)(base + (SIZE_T)used * cbObjectData);
            memset(item, 0, sizeof(*item));
            item->dwOfs = DIK_LSHIFT_LOCAL;
            item->dwData = isDown ? 0x80 : 0;
            item->dwTimeStamp = time;
            item->dwSequence = g_keyboardSequence++;
            ++used;
        }

        if (used < capacity)
        {
            DIDEVICEOBJECTDATA_LOCAL* item = (DIDEVICEOBJECTDATA_LOCAL*)(base + (SIZE_T)used * cbObjectData);
            memset(item, 0, sizeof(*item));
            item->dwOfs = key.dik;
            item->dwData = isDown ? 0x80 : 0;
            item->dwTimeStamp = time;
            item->dwSequence = g_keyboardSequence++;
            ++used;
        }

        *pdwInOut = used;
        AdvanceKeyboardInjectionLocked();
    }
    LeaveCriticalSection(&g_keyboardLock);
}

static bool PatchVtableSlot(void** slot, void* detour, void** origOut, const char* tag)
{
    if (!slot || !detour)
        return false;

    if (*slot == detour)
        return true;

    DWORD oldProt = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &oldProt))
    {
        WriteLog("[Windower] %s vtable VirtualProtect failed err=%lu", tag, (unsigned long)GetLastError());
        return false;
    }

    if (origOut && !*origOut)
        *origOut = *slot;
    *slot = detour;

    VirtualProtect(slot, sizeof(void*), oldProt, &oldProt);
    FlushInstructionCache(GetCurrentProcess(), slot, sizeof(void*));
    WriteLog("[Windower] %s vtable hook installed slot=%p detour=%p", tag, (void*)slot, detour);
    return true;
}

static void HookDirectInputDeviceKeyboard(void* device)
{
    if (!device || g_diKeyboardDeviceHooked)
        return;

    void** vtable = *(void***)device;
    if (!vtable)
        return;

    bool okState = PatchVtableSlot(&vtable[9], (void*)HookedDIGetDeviceState, (void**)&g_origDIGetDeviceState, "IDirectInputDevice8::GetDeviceState");
    bool okData = PatchVtableSlot(&vtable[10], (void*)HookedDIGetDeviceData, (void**)&g_origDIGetDeviceData, "IDirectInputDevice8::GetDeviceData");
    g_diKeyboardDeviceHooked = okState || okData;
    WriteLog("[Windower] DirectInput keyboard device hook state=%d data=%d", okState ? 1 : 0, okData ? 1 : 0);
}

static void HookDirectInput8Object(void* di8)
{
    if (!di8 || g_di8CreateDeviceHooked)
        return;

    void** vtable = *(void***)di8;
    if (!vtable)
        return;

    if (PatchVtableSlot(&vtable[3], (void*)HookedDI8CreateDevice, (void**)&g_origDI8CreateDevice, "IDirectInput8::CreateDevice"))
        g_di8CreateDeviceHooked = true;
}

static HRESULT WINAPI HookedDirectInput8Create(HINSTANCE hinst, DWORD dwVersion, REFIID riidltf, LPVOID* ppvOut, void* punkOuter)
{
    DirectInput8Create_t orig = g_origDirectInput8Create ? g_origDirectInput8Create : (DirectInput8Create_t)g_directInput8CreateDetour.target;
    if (!orig)
        return E_FAIL;

    HRESULT hr = orig(hinst, dwVersion, riidltf, ppvOut, punkOuter);
    WriteLog("[Windower] DirectInput8Create hr=0x%08lX out=%p", (unsigned long)hr, (ppvOut ? *ppvOut : nullptr));
    if (SUCCEEDED(hr) && ppvOut && *ppvOut)
        HookDirectInput8Object(*ppvOut);
    return hr;
}

static HRESULT WINAPI HookedDirectInputCreateA(HINSTANCE hinst, DWORD dwVersion, void** ppvOut, void* punkOuter)
{
    DirectInputCreateA_t orig = g_origDirectInputCreateA ? g_origDirectInputCreateA : (DirectInputCreateA_t)g_directInputCreateADetour.target;
    if (!orig)
        return E_FAIL;

    HRESULT hr = orig(hinst, dwVersion, ppvOut, punkOuter);
    WriteLog("[Windower] DirectInputCreateA hr=0x%08lX out=%p", (unsigned long)hr, (ppvOut ? *ppvOut : nullptr));
    if (SUCCEEDED(hr) && ppvOut && *ppvOut)
        HookDirectInput8Object(*ppvOut);
    return hr;
}

static HRESULT WINAPI HookedDirectInputCreateW(HINSTANCE hinst, DWORD dwVersion, void** ppvOut, void* punkOuter)
{
    DirectInputCreateW_t orig = g_origDirectInputCreateW ? g_origDirectInputCreateW : (DirectInputCreateW_t)g_directInputCreateWDetour.target;
    if (!orig)
        return E_FAIL;

    HRESULT hr = orig(hinst, dwVersion, ppvOut, punkOuter);
    WriteLog("[Windower] DirectInputCreateW hr=0x%08lX out=%p", (unsigned long)hr, (ppvOut ? *ppvOut : nullptr));
    if (SUCCEEDED(hr) && ppvOut && *ppvOut)
        HookDirectInput8Object(*ppvOut);
    return hr;
}

static HRESULT WINAPI HookedDI8CreateDevice(void* self, REFGUID rguid, void** deviceOut, void* punkOuter)
{
    if (!g_origDI8CreateDevice)
        return E_FAIL;

    HRESULT hr = g_origDI8CreateDevice(self, rguid, deviceOut, punkOuter);
    const bool isKeyboard = GuidEquals(rguid, GUID_SysKeyboard_Local);
    WriteLog("[Windower] IDirectInput8::CreateDevice hr=0x%08lX keyboard=%d device=%p",
        (unsigned long)hr, isKeyboard ? 1 : 0, (deviceOut ? *deviceOut : nullptr));
    if (SUCCEEDED(hr) && isKeyboard && deviceOut && *deviceOut)
        HookDirectInputDeviceKeyboard(*deviceOut);
    return hr;
}

static HRESULT WINAPI HookedDIGetDeviceState(void* self, DWORD cbData, LPVOID lpvData)
{
    if (!g_origDIGetDeviceState)
        return E_FAIL;

    HRESULT hr = g_origDIGetDeviceState(self, cbData, lpvData);
    if (SUCCEEDED(hr) && lpvData && cbData >= 256)
        OverlayDirectInputKeyboardState((BYTE*)lpvData, cbData);
    return hr;
}

static HRESULT WINAPI HookedDIGetDeviceData(void* self, DWORD cbObjectData, void* rgdod, LPDWORD pdwInOut, DWORD dwFlags)
{
    if (!g_origDIGetDeviceData)
        return E_FAIL;

    DWORD capacity = pdwInOut ? *pdwInOut : 0;
    HRESULT hr = g_origDIGetDeviceData(self, cbObjectData, rgdod, pdwInOut, dwFlags);
    if (SUCCEEDED(hr) && pdwInOut && rgdod && capacity > *pdwInOut)
        AppendDirectInputDeviceDataEvents(rgdod, cbObjectData, pdwInOut, capacity);
    return hr;
}

static SHORT WINAPI HookedGetAsyncKeyState(int vKey)
{
    GetAsyncKeyState_t orig = g_origGetAsyncKeyState ? g_origGetAsyncKeyState : (GetAsyncKeyState_t)g_getAsyncKeyStateDetour.target;
    SHORT ret = orig ? orig(vKey) : 0;

    if (g_keyboardLockReady)
    {
        EnterCriticalSection(&g_keyboardLock);
        if (CurrentVirtualKeyDownLocked(vKey))
            ret = (SHORT)(ret | 0x8000);
        MaybeAdvanceKeyboardAsyncLocked();
        LeaveCriticalSection(&g_keyboardLock);
    }
    return ret;
}

static BOOL WINAPI HookedGetKeyboardState(PBYTE lpKeyState)
{
    GetKeyboardState_t orig = g_origGetKeyboardState ? g_origGetKeyboardState : (GetKeyboardState_t)g_getKeyboardStateDetour.target;
    BOOL ret = orig ? orig(lpKeyState) : FALSE;
    if (ret && lpKeyState && g_keyboardLockReady)
    {
        EnterCriticalSection(&g_keyboardLock);
        KbdInjectKey key = {};
        bool isDown = false;
        if (GetCurrentInjectedKeyLocked(&key, &isDown) && isDown)
        {
            lpKeyState[key.vk] |= 0x80;
            if (key.shift)
            {
                lpKeyState[VK_SHIFT] |= 0x80;
                lpKeyState[VK_LSHIFT] |= 0x80;
            }
        }
        MaybeAdvanceKeyboardAsyncLocked();
        LeaveCriticalSection(&g_keyboardLock);
    }
    return ret;
}

static void InstallKeyboardHooks()
{
    if (!IsKeyboardInjectEnabled())
        return;
    if (g_keyboardHooksAttempted)
        return;
    g_keyboardHooksAttempted = true;

    LogKeyboardApiDetection();

    HMODULE hDinput8 = GetModuleHandleA("dinput8.dll");
    if (!hDinput8)
        hDinput8 = LoadLibraryA("dinput8.dll");
    g_directInput8CreateDetour.target = hDinput8 ? (void*)GetProcAddress(hDinput8, "DirectInput8Create") : nullptr;
    if (g_directInput8CreateDetour.target && InstallInlineDetour(&g_directInput8CreateDetour))
        g_origDirectInput8Create = (DirectInput8Create_t)g_directInput8CreateDetour.trampoline;
    WriteLog("[Windower] keyboard DirectInput8Create hook installed=%d", g_directInput8CreateDetour.installed ? 1 : 0);

    HMODULE hDinput = GetModuleHandleA("dinput.dll");
    if (!hDinput)
        hDinput = LoadLibraryA("dinput.dll");
    g_directInputCreateADetour.target = hDinput ? (void*)GetProcAddress(hDinput, "DirectInputCreateA") : nullptr;
    if (g_directInputCreateADetour.target && InstallInlineDetour(&g_directInputCreateADetour))
        g_origDirectInputCreateA = (DirectInputCreateA_t)g_directInputCreateADetour.trampoline;
    WriteLog("[Windower] keyboard DirectInputCreateA hook installed=%d", g_directInputCreateADetour.installed ? 1 : 0);

    g_directInputCreateWDetour.target = hDinput ? (void*)GetProcAddress(hDinput, "DirectInputCreateW") : nullptr;
    if (g_directInputCreateWDetour.target && InstallInlineDetour(&g_directInputCreateWDetour))
        g_origDirectInputCreateW = (DirectInputCreateW_t)g_directInputCreateWDetour.trampoline;
    WriteLog("[Windower] keyboard DirectInputCreateW hook installed=%d", g_directInputCreateWDetour.installed ? 1 : 0);

    HMODULE hUser32 = GetModuleHandleA("user32.dll");
    if (!hUser32)
        hUser32 = LoadLibraryA("user32.dll");
    g_getAsyncKeyStateDetour.target = hUser32 ? (void*)GetProcAddress(hUser32, "GetAsyncKeyState") : nullptr;
    if (g_getAsyncKeyStateDetour.target && InstallInlineDetour(&g_getAsyncKeyStateDetour))
        g_origGetAsyncKeyState = (GetAsyncKeyState_t)g_getAsyncKeyStateDetour.trampoline;
    WriteLog("[Windower] keyboard GetAsyncKeyState hook installed=%d", g_getAsyncKeyStateDetour.installed ? 1 : 0);

    g_getKeyboardStateDetour.target = hUser32 ? (void*)GetProcAddress(hUser32, "GetKeyboardState") : nullptr;
    if (g_getKeyboardStateDetour.target && InstallInlineDetour(&g_getKeyboardStateDetour))
        g_origGetKeyboardState = (GetKeyboardState_t)g_getKeyboardStateDetour.trampoline;
    WriteLog("[Windower] keyboard GetKeyboardState hook installed=%d", g_getKeyboardStateDetour.installed ? 1 : 0);
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
    if (IsEnvFlagOne("MAPLEFORGE_WINDOWER_DISABLE_WINSOCK"))
    {
        if (!g_ws2HooksSkippedByEnv)
        {
            g_ws2HooksSkippedByEnv = true;
            WriteLog("[Windower] winsock hooks skipped(env)");
        }
    }
    else
    {
        InstallWs2Hooks();
    }

    if (IsEnvFlagOne("MAPLEFORGE_WINDOWER_DISABLE_D3D"))
    {
        if (!g_d3d8HookSkippedByEnv)
        {
            g_d3d8HookSkippedByEnv = true;
            WriteLog("[Windower] D3D hooks skipped(env)");
        }
    }
    else
    {
        const bool wasHooked = g_d3d8EntryHooked;
        if (InstallDirect3DCreate8Hook() && !wasHooked)
            WriteLog("[Windower] D3D hooks installed");
    }

    InstallKeyboardHooks();
}

// ── SetWindowsHookEx callback ────────────────────────────────────────────────

extern "C" __declspec(dllexport)
LRESULT CALLBACK CallWndProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0 && !g_d3d8EntryHooked && !g_d3d8HookSkippedByEnv)
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
        InitializeCriticalSection(&g_keyboardLock);
        g_keyboardLockReady = true;
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
        if (g_keyboardLockReady)
        {
            DeleteCriticalSection(&g_keyboardLock);
            g_keyboardLockReady = false;
        }
    }
    return TRUE;
}
