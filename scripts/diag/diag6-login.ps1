# [3-2] Login-flow diagnostic: reach login screen, then step through credential entry
# WITH screenshots after each step, to see exactly where auto-login fails to land.
# Uses its OWN log path (NOT live.log) to avoid collision with test-live.ps1.
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class WL {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char c);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(4)] public KEYBDINPUT ki; }
    public const uint KBD=1, KEYUP=2; public const ushort ESC=0x1B,TAB=0x09,ENTER=0x0D,SHIFT=0x10;
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Press(ushort vk){ var a=new INPUT[2]; a[0].type=KBD; a[0].ki.wVk=vk; a[1].type=KBD; a[1].ki.wVk=vk; a[1].ki.dwFlags=KEYUP; SendInput(2,a,Marshal.SizeOf(typeof(INPUT))); Thread.Sleep(90); }
    public static void Type(string s){ foreach(var c in s){ short v=VkKeyScan(c); ushort vk=(ushort)(v&0xFF); bool sh=(v&0x100)!=0; if(sh){ var si=new INPUT[1]; si[0].type=KBD; si[0].ki.wVk=SHIFT; SendInput(1,si,Marshal.SizeOf(typeof(INPUT))); } Press(vk); if(sh){ var si=new INPUT[1]; si[0].type=KBD; si[0].ki.wVk=SHIFT; si[0].ki.dwFlags=KEYUP; SendInput(1,si,Marshal.SizeOf(typeof(INPUT))); } } }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(300); SetCursorPos(x,y); Thread.Sleep(150); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(80); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(200); }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
Add-Type -AssemblyName System.Drawing
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-login-diag.log"
$Shot="$Root\diag-shots"; if(-not(Test-Path $Shot)){ New-Item -ItemType Directory $Shot|Out-Null }
$env:MAPLEFORGE_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Shot([IntPtr]$h,[string]$tag){ [WL+R]$r=New-Object 'WL+R'; [void][WL]::GetWindowRect($h,[ref]$r); $w=$r.Ri-$r.L; $ht=$r.B-$r.T; if($w -le 0 -or $ht -le 0){return}; $b=New-Object System.Drawing.Bitmap($w,$ht); $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size($w,$ht))); $b.Save("$Shot\login-$tag.png",[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose(); Write-Host "  shot login-$tag ($($w)x$ht)" }
function SrvHits([string]$pat){ if(Test-Path $Log){ return (Select-String -Path $Log -Pattern $pat -AllMatches -EA SilentlyContinue).Matches.Count } return 0 }
Get-Process MapleStory,windower_host,Maple.Host.Login -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1
$srv=$null;$wh=$null;$cli=$null
try{
    & "$Root\tools\seed-test-data.ps1" -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" 2>&1 | Out-Null
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru
    Start-Sleep 5
    $wh=Start-Process -FilePath $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
    $cli=Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    Write-Host "client PID=$($cli.Id)"
    # wait launcher then click Play!
    $h=[IntPtr]::Zero
    for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WL]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WL]::Width($h) -ge 600){ break } }
    Start-Sleep 5; if($h -ne [IntPtr]::Zero){ [WL]::Click($h,0.5,0.965); Write-Host "clicked Play!" }
    # wait for game window (>=780) = login screen
    $g=[IntPtr]::Zero
    for($i=0;$i -lt 30;$i++){ Start-Sleep 1; $g=[WL]::FindByPid([uint]$cli.Id); if($g -ne [IntPtr]::Zero -and [WL]::Width($g) -ge 780){ Write-Host "game window w=$([WL]::Width($g)) t=$i"; break } }
    if($g -eq [IntPtr]::Zero -or [WL]::Width($g) -lt 780){ Write-Host "game window 沒到 780，目前 w=$(if($g -ne [IntPtr]::Zero){[WL]::Width($g)}else{'none'})" }
    Shot $g "1-splash"
    # 等遊戲跑完開場 splash 到登入畫面（server 收到 0x17 心跳＝登入畫面就緒）
    Write-Host "  等 0x17(登入畫面就緒)..."
    for($i=0;$i -lt 40;$i++){ Start-Sleep 1; if((SrvHits 'opcode=0x17') -gt 0){ Write-Host "  ✓ 0x17 t=$i (登入畫面就緒)"; break } }
    Start-Sleep -Seconds 2
    $g=[WL]::FindByPid([uint]$cli.Id); Shot $g "2-loginscreen"
    [WL]::ShowWindow($g,9)|Out-Null; [WL]::SetForegroundWindow($g)|Out-Null; Start-Sleep -Milliseconds 800
    # 精準點欄位：帳號(0.57,0.35) 密碼(0.57,0.40) 登入按鈕(0.74,0.37)。先清空(Backspace×15)再打，避免記憶帳號 append。
    [WL]::Click($g,0.57,0.35); Start-Sleep -Milliseconds 400; for($k=0;$k -lt 15;$k++){ [WL]::Press(0x08) }; [WL]::Type("testuser"); Start-Sleep -Milliseconds 300; Shot $g "3-id"
    [WL]::Click($g,0.57,0.40); Start-Sleep -Milliseconds 400; for($k=0;$k -lt 15;$k++){ [WL]::Press(0x08) }; [WL]::Type("test1234"); Start-Sleep -Milliseconds 300; Shot $g "4-pw"
    [WL]::Click($g,0.74,0.37); Start-Sleep -Milliseconds 2500; Shot $g "5-after-login-btn"
    # fallback：若按鈕沒中，再按一次 ENTER
    [WL]::Press([WL]::ENTER); Start-Sleep -Milliseconds 2000; Shot $g "6-after-enter"
    Write-Host "=== server log 登入訊號 ==="
    Write-Host ("  握手={0} 0x17={1} 其他opcode={2}" -f (SrvHits '握手送出'),(SrvHits 'opcode=0x17'),(SrvHits 'opcode=0x[0-9a-fA-F]'))
    if(Test-Path $Log){ Get-Content $Log | Select-String -Pattern "opcode=0x|登入|LOGIN|PASSWORD|AUTH|CHANNEL" | Select-Object -Last 10 | %{ Write-Host "  SRV: $($_.Line)" } }
}
finally{
    if($cli){ $cli|Stop-Process -Force -EA SilentlyContinue }
    if($wh){ $wh|Stop-Process -Force -EA SilentlyContinue }
    if($srv){ $srv|Stop-Process -Force -EA SilentlyContinue }
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
    if($now -and $origRes -and $now -ne $origRes){ [WL]::RestoreDisplay()|Out-Null }
    Write-Host "done (res $origRes)"
}
