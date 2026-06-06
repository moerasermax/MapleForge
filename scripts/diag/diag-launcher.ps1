# Play! launcher diagnostic: capture screenshots of the launcher window before/after a click,
# to understand the 636 -> 350 shrink and locate the real Play! button. GUI-only, has cleanup.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h,_) => { if(!IsWindowVisible(h)) return true; uint p; GetWindowThreadProcessId(h, out p); if(p==pid){found=h;return false;} return true; }, IntPtr.Zero);
        return found;
    }
    public static void Click(IntPtr h, double fx, double fy) {
        R r; GetWindowRect(h, out r);
        int x = r.L + (int)((r.Ri-r.L)*fx); int y = r.T + (int)((r.B-r.T)*fy);
        SetForegroundWindow(h); System.Threading.Thread.Sleep(300);
        SetCursorPos(x,y); System.Threading.Thread.Sleep(150);
        mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80);
        mouse_event(0x0004,0,0,0,IntPtr.Zero);
    }
}
"@
Add-Type -AssemblyName System.Drawing

$Root   = (Resolve-Path "$PSScriptRoot\..\..").Path
$CliDir = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$CliExe = "$CliDir\MapleStory.exe"
$ShotDir = "$Root\diag-shots"
if (-not (Test-Path $ShotDir)) { New-Item -ItemType Directory -Path $ShotDir | Out-Null }

function Shot([IntPtr]$h, [string]$tag) {
    [W+R]$r = New-Object 'W+R'
    [void][W]::GetWindowRect($h, [ref]$r)
    $w = $r.Ri - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { Write-Host "  [$tag] bad rect w=$w h=$ht"; return }
    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $path = "$ShotDir\$tag.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "  [$tag] saved $path  (win ${w}x${ht} at $($r.L),$($r.T))"
}

$srv = $null; $cli = $null
try {
    Write-Host "=== server ===" -ForegroundColor Cyan
    $srv = Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -PassThru -WindowStyle Minimized
    Start-Sleep 5
    Write-Host "=== client ===" -ForegroundColor Cyan
    $cli = Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    Write-Host "  client PID=$($cli.Id)"

    # wait for launcher window
    $h = [IntPtr]::Zero
    for ($i=0; $i -lt 25; $i++) {
        Start-Sleep 1
        $h = [W]::FindByPid([uint]$cli.Id)
        if ($h -ne [IntPtr]::Zero) {
            [W+R]$rr = New-Object 'W+R'; [void][W]::GetWindowRect($h, [ref]$rr)
            $w = $rr.Ri - $rr.L
            Write-Host "  win appeared hWnd=$h width=$w (t=$i s)"
            if ($w -ge 600) { break }   # launcher at 636
        }
    }
    if ($h -eq [IntPtr]::Zero) { Write-Host "  no window"; return }

    Start-Sleep 5   # let launcher fully render
    Shot $h "01-launcher-before-click"

    Write-Host "=== click bottom-center (0.5, 0.965) as current script does ===" -ForegroundColor Yellow
    [W]::Click($h, 0.5, 0.965)
    Start-Sleep 4
    $h2 = [W]::FindByPid([uint]$cli.Id)
    if ($h2 -ne [IntPtr]::Zero) {
        [W+R]$r2 = New-Object 'W+R'; [void][W]::GetWindowRect($h2, [ref]$r2)
        Write-Host "  after click width=$($r2.Ri - $r2.L)"
        Shot $h2 "02-after-click"
    }
}
finally {
    Write-Host "=== cleanup ===" -ForegroundColor Cyan
    if ($cli) { $cli | Stop-Process -Force -EA SilentlyContinue }
    if ($srv) { $srv | Stop-Process -Force -EA SilentlyContinue }
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
    Write-Host "done"
}
