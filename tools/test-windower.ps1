# 驗證 MapleForge windower「接管視窗化」是否生效。
# 流程：清殘留 → 起 server → 注入 windower_host → 啟動客戶端 → 點 Play! → 遊戲 D3D init 觸發 hook
#       → 檢查遊戲視窗是否被改成有標題列/邊框的正常視窗 + 標題 "MapleForge" + 讀 windower log。
# 安全：try/finally 快照+還原桌面解析度；只殺本專案進程。不依賴登入（純驗視窗化）。
$ErrorActionPreference = "Continue"
$Root = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge"
$CliDir = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$WLog = "C:\windower_inject.log"

Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public struct RC{public int L,T,R,B;}
public class WW{
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr l);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RC r);
 [DllImport("user32.dll")]public static extern int GetWindowLong(IntPtr h,int i);
 [DllImport("user32.dll",CharSet=CharSet.Auto)]public static extern int GetWindowText(IntPtr h,StringBuilder s,int c);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
 [DllImport("user32.dll")]public static extern int ChangeDisplaySettings(IntPtr d,int f);
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 public static IntPtr Find(uint pid){IntPtr r=IntPtr.Zero;EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid&&IsWindowVisible(h)){r=h;return false;}return true;},IntPtr.Zero);return r;}
 public static int Width(IntPtr h){RC r;GetWindowRect(h,out r);return r.R-r.L;}
 public static string Title(IntPtr h){var s=new StringBuilder(256);GetWindowText(h,s,256);return s.ToString();}
 public static void ClickBottomCenter(IntPtr h){RC r;GetWindowRect(h,out r);int x=r.L+(r.R-r.L)/2,y=r.B-20;SetForegroundWindow(h);System.Threading.Thread.Sleep(300);SetCursorPos(x,y);System.Threading.Thread.Sleep(150);mouse_event(0x0002,0,0,0,IntPtr.Zero);System.Threading.Thread.Sleep(80);mouse_event(0x0004,0,0,0,IntPtr.Zero);}
}
"@

function StyleDesc($s){
    $d=@()
    if($s -band 0x00C00000){$d+="WS_CAPTION(標題列)"}
    if($s -band 0x00040000){$d+="WS_THICKFRAME(可縮放邊框)"}
    if($s -band 0x00080000){$d+="WS_SYSMENU"}
    if($s -band 0x80000000){$d+="WS_POPUP(無框)"}
    if($d.Count -eq 0){$d+="(無)"}
    return ($d -join "+")
}

$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
Write-Host "解析度快照: $origRes" -ForegroundColor DarkGray
$wlBase = if(Test-Path $WLog){(Get-Content $WLog).Count}else{0}
$srv=$null;$wh=$null;$cli=$null
try{
    Get-Process Maple.Host.Login,Maple.Host.Channel,MapleStory -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    Write-Host "=== 起 server ===" -ForegroundColor Cyan
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput "$Root\live-windower.log" -NoNewWindow -PassThru
    Start-Sleep 5
    Write-Host "=== 注入 windower_host ===" -ForegroundColor Cyan
    $wh=Start-Process "$Root\tools\windower\bin\windower_host.exe" -PassThru -WindowStyle Minimized
    Start-Sleep -Milliseconds 1200
    if($wh.HasExited){Write-Host "windower_host 提前退出 code=$($wh.ExitCode)" -ForegroundColor Red}
    Write-Host "=== 啟動客戶端 ===" -ForegroundColor Cyan
    $cli=Start-Process "$CliDir\MapleStory.exe" -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    Write-Host "client PID=$($cli.Id)，等啟動器..."
    Start-Sleep 7
    # 點 Play! 直到遊戲視窗(寬>=780)
    for($p=1;$p -le 5;$p++){
        $h=[WW]::Find([uint]$cli.Id); if($h -eq [IntPtr]::Zero){Start-Sleep 2;continue}
        $w=[WW]::Width($h)
        if($w -ge 780){Write-Host "  遊戲視窗就緒 寬=$w" -ForegroundColor Green;break}
        Write-Host "  [$p] 啟動器寬=$w → 點 Play!"; [WW]::ClickBottomCenter($h); Start-Sleep 5
    }
    Write-Host "=== 等 D3D init + windower hook 套用視窗化 (12s) ===" -ForegroundColor Cyan
    Start-Sleep 12
    $h=[WW]::Find([uint]$cli.Id)
    if($h -ne [IntPtr]::Zero){
        $st=[WW]::GetWindowLong($h,-16)
        $title=[WW]::Title($h)
        $rc=New-Object RC;[WW]::GetWindowRect($h,[ref]$rc)|Out-Null
        Write-Host "`n──── 視窗化接管驗證 ────" -ForegroundColor Yellow
        Write-Host "  視窗大小 : $(($rc.R-$rc.L))x$(($rc.B-$rc.T)) @($($rc.L),$($rc.T))"
        Write-Host "  視窗標題 : '$title'  $(if($title -match 'MapleForge'){'✓ windower 設定的標題'}else{'✗ 非 MapleForge'})" -ForegroundColor $(if($title -match 'MapleForge'){'Green'}else{'Red'})
        Write-Host "  視窗樣式 : $('0x{0:X8}' -f $st) = $(StyleDesc $st)"
        $hasCaption = ($st -band 0x00C00000) -ne 0
        Write-Host "  接管結果 : $(if($hasCaption){'✓ 有標題列/邊框 = windower 接管成功'}else{'✗ 仍無邊框 = windower 未生效'})" -ForegroundColor $(if($hasCaption){'Green'}else{'Red'})
    } else { Write-Host "找不到遊戲視窗" -ForegroundColor Red }
    Write-Host "`n>>> 視窗保持開啟 30 秒供你觀察：看標題列是否寫『MapleForge』、有無邊框、裡面是不是正常遊戲(登入)畫面 <<<" -ForegroundColor Magenta
    for($s=30;$s -gt 0;$s-=5){ Write-Host "    ...剩 $s 秒"; Start-Sleep 5 }
}finally{
    if($cli){$cli|Stop-Process -Force -EA SilentlyContinue}
    if($wh){$wh|Stop-Process -Force -EA SilentlyContinue}
    if($srv){$srv|Stop-Process -Force -EA SilentlyContinue}
    Get-Process Maple.Host.Login,Maple.Host.Channel,MapleStory -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
    if($now -and $origRes -and $now -ne $origRes){Write-Host "解析度被改 $origRes->$now → 還原" -ForegroundColor Yellow;[WW]::ChangeDisplaySettings([IntPtr]::Zero,0)|Out-Null}else{Write-Host "解析度未變 ($origRes) ✓" -ForegroundColor DarkGreen}
}
Write-Host "`n=== windower log 新增行 ===" -ForegroundColor Cyan
if(Test-Path $WLog){Get-Content $WLog|Select-Object -Skip $wlBase}
