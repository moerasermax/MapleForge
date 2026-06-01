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

    // 顯示設定還原：傳 NULL DEVMODE 給 ChangeDisplaySettings → 套用登錄檔裡的「正常桌面解析度」
    // （客戶端是老 DirectDraw/D3D8，可能在啟動/離開時改解析度；這是還原使用者實機的安全閥）
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr devmode, int flags);
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero, 0); } // 0 = CDS_NONE → 套用登錄檔設定

    // 滑鼠點擊（用於點啟動器的 Play! 按鈕：MapleStory.exe 先開啟動器，點 Play! 才啟動 D3D 遊戲）
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    public static void ClickBottomCenter(IntPtr h){
        RECT r; GetWindowRect(h, out r);
        int x = r.L + (r.R - r.L)/2; int y = r.B - 20;   // 底部中央 = Play! 按鈕
        SetForegroundWindow(h); Thread.Sleep(300);
        SetCursorPos(x,y); Thread.Sleep(150);
        mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(80);   // LEFTDOWN
        mouse_event(0x0004,0,0,0,IntPtr.Zero);                     // LEFTUP
    }
    public static int WinWidth(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R - r.L; }
}
"@

$Root    = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge"
$CliDir  = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$CliExe  = "$CliDir\MapleStory.exe"
$Log     = "$Root\live.log"
if (Test-Path $Log) { Remove-Item $Log }
$dxwnd = $null

# 分析用計數器
$stats = @{ Connections=0; GotHandshake=$false; GotLogin=$false; GotChannel=$false; GotMap=$false }

# ── 實機安全快照（啟動客戶端前；finally 一定還原）────────────────────────────
$origRes = (Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentHorizontalResolution } |
            Select-Object -First 1 |
            ForEach-Object { "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" })
Write-Host "原始解析度快照: $origRes" -ForegroundColor DarkGray
# AppCompat Layers（客戶端需要 WINXPSP3 16BITCOLOR；快照以便 finally 還原，不主動更動）
$layerKey  = "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"
$origLayer = $null
try { $origLayer = (Get-ItemProperty -Path $layerKey -Name $CliExe -EA Stop).$CliExe } catch {}

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

# ── 清理殘留 server/test host（每次啟動 server 前必跑；只殺本專案自己的東西）──
function Clear-StaleHosts {
    Write-Host "=== [0] 清理殘留 server/test host ===" -ForegroundColor Cyan
    $killed = 0
    # 1. 直接的 server host exe（dotnet run 會以此名跑起來）
    Get-Process -Name "Maple.Host.Login","Maple.Host.Channel" -EA SilentlyContinue | ForEach-Object {
        Write-Host "    殺殘留 $($_.ProcessName) PID=$($_.Id)" -ForegroundColor DarkYellow
        $_ | Stop-Process -Force -EA SilentlyContinue; $killed++
    }
    # 2. dotnet.exe，但**只殺命令列指向本專案**的（保護 VS Code build host 等無辜 dotnet）
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -EA SilentlyContinue |
        Where-Object { $_.CommandLine -and (
            $_.CommandLine -match 'Maple\.Host\.Login' -or
            $_.CommandLine -match 'Maple\.Host\.Channel' -or
            ($_.CommandLine -match 'vstest|testhost' -and $_.CommandLine -match 'MapleForge')
        ) } | ForEach-Object {
            Write-Host "    殺殘留 dotnet PID=$($_.ProcessId)（本專案 host/test）" -ForegroundColor DarkYellow
            Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $killed++
        }
    # 3. 保險：誰還佔著我們的 port 就釋放（不論進程名）
    foreach ($p in 8484,8585) {
        $c = Get-NetTCPConnection -LocalPort $p -State Listen -EA SilentlyContinue
        if ($c) {
            $owner = ($c.OwningProcess | Select-Object -First 1)
            $pn = (Get-Process -Id $owner -EA SilentlyContinue).ProcessName
            Write-Host "    port $p 仍被 PID=$owner ($pn) 佔用 → 釋放" -ForegroundColor DarkYellow
            Stop-Process -Id $owner -Force -EA SilentlyContinue; $killed++
        }
    }
    if ($killed -eq 0) { Write-Host "    無殘留，乾淨。" -ForegroundColor DarkGreen }
    else { Write-Host "    清掉 $killed 個殘留。" -ForegroundColor Green; Start-Sleep -Milliseconds 600 }
}
Clear-StaleHosts

