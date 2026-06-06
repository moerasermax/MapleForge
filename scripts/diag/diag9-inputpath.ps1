# [3-2a] Reveal the login field's keyboard input path via windower KBD_DEBUG instrumentation.
# Reach login, focus 密碼 field, wait passively (polling APIs accumulate counts even w/o input),
# then PostMessage WM_CHAR to test the message path. Dump inject.log keyboard diagnostics.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Threading;
public class WX {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(250); SetCursorPos(x,y); Thread.Sleep(120); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(200); }
    public static void PostChar(IntPtr h,string s){ foreach(var c in s){ PostMessage(h,0x0100,(IntPtr)0x41,IntPtr.Zero); PostMessage(h,0x0102,(IntPtr)c,IntPtr.Zero); PostMessage(h,0x0101,(IntPtr)0x41,IntPtr.Zero); Thread.Sleep(60);} }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-path-diag.log"
$ij="$Root\tools\windower\captures\windower_inject.log"
$env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"; $env:MAPLEFORGE_CAPTURE="1"
$env:MAPLEFORGE_WINDOWER_KBD_DEBUG="1"
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Hits([string]$p){ if(Test-Path $Log){return (Select-String -Path $Log -Pattern $p -AllMatches -EA SilentlyContinue).Matches.Count} return 0 }
Get-Process MapleStory,windower_host,Maple.Host.Login -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1
$srv=$null;$wh=$null;$cli=$null
try{
    & "$Root\tools\seed-test-data.ps1" -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" 2>&1|Out-Null
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru; Start-Sleep 5
    $wh=Start-Process $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
    $cli=Start-Process $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    $h=[IntPtr]::Zero; for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WX]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WX]::Width($h) -ge 600){break} }
    Start-Sleep 5; if($h -ne [IntPtr]::Zero){ [WX]::Click($h,0.5,0.965); Write-Host "Play!" }
    $g=[IntPtr]::Zero; for($i=0;$i -lt 30;$i++){ Start-Sleep 1; $g=[WX]::FindByPid([uint]$cli.Id); if($g -ne [IntPtr]::Zero -and [WX]::Width($g) -ge 780){break} }
    Write-Host "等 0x17..."; for($i=0;$i -lt 40;$i++){ Start-Sleep 1; if((Hits 'opcode=0x17') -gt 0){ Write-Host "0x17 t=$i"; break } }
    Start-Sleep 2
    Write-Host "=== 階段A：聚焦密碼框後靜候 6s（看 poll 類 API 計數）==="
    [WX]::ShowWindow($g,9)|Out-Null; [WX]::SetForegroundWindow($g)|Out-Null; Start-Sleep -Milliseconds 400
    [WX]::Click($g,0.57,0.40); Start-Sleep -Seconds 6
    Write-Host "=== 階段B：PostMessage WM_KEYDOWN/CHAR/KEYUP x6（看訊息路徑）==="
    [WX]::PostChar($g,"abc123"); Start-Sleep -Seconds 2
    Write-Host "=== inject.log 鍵盤輸入路徑診斷（最後 40 行相關）==="
    if(Test-Path $ij){ Get-Content $ij | Select-String -Pattern "keyboard poll|keyboard msg|GetAsyncKeyState|GetKeyboardState|GetDeviceState|GetDeviceData|PeekMessage|GetMessage|TranslateMessage|WM_KEY|WM_CHAR|detection" | Select-Object -Last 40 | %{ Write-Host "  $($_.Line)" } }
}
finally{
    if($cli){$cli|Stop-Process -Force -EA SilentlyContinue}; if($wh){$wh|Stop-Process -Force -EA SilentlyContinue}; if($srv){$srv|Stop-Process -Force -EA SilentlyContinue}
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"}); if($now -ne $origRes){ [WX]::RestoreDisplay()|Out-Null }
    Write-Host "done"
}
