# [3-2a] Verify windower DirectInput keyboard injection: reach login, click fields,
# write strings to the kbd inject file, screenshot to confirm text lands + login proceeds.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Threading;
public class WJ {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(250); SetCursorPos(x,y); Thread.Sleep(120); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(200); }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
Add-Type -AssemblyName System.Drawing
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-inject-diag.log"; $Shot="$Root\diag-shots"
$KbdFile="$Root\tools\windower\captures\kbd.txt"
$env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"; $env:MAPLEFORGE_CAPTURE="1"
$env:MAPLEFORGE_WINDOWER_KBD_INJECT="1"; $env:MAPLEFORGE_WINDOWER_KBD_FILE=$KbdFile
Set-Content -Path $KbdFile -Value "" -NoNewline -Encoding ASCII
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Shot([IntPtr]$h,[string]$t){ [WJ+R]$r=New-Object 'WJ+R'; [void][WJ]::GetWindowRect($h,[ref]$r); $w=$r.Ri-$r.L;$ht=$r.B-$r.T; if($w-le 0){return}; $b=New-Object System.Drawing.Bitmap($w,$ht);$gg=[System.Drawing.Graphics]::FromImage($b);$gg.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size($w,$ht)));$b.Save("$Shot\inj-$t.png",[System.Drawing.Imaging.ImageFormat]::Png);$gg.Dispose();$b.Dispose(); Write-Host "  shot inj-$t" }
function Hits([string]$p){ if(Test-Path $Log){return (Select-String -Path $Log -Pattern $p -AllMatches -EA SilentlyContinue).Matches.Count} return 0 }
function InjectType([string]$s){ Set-Content -Path $KbdFile -Value $s -NoNewline -Encoding ASCII; Write-Host "  -> kbd.txt = '$s'"; Start-Sleep -Seconds 3 }
Get-Process MapleStory,windower_host,Maple.Host.Login -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1
$srv=$null;$wh=$null;$cli=$null
try{
    & "$Root\tools\seed-test-data.ps1" -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" 2>&1|Out-Null
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru; Start-Sleep 5
    $wh=Start-Process $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
    $cli=Start-Process $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    $h=[IntPtr]::Zero; for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WJ]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WJ]::Width($h) -ge 600){break} }
    Start-Sleep 5; if($h -ne [IntPtr]::Zero){ [WJ]::Click($h,0.5,0.965); Write-Host "Play!" }
    $g=[IntPtr]::Zero; for($i=0;$i -lt 30;$i++){ Start-Sleep 1; $g=[WJ]::FindByPid([uint]$cli.Id); if($g -ne [IntPtr]::Zero -and [WJ]::Width($g) -ge 780){break} }
    Write-Host "等 0x17..."; for($i=0;$i -lt 40;$i++){ Start-Sleep 1; if((Hits 'opcode=0x17') -gt 0){ Write-Host "0x17 t=$i"; break } }
    Start-Sleep 2; [WJ]::ShowWindow($g,9)|Out-Null; [WJ]::SetForegroundWindow($g)|Out-Null; Start-Sleep -Milliseconds 600
    Shot $g "1-login"
    [WJ]::Click($g,0.57,0.40); InjectType "test1234"; Shot $g "2-pw-injected"   # 密碼框注入
    [WJ]::Click($g,0.74,0.37); Start-Sleep -Milliseconds 2500; Shot $g "3-after-login"
    Write-Host "=== 登入訊號 ==="; Write-Host ("  0x17={0} 其他opcode={1}" -f (Hits 'opcode=0x17'),(Hits 'opcode=0x'))
    if(Test-Path $Log){ Get-Content $Log | Select-String -Pattern "opcode=0x|登入|LOGIN|PASSWORD|CHANNEL|地圖|角色" | Select-Object -Last 12 | %{ Write-Host "  SRV: $($_.Line)" } }
    Write-Host "=== inject.log 鍵盤偵測/注入 ==="
    $ij="$Root\tools\windower\captures\windower_inject.log"; if(Test-Path $ij){ Get-Content $ij | Select-String -Pattern "keyboard|inject|DirectInput|RawInput|GetAsyncKey" | Select-Object -Last 15 | %{ Write-Host "  $($_.Line)" } }
}
finally{
    if($cli){$cli|Stop-Process -Force -EA SilentlyContinue}; if($wh){$wh|Stop-Process -Force -EA SilentlyContinue}; if($srv){$srv|Stop-Process -Force -EA SilentlyContinue}
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"}); if($now -ne $origRes){ [WJ]::RestoreDisplay()|Out-Null }
    Write-Host "done"
}
