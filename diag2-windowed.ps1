# Diagnostic 2: with windower injected, click Play!, then screenshot the resulting (350-wide?) window
# to see whether it is a crash dialog / frozen game / stuck init. Includes display restore safety.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W2 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int c);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h,_) => { if(!IsWindowVisible(h)) return true; uint p; GetWindowThreadProcessId(h, out p); if(p==pid){found=h;return false;} return true; }, IntPtr.Zero);
        return found;
    }
    public static string Title(IntPtr h){ var sb=new System.Text.StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }
    public static void Click(IntPtr h, double fx, double fy) {
        R r; GetWindowRect(h, out r);
        int x = r.L + (int)((r.Ri-r.L)*fx); int y = r.T + (int)((r.B-r.T)*fy);
        SetForegroundWindow(h); System.Threading.Thread.Sleep(300);
        SetCursorPos(x,y); System.Threading.Thread.Sleep(150);
        mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80);
        mouse_event(0x0004,0,0,0,IntPtr.Zero);
    }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero, 0); }
}
"@
Add-Type -AssemblyName System.Drawing

$Root   = $PSScriptRoot
$CliDir = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$CliExe = "$CliDir\MapleStory.exe"
$WHost  = "$Root\tools\windower\bin\windower_host.exe"
$ShotDir = "$Root\diag-shots"
if (-not (Test-Path $ShotDir)) { New-Item -ItemType Directory -Path $ShotDir | Out-Null }
$env:MAPLEFORGE_WINDOWER_CAPTURE = "1"
$env:MAPLEFORGE_WINDOWER_CAPTURE_DIR = "$Root\tools\windower\captures"

$origRes = (Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentHorizontalResolution } | Select-Object -First 1 | ForEach-Object { "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" })
Write-Host "origRes=$origRes"

function Shot([IntPtr]$h, [string]$tag) {
    [W2+R]$r = New-Object 'W2+R'; [void][W2]::GetWindowRect($h, [ref]$r)
    $w = $r.Ri - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { Write-Host "  [$tag] bad rect"; return }
    $bmp = New-Object System.Drawing.Bitmap($w, $ht); $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $bmp.Save("$ShotDir\$tag.png", [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
    Write-Host "  [$tag] saved (win ${w}x${ht}) title='$([W2]::Title($h))'"
}

$srv=$null;$wh=$null;$cli=$null
try {
    $srv = Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -PassThru -WindowStyle Minimized
    Start-Sleep 5
    $wh = Start-Process -FilePath $WHost -PassThru -WindowStyle Minimized
    Start-Sleep 1
    $cli = Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    Write-Host "client PID=$($cli.Id)"
    $h=[IntPtr]::Zero
    for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[W2]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero){ [W2+R]$rr=New-Object 'W2+R';[void][W2]::GetWindowRect($h,[ref]$rr); if(($rr.Ri-$rr.L) -ge 600){ Write-Host "launcher 636 at t=$i"; break } } }
    Start-Sleep 5
    if($h -ne [IntPtr]::Zero){ [W2]::Click($h,0.5,0.965); Write-Host "clicked Play!" }
    # poll the window state for 25s, screenshotting at intervals to catch the 350 state
    for($i=1;$i -le 5;$i++){
        Start-Sleep 4
        $hx=[W2]::FindByPid([uint]$cli.Id)
        if($hx -eq [IntPtr]::Zero){ Write-Host "  [t=$i] no visible window (fullscreen/transition/exited)"; continue }
        [W2+R]$rx=New-Object 'W2+R';[void][W2]::GetWindowRect($hx,[ref]$rx)
        Write-Host "  [t=$i] width=$($rx.Ri-$rx.L) title='$([W2]::Title($hx))'"
        Shot $hx ("state-{0}" -f $i)
    }
}
finally {
    if($cli){ $cli|Stop-Process -Force -EA SilentlyContinue }
    if($wh){ $wh|Stop-Process -Force -EA SilentlyContinue }
    if($srv){ $srv|Stop-Process -Force -EA SilentlyContinue }
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
    $nowRes = (Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentHorizontalResolution } | Select-Object -First 1 | ForEach-Object { "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" })
    if($nowRes -and $origRes -and $nowRes -ne $origRes){ Write-Host "restore display $origRes <- $nowRes"; [W2]::RestoreDisplay()|Out-Null }
    else { Write-Host "res unchanged $origRes" }
    Write-Host "done"
}