try {

# ── [2a] 種測試資料（testuser 帳號 + TestHero 角色）──────────────────────────
# 必須在 server 開 DB 之前跑（LiteDB 檔案鎖）；用 seed-test-data.ps1（連帳號一起建，正確 int _id）
$SeedScript = Join-Path $Root "tools\seed-test-data.ps1"
if (Test-Path $SeedScript) {
    Write-Host "=== [2a] 種測試資料 (testuser / TestHero) ===" -ForegroundColor Cyan
    & $SeedScript -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero"
    if ($LASTEXITCODE -ne 0) { throw "種子腳本失敗 ExitCode=$LASTEXITCODE" }
} else {
    Write-Host "=== [2a] 找不到 seed-test-data.ps1，跳過 ===" -ForegroundColor DarkYellow
}

# ── Server ──────────────────────────────────────────────────────────────────
Write-Host "=== [1] Server 啟動 ===" -ForegroundColor Cyan
$srv = Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" `
    -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru
Start-Sleep 5
$ok = Test-NetConnection 127.0.0.1 -Port 8484 -InformationLevel Quiet -WarningAction SilentlyContinue
Write-Host "    8484=$ok"

# ── 視窗化說明 ───────────────────────────────────────────────────────────────
# 客戶端 SolusTech.ini 已 Windowed=1 → 原生視窗化（實測 636x536 無邊框小視窗，不改解析度）。
# 自製 windower 對「自動化測試」非必要；故預設不注入（與 Themida 硬幹易脆弱）。
# 若日後要驗 windower 診斷 log，再手動啟動 tools\windower\bin\windower_host.exe。

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
    
    # 等待 MapleStory 視窗出現（最多 20 秒）
    Write-Host "    等待 MapleStory 視窗出現..." -ForegroundColor DarkYellow
    $hWnd = [IntPtr]::Zero
    for ($w = 0; $w -lt 20; $w++) {
        $allPids = [System.Collections.Generic.HashSet[uint]]::new()
        $allPids.Add([uint]$cli.Id) | Out-Null
        $hWnd = [WinAPI]::FindWindowByPids($allPids)
        if ($hWnd -ne [IntPtr]::Zero) { Write-Host "    視窗已出現 hWnd=$hWnd"; break }
        Start-Sleep -Seconds 1
        Write-Host "    等視窗 $($w+1)/20..." -NoNewline
    }
    if ($hWnd -eq [IntPtr]::Zero) {
        Write-Host "    hWnd=0，嘗試 MainWindowHandle..." -ForegroundColor Yellow
        $hWnd = (Get-Process -Id $cli.Id -EA SilentlyContinue).MainWindowHandle
    }
    # ── 啟動器：MapleStory.exe 先開「啟動器」(636寬,白底+Play!按鈕)，點 Play! 才啟動 D3D 遊戲(800寬) ──
    Start-Sleep -Seconds 7   # 等啟動器完全渲染、Play! 按鈕就緒（太早點不中）
    for ($p = 1; $p -le 5; $p++) {
        $hWnd = [WinAPI]::FindWindowByPids($allPids)
        if ($hWnd -eq [IntPtr]::Zero) { $hWnd = (Get-Process -Id $cli.Id -EA SilentlyContinue).MainWindowHandle }
        if (-not $hWnd -or $hWnd -eq [IntPtr]::Zero) { Write-Host "    [$p] 無視窗，等待..."; Start-Sleep 2; continue }
        $wpx = [WinAPI]::WinWidth($hWnd)
        if ($wpx -ge 780) { Write-Host "    ✓ 遊戲視窗已就緒（寬=$wpx）" -ForegroundColor Green; break }
        Write-Host "    [$p] 啟動器寬=$wpx → 點 Play!（底部中央）" -ForegroundColor Cyan
        [WinAPI]::ClickBottomCenter($hWnd)
        Start-Sleep -Seconds 5
    }

    # ── 等遊戲真正連上登入伺服器（0x17 心跳 = 登入畫面已就緒、可接受輸入）──
    # 實測：遊戲視窗出現後還要數秒才連上登入畫面；太早打帳密會落空
    Write-Host "    等遊戲連上登入畫面（0x17 心跳）..." -ForegroundColor DarkYellow
    [void](Watch-Pattern "opcode=0x17" 35 "遊戲登入畫面就緒(0x17)")
    Start-Sleep -Seconds 2

    # ── 遊戲視窗(800x600 D3D)就緒後輸入帳密（登入框在 Play! 之後才有）──
    $hWnd = [WinAPI]::FindWindowByPids($allPids)
    if ($hWnd -eq [IntPtr]::Zero) { $hWnd = (Get-Process -Id $cli.Id -EA SilentlyContinue).MainWindowHandle }
    Write-Host "=== [AUTO] 輸入帳密 (hWnd=$hWnd 寬=$([WinAPI]::WinWidth($hWnd))) ===" -ForegroundColor Cyan
    if ($hWnd -and $hWnd -ne [IntPtr]::Zero) {
        [WinAPI]::ShowWindow($hWnd, 9) | Out-Null
        [WinAPI]::SetForegroundWindow($hWnd) | Out-Null
        Start-Sleep -Milliseconds 1000
        [WinAPI]::Press([WinAPI]::ESC)            # 跳過開場/通知
        Start-Sleep -Milliseconds 1200
        [WinAPI]::Type("testuser")
        Start-Sleep -Milliseconds 500
        [WinAPI]::Press([WinAPI]::TAB)
        Start-Sleep -Milliseconds 400
        [WinAPI]::Type("test1234")
        Start-Sleep -Milliseconds 400
        [WinAPI]::Press([WinAPI]::ENTER)
        Write-Host "    帳密已輸入 (testuser / test1234)"
    } else {
        Write-Host "    找不到遊戲視窗，無法輸入帳密" -ForegroundColor Red
    }

    # 等登入結果（server 端 ground-truth）
    Write-Host "=== [AUTO] 等待登入結果... ===" -ForegroundColor Yellow
    $stats['GotLogin'] = Watch-Pattern "登入成功|LOGIN_PASSWORD|解密封包 opcode" 30 "登入封包/成功"

    # 登入後 → 選角進地圖（連按 ENTER 選第一個角色 + 確認）
    if ($stats['GotLogin']) {
        Start-Sleep -Seconds 2
        $hWnd = [WinAPI]::FindWindowByPids($allPids)
        if ($hWnd -eq [IntPtr]::Zero) { $hWnd = (Get-Process -Id $cli.Id -EA SilentlyContinue).MainWindowHandle }
        if ($hWnd -and $hWnd -ne [IntPtr]::Zero) {
            Write-Host "=== [AUTO] 選角進地圖（ENTER x4）===" -ForegroundColor Cyan
            for ($k=0; $k -lt 4; $k++) { [WinAPI]::SetForegroundWindow($hWnd) | Out-Null; Start-Sleep -Milliseconds 1500; [WinAPI]::Press([WinAPI]::ENTER) }
        }
    } else {
        Write-Host "    （登入未偵測到）" -ForegroundColor DarkYellow
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

# ── 等待進入地圖（ground-truth = server log「已進入地圖」）─────────────────────
Write-Host "`n=== [4] 等待進入地圖 (30秒) ===" -ForegroundColor Yellow
for ($i=1; $i -le 30; $i++) {
    Start-Sleep 1
    if (Test-Path $Log) {
        if (Select-String -Path $Log -Pattern "CHANNEL|SELECT|PLAYER|SET_FIELD" -Quiet) {
            $stats['GotChannel'] = $true
        }
        if (Select-String -Path $Log -Pattern "已進入地圖" -Quiet) {
            if (-not $stats['GotMap']) { Write-Host "    ✓✓ 已進入地圖（ground-truth 成功訊號）" -ForegroundColor Green }
            $stats['GotMap'] = $true; $stats['GotChannel'] = $true
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
Write-Host "進入地圖:        $(if($stats['GotMap']){'是'}else{'否'})" -ForegroundColor $(if($stats['GotMap']){'Green'}else{'Red'})

# 整體判斷（GotMap = ground-truth 真正成功）
if ($stats['GotMap']) {
    $verdict = "成功（已進入地圖）"
    $color = "Green"
} elseif ($stats['GotLogin'] -and $stats['GotChannel']) {
    $verdict = "部分成功（到選角/頻道，未確認進地圖）"
    $color = "Yellow"
} elseif ($stats['GotLogin']) {
    $verdict = "部分成功（登入）"
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

}
finally {
    # ── 清理（無論成敗一定執行）──────────────────────────────────────────────
    Write-Host "`n=== [清理] 還原實機 + 收進程 ===" -ForegroundColor Cyan
    if ($cli)   { $cli   | Stop-Process -Force -EA SilentlyContinue }
    if ($srv)   { $srv   | Stop-Process -Force -EA SilentlyContinue }
    if ($dxwnd) { $dxwnd | Stop-Process -Force -EA SilentlyContinue }
    # 再掃一次本專案殘留（保險，呼應 Clear-StaleHosts）
    Get-Process -Name "Maple.Host.Login","Maple.Host.Channel" -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
    # 還原解析度：若客戶端改過 → 套回登錄檔正常設定（使用者實機安全閥）
    $nowRes = (Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentHorizontalResolution } |
               Select-Object -First 1 | ForEach-Object { "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" })
    if ($nowRes -and $origRes -and $nowRes -ne $origRes) {
        Write-Host "    ⚠️ 解析度被改 ($origRes → $nowRes)，還原中..." -ForegroundColor Yellow
        [WinAPI]::RestoreDisplay() | Out-Null
        Start-Sleep -Milliseconds 500
    } else {
        Write-Host "    解析度未變 ($origRes) ✓" -ForegroundColor DarkGreen
    }
    Write-Host "完成"
}
