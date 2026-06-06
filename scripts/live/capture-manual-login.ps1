# 人在迴路 s2c 擷取：啟動 server+windower+client，自動點 Play! 帶到登入畫面，
# 然後「保持執行、不關閉」並結束腳本，讓使用者用真實鍵盤登入。windower 全程錄。
# 之後由 collect 步驟收集+解碼+清理。
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Threading;
public class WC {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(250); SetCursorPos(x,y); Thread.Sleep(120); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(180); }
}
"@
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-manual.log"
$env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"; $env:MAPLEFORGE_CAPTURE="1"
# 不開鍵盤注入（人類用真實鍵盤）
Get-Process MapleStory,windower_host,Maple.Host.Login -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1
& "$Root\tools\seed-test-data.ps1" -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" 2>&1|Out-Null
$srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru; Start-Sleep 5
$wh=Start-Process $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
$cli=Start-Process $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
Write-Host "client PID=$($cli.Id)"
$h=[IntPtr]::Zero; for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WC]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WC]::Width($h) -ge 600){break} }
Start-Sleep 5; if($h -ne [IntPtr]::Zero){ [WC]::Click($h,0.5,0.965); Write-Host "clicked Play!" }
# 等登入畫面就緒（0x17）
$ready=$false
for($i=0;$i -lt 45;$i++){ Start-Sleep 1; if((Test-Path $Log) -and (Select-String -Path $Log -Pattern "opcode=0x17" -Quiet -EA SilentlyContinue)){ $ready=$true; Write-Host "LOGIN SCREEN READY t=$i"; break } }
Write-Host "=============================================="
Write-Host (" 登入畫面就緒：{0}" -f $ready)
Write-Host (" client PID = {0}" -f $cli.Id)
Write-Host (" server PID = {0}" -f $srv.Id)
Write-Host (" windower_host PID = {0}" -f $wh.Id)
Write-Host (" 擷取輸出目錄 = {0}\tools\windower\captures" -f $Root)
Write-Host " 帳號 testuser / 密碼 test1234（記憶帳號可能已填帳號）"
Write-Host " >> 請用鍵盤登入、進遊戲走動/開面板，windower 全程錄。完成後告訴 AI 收集。"
Write-Host " （本腳本結束但進程保持執行）"
Write-Host "=============================================="
