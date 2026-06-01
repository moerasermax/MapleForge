# MapleForge Live 測試 v5 - 自動/手動模式
param([ValidateSet("Auto","Manual")][string]$Mode = "Auto")

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class WinAPI {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // 找指定 PID（含子進程 PID 集合）的第一個可見視窗
    public static IntPtr FindWindowByPids(System.Collections.Generic.HashSet<uint> pids) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) => {
            if (!IsWindowVisible(hWnd)) return true;
            uint pid; GetWindowThreadProcessId(hWnd, out pid);
            if (pids.Contains(pid)) { found = hWnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT {
        [FieldOffset(0)] public uint type;
        [FieldOffset(4)] public KEYBDINPUT ki;
    }
    public const uint KBD=1, KEYUP=2;
    public const ushort ESC=0x1B,TAB=0x09,ENTER=0x0D,SHIFT=0x10;

    public static void Press(ushort vk){
        var a=new INPUT[2];
        a[0].type=KBD; a[0].ki.wVk=vk;
        a[1].type=KBD; a[1].ki.wVk=vk; a[1].ki.dwFlags=KEYUP;
        SendInput(2,a,System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(80);
    }
    public static void Type(string s){
        foreach(var c in s){
            short vks=VkKeyScan(c); ushort vk=(ushort)(vks&0xFF);
            bool sh=(vks&0x100)!=0;
            if(sh){ var si=new INPUT[1]; si[0].type=KBD; si[0].ki.wVk=SHIFT; SendInput(1,si,System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT))); }
            Press(vk);
            if(sh){ var si=new INPUT[1]; si[0].type=KBD; si[0].ki.wVk=SHIFT; si[0].ki.dwFlags=KEYUP; SendInput(1,si,System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT))); }
        }
    }
}
"@

$Root    = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge"
$CliDir  = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$CliExe  = "$CliDir\MapleStory.exe"
$Log     = "$Root\live.log"
if (Test-Path $Log) { Remove-Item $Log }

# 分析用計數器
$stats = @{ Connections=0; GotHandshake=$false; GotLogin=$false; GotChannel=$false }

function Watch-Pattern {
    param([string]$Pattern, [int]$TimeoutSec, [string]$Label)
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSec) {
        if (Test-Path $Log) {
            $found = Select-String -Path $Log -Pattern $Pattern -Quiet
            if ($found) { Write-Host "    ✓ $Label" -ForegroundColor Green; return $true }
        }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "    ✗ $Label (timeout)" -ForegroundColor Red
    return $false
}

function Show-NewLogs {
    param([int]$Count = 3)
    if (Test-Path $Log) {
        Get-Content $Log -Tail $Count | ForEach-Object { Write-Host "  SRV: $_" -ForegroundColor DarkGreen }
    }
}

# ── Server ──────────────────────────────────────────────────────────────────
Write-Host "=== [1] Server 啟動 ===" -ForegroundColor Cyan
$srv = Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" `
    -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru
Start-Sleep 5
$ok = Test-NetConnection 127.0.0.1 -Port 8484 -InformationLevel Quiet -WarningAction SilentlyContinue
Write-Host "    8484=$ok"

# ── Client ──────────────────────────────────────────────────────────────────
Write-Host "=== [2] Client 啟動 ===" -ForegroundColor Cyan
$cli = Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
Write-Host "    PID=$($cli.Id)"

# ── 等握手完成（客戶端到達 login 畫面前）──────────────────────────────────────
Write-Host "=== [3] 等待握手... ===" -ForegroundColor Yellow
$stats['GotHandshake'] = Watch-Pattern "握手送出" 30 "握手送出"

