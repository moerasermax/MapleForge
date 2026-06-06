# [3-2] keyboard-injection method test: does the v113 client accept synthetic keyboard?
# At login screen, test 3 methods on the 密碼 field, screenshot each to see which lands text.
#   A) SendInput (current, suspected failing)   B) keybd_event (legacy)   C) PostMessage WM_CHAR
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class WK {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int cb);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char c);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(4)] public KEYBDINPUT ki; }
    public const uint KBD=1, KEYUP=2, KEYDOWN_SCAN=0x0008;
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(250); SetCursorPos(x,y); Thread.Sleep(120); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(180); }
    // A) SendInput with scancode
    public static void SendInputType(string s){ foreach(var c in s){ short v=VkKeyScan(c); ushort vk=(ushort)(v&0xFF); ushort sc=(ushort)MapVirtualKey(vk,0); var a=new INPUT[2]; a[0].type=KBD; a[0].ki.wVk=vk; a[0].ki.wScan=sc; a[1].type=KBD; a[1].ki.wVk=vk; a[1].ki.wScan=sc; a[1].ki.dwFlags=KEYUP; SendInput(2,a,Marshal.SizeOf(typeof(INPUT))); Thread.Sleep(80); } }
    // B) keybd_event legacy
    public static void KeybdType(string s){ foreach(var c in s){ short v=VkKeyScan(c); byte vk=(byte)(v&0xFF); byte sc=(byte)MapVirtualKey(vk,0); keybd_event(vk,sc,0,IntPtr.Zero); Thread.Sleep(40); keybd_event(vk,sc,2,IntPtr.Zero); Thread.Sleep(80); } }
    // C) PostMessage WM_CHAR
    public static void PostCharType(IntPtr h,string s){ foreach(var c in s){ PostMessage(h,0x0102,(IntPtr)c,IntPtr.Zero); Thread.Sleep(80); } }
    public static void Back(int n){ for(int i=0;i<n;i++){ keybd_event(0x08,0,0,IntPtr.Zero); Thread.Sleep(30); keybd_event(0x08,0,2,IntPtr.Zero); Thread.Sleep(50);} }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
Add-Type -AssemblyName System.Drawing
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-kbd-diag.log"; $Shot="$Root\diag-shots"
$env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"; $env:MAPLEFORGE_CAPTURE="1"
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Shot([IntPtr]$h,[string]$tag){ [WK+R]$r=New-Object 'WK+R'; [void][WK]::GetWindowRect($h,[ref]$r); $w=$r.Ri-$r.L;$ht=$r.B-$r.T; if($w-le 0){return}; $b=New-Object System.Drawing.Bitmap($w,$ht);$gg=[System.Drawing.Graphics]::FromImage($b);$gg.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size($w,$ht)));$b.Save("$Shot\kbd-$tag.png",[System.Drawing.Imaging.ImageFormat]::Png);$gg.Dispose();$b.Dispose(); Write-Host "  shot kbd-$tag" }
function Hits([string]$p){ if(Test-Path $Log){return (Select-String -Path $Log -Pattern $p -AllMatches -EA SilentlyContinue).Matches.Count} return 0 }
Get-Process MapleStory,windower_host,Maple.Host.Login -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1
$srv=$null;$wh=$null;$cli=$null
try{
    & "$Root\tools\seed-test-data.ps1" -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" 2>&1|Out-Null
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru; Start-Sleep 5
    $wh=Start-Process $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
    $cli=Start-Process $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    $h=[IntPtr]::Zero; for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WK]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WK]::Width($h) -ge 600){break} }
    Start-Sleep 5; if($h -ne [IntPtr]::Zero){ [WK]::Click($h,0.5,0.965); Write-Host "Play!" }
    $g=[IntPtr]::Zero; for($i=0;$i -lt 30;$i++){ Start-Sleep 1; $g=[WK]::FindByPid([uint]$cli.Id); if($g -ne [IntPtr]::Zero -and [WK]::Width($g) -ge 780){break} }
    Write-Host "等 0x17..."; for($i=0;$i -lt 40;$i++){ Start-Sleep 1; if((Hits 'opcode=0x17') -gt 0){ Write-Host "0x17 ready t=$i"; break } }
    Start-Sleep 2; [WK]::ShowWindow($g,9)|Out-Null; [WK]::SetForegroundWindow($g)|Out-Null; Start-Sleep -Milliseconds 600
    # A) SendInput(scancode) into 密碼
    [WK]::Click($g,0.57,0.40); [WK]::Back(15); [WK]::SendInputType("AAAA1111"); Start-Sleep -Milliseconds 400; Shot $g "A-sendinput"
    # B) keybd_event into 密碼
    [WK]::Click($g,0.57,0.40); [WK]::Back(15); [WK]::KeybdType("BBBB2222"); Start-Sleep -Milliseconds 400; Shot $g "B-keybd"
    # C) PostMessage WM_CHAR into 密碼
    [WK]::Click($g,0.57,0.40); [WK]::Back(15); [WK]::PostCharType($g,"CCCC3333"); Start-Sleep -Milliseconds 400; Shot $g "C-postmsg"
    Write-Host "三法都試完，看 kbd-A/B/C 截圖哪個密碼框有字"
}
finally{
    if($cli){$cli|Stop-Process -Force -EA SilentlyContinue}; if($wh){$wh|Stop-Process -Force -EA SilentlyContinue}; if($srv){$srv|Stop-Process -Force -EA SilentlyContinue}
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"}); if($now -ne $origRes){ [WK]::RestoreDisplay()|Out-Null }
    Write-Host "done"
}
