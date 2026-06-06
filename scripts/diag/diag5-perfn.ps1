# Per-function isolation: with clean hotpatch mechanism, enable ONE winsock hook at a time
# (D3D disabled) to find which hook's LOGIC breaks the client login.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WP {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
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
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); System.Threading.Thread.Sleep(300); SetCursorPos(x,y); System.Threading.Thread.Sleep(150); mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(0x0004,0,0,0,IntPtr.Zero); }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Clear-Stale { Get-Process MapleStory,windower_host -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1 }

function Run-Fn([string]$fn){
    Write-Host "`n######## HOOKS = $fn ########" -ForegroundColor Magenta
    Clear-Stale
    $env:MAPLEFORGE_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"
    $env:MAPLEFORGE_WINDOWER_DISABLE_D3D="1"
    Remove-Item Env:\MAPLEFORGE_WINDOWER_DISABLE_WINSOCK -EA SilentlyContinue
    Set-Item Env:\MAPLEFORGE_WINDOWER_HOOKS -Value $fn
    $Log="$Root\live-fn-$fn.log"
    $srv=$null;$wh=$null;$cli=$null
    try{
        $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru
        Start-Sleep 5
        $wh=Start-Process -FilePath $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
        $cli=Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
        $h=[IntPtr]::Zero
        for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WP]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WP]::Width($h) -ge 600){ break } }
        Start-Sleep 5
        if($h -ne [IntPtr]::Zero){ [WP]::Click($h,0.5,0.965) }
        Start-Sleep 26
        $reset=(Select-String -Path $Log -Pattern "10054|10053|強制關閉|中止" -AllMatches -EA SilentlyContinue).Matches.Count
        $opc  =(Select-String -Path $Log -Pattern "opcode" -AllMatches -EA SilentlyContinue).Matches.Count
        $v = if($opc -gt 0 -and $reset -eq 0){"OK"} elseif($reset -ge 1){"BROKEN"} else {"INCONCL"}
        Write-Host ("  [{0}] 斷線={1} opcode={2} => {3}" -f $fn,$reset,$opc,$v) -ForegroundColor Cyan
        return [pscustomobject]@{ hook=$fn; reset=$reset; opcode=$opc; verdict=$v }
    } finally {
        if($cli){ $cli|Stop-Process -Force -EA SilentlyContinue }
        if($wh){ $wh|Stop-Process -Force -EA SilentlyContinue }
        if($srv){ $srv|Stop-Process -Force -EA SilentlyContinue }
        Clear-Stale
    }
}
$res=@()
try{
    foreach($fn in "recv","WSARecv","send","WSASend","WSAGetOverlappedResult","GetQueuedCompletionStatus"){ $res += Run-Fn $fn }
}
finally{
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
    if($now -and $origRes -and $now -ne $origRes){ [WP]::RestoreDisplay()|Out-Null }
    Write-Host "`n===== PER-FUNCTION SUMMARY =====" -ForegroundColor Green
    $res | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "res $origRes ; done"
}