# ── 根據模式執行 ─────────────────────────────────────────────────────────────
if ($Mode -eq "Auto") {
    Write-Host "=== [AUTO] 自動模式 ===" -ForegroundColor Cyan
    
    # 用 EnumWindows 掃描所有視窗（包含子進程）
    $childPids = (Get-Process -EA SilentlyContinue | Where-Object { $_.Parent.Id -eq $cli.Id }).Id
    $allPids = [System.Collections.Generic.HashSet[uint]]::new()
    $allPids.Add([uint]$cli.Id) | Out-Null
    foreach ($cp in $childPids) { $allPids.Add([uint]$cp) | Out-Null }
    $hWnd = [WinAPI]::FindWindowByPids($allPids)
    if ($hWnd -eq [IntPtr]::Zero) {
        $hWnd = (Get-Process -Id $cli.Id -EA SilentlyContinue).MainWindowHandle
    }
    Write-Host "    hWnd=$hWnd (PID=$($cli.Id), 子進程=$($childPids -join ',')) → 送 ESC"
    if ($hWnd -and $hWnd -ne [IntPtr]::Zero) {
        [WinAPI]::ShowWindow($hWnd, 9) | Out-Null
        [WinAPI]::SetForegroundWindow($hWnd) | Out-Null
        Start-Sleep -Milliseconds 1500
        [WinAPI]::Press([WinAPI]::ESC)
        Write-Host "    ESC 已送出"
    } else {
        Write-Host "    找不到視窗！" -ForegroundColor Red
    }
    
    # 等 LOGIN_PASSWORD / AUTH
    Write-Host "=== [AUTO] 等待登入封包... ===" -ForegroundColor Yellow
    $stats['GotLogin'] = Watch-Pattern "解密封包|LOGIN_PASSWORD|AUTH" 60 "登入封包"
    
    if ($stats['GotLogin']) {
        # 輸入帳密
        $hWnd = [WinAPI]::FindWindowByPids($allPids)
        if ($hWnd -eq [IntPtr]::Zero) { $hWnd = (Get-Process -Id $cli.Id -EA SilentlyContinue).MainWindowHandle }
        Write-Host "=== [AUTO] 輸入帳密 (hWnd=$hWnd) ===" -ForegroundColor Cyan
        if ($hWnd -and $hWnd -ne [IntPtr]::Zero) {
            [WinAPI]::SetForegroundWindow($hWnd) | Out-Null
            Start-Sleep -Milliseconds 800
            [WinAPI]::Type("testuser")
            Start-Sleep -Milliseconds 600
            [WinAPI]::Press([WinAPI]::TAB)
            Start-Sleep -Milliseconds 400
            [WinAPI]::Type("test1234")
            Start-Sleep -Milliseconds 400
            [WinAPI]::Press([WinAPI]::ENTER)
            Write-Host "    帳密送出"
        }
    } else {
        Write-Host "`n=== 需要手動操作 ===" -ForegroundColor Yellow
        Write-Host "1. 在遊戲視窗按 ESC"
        Write-Host "2. 輸入帳號: testuser"
        Write-Host "3. 輸入密碼: test1234"
        Write-Host "4. 按 Enter"
        Write-Host "AI 會持續監測 server log..."
        
        # 手動模式備援
        for ($i=1; $i -le 120; $i++) {
            Start-Sleep 1
            Show-NewLogs 2
            if (Test-Path $Log) {
                if (Select-String -Path $Log -Pattern "LOGIN_PASSWORD|AUTH" -Quiet) {
                    $stats['GotLogin'] = $true
                }
            }
        }
    }
} else {
    Write-Host "=== [MANUAL] 手動模式 ===" -ForegroundColor Cyan
    Write-Host "`n=== 請手動操作 ===" -ForegroundColor Yellow
    Write-Host "1. 在遊戲視窗按 ESC"
    Write-Host "2. 輸入帳號: testuser"
    Write-Host "3. 輸入密碼: test1234"
    Write-Host "4. 按 Enter"
    Write-Host "AI 會持續監測 server log (120秒)..."
    
    for ($i=1; $i -le 120; $i++) {
        Start-Sleep 1
        Write-Host "    [$i/120]" -NoNewline
        Show-NewLogs 2
        if (Test-Path $Log) {
            if (Select-String -Path $Log -Pattern "LOGIN_PASSWORD|AUTH" -Quiet) {
                $stats['GotLogin'] = $true
            }
        }
    }
}

# ── 等待進入 channel ────────────────────────────────────────────────────────
Write-Host "`n=== [4] 等待進入 channel (30秒) ===" -ForegroundColor Yellow
for ($i=1; $i -le 30; $i++) {
    Start-Sleep 1
    if (Test-Path $Log) {
        if (Select-String -Path $Log -Pattern "CHANNEL|SELECT|PLAYER" -Quiet) {
            $stats['GotChannel'] = $true
            Write-Host "    ✓ 偵測到 channel 相關封包" -ForegroundColor Green
        }
    }
    Show-NewLogs 1
}

# ── 統計連線次數 ─────────────────────────────────────────────────────────────
if (Test-Path $Log) {
    $stats['Connections'] = (Select-String -Path $Log -Pattern "Client connected|連線" -AllMatches).Matches.Count
}

# ── 分析摘要 ─────────────────────────────────────────────────────────────────
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "          測試結果摘要" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "連線次數:        $($stats['Connections'])" -ForegroundColor White
Write-Host "收到握手:        $(if($stats['GotHandshake']){'是'}else{'否'})" -ForegroundColor $(if($stats['GotHandshake']){'Green'}else{'Red'})
Write-Host "收到 LOGIN:      $(if($stats['GotLogin']){'是'}else{'否'})" -ForegroundColor $(if($stats['GotLogin']){'Green'}else{'Red'})
Write-Host "進入 Channel:    $(if($stats['GotChannel']){'是'}else{'否'})" -ForegroundColor $(if($stats['GotChannel']){'Green'}else{'Red'})

# 整體判斷
if ($stats['GotLogin'] -and $stats['GotChannel']) {
    $verdict = "成功"
    $color = "Green"
} elseif ($stats['GotLogin']) {
    $verdict = "部分成功"
    $color = "Yellow"
} else {
    $verdict = "需要人工介入"
    $color = "Red"
}
Write-Host "`n整體判斷: $verdict" -ForegroundColor $color
Write-Host "========================================`n" -ForegroundColor Cyan

# ── 完整 Log ─────────────────────────────────────────────────────────────────
Write-Host "=== 完整 Server Log ===" -ForegroundColor Yellow
if (Test-Path $Log) { Get-Content $Log } else { Write-Host "(空)" }

# ── 清理 ─────────────────────────────────────────────────────────────────────
$cli | Stop-Process -Force -EA SilentlyContinue
$srv | Stop-Process -Force -EA SilentlyContinue
Write-Host "完成"
