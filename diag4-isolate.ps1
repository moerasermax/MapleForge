# Isolation harness: run windower injection in 3 modes, see which hook group breaks client login.
#   winsock : DISABLE_D3D=1      (only winsock hooks active)
#   d3d     : DISABLE_WINSOCK=1  (only D3D hook active)
#   both-off: both disabled      (control ~ no-op inject; should login like no-windower)
# Per mode: start server(capture)+windower_host+client, click Play!, wait, analyze server log.
# Signal of SUCCESS = server sees opcode (client passed getHello) AND no 10054 reset storm.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WI {
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
$Root="$PSScriptRoot"; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})

function Clear-Stale { Get-Process MapleStory,windower_host -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1 }

function Run-Mode([string]$mode,[hashtable]$flags){
    Write-Host "`n######## MODE = $mode ########" -ForegroundColor Magenta
    Clear-Stale
    $env:MAPLEFORGE_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"
    Remove-Item Env:\MAPLEFORGE_WINDOWER_DISABLE_WINSOCK -EA SilentlyContinue
    Remove-Item Env:\MAPLEFORGE_WINDOWER_DISABLE_D3D -EA SilentlyContinue
    foreach($k in $flags.Keys){ Set-Item -Path "Env:\$k" -Value $flags[$k] }
    $Log="$Root\live-iso-$mode.log"
    $srv=$null;$wh=$null;$cli=$null
    try{
        $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru
        Start-Sleep 5
        $wh=Start-Process -FilePath $WHost -PassThru -WindowStyle Minimized
        Start-Sleep 1
        $cli=Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
        $h=[IntPtr]::Zero
        for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WI]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WI]::Width($h) -ge 600){ break } }
        Start-Sleep 5
        if($h -ne [IntPtr]::Zero){ [WI]::Click($h,0.5,0.965) }
        Start-Sleep 28
        $accept=(Select-String -Path $Log -Pattern "接受連線" -AllMatches -EA SilentlyContinue).Matches.Count
        $hello =(Select-String -Path $Log -Pattern "握手送出" -AllMatches -EA SilentlyContinue).Matches.Count
        $reset =(Select-String -Path $Log -Pattern "10054|10053|強制關閉|中止" -AllMatches -EA SilentlyContinue).Matches.Count
        $opc   =(Select-String -Path $Log -Pattern "opcode" -AllMatches -EA SilentlyContinue).Matches.Count
        $verdict = if($opc -gt 0 -and $reset -eq 0){ "LOGIN OK (越過getHello, 0斷線)" } elseif($hello -gt 0 -and $reset -ge 1){ "BROKEN (getHello後即斷線)" } else { "INCONCLUSIVE" }
        Write-Host ("  [{0}] 接受連線={1} 握手={2} 斷線={3} opcode={4} => {5}" -f $mode,$accept,$hello,$reset,$opc,$verdict) -ForegroundColor Cyan
        return [pscustomobject]@{ mode=$mode; accept=$accept; hello=$hello; reset=$reset; opcode=$opc; verdict=$verdict }
    } finally {
        if($cli){ $cli|Stop-Process -Force -EA SilentlyContinue }
        if($wh){ $wh|Stop-Process -Force -EA SilentlyContinue }
        if($srv){ $srv|Stop-Process -Force -EA SilentlyContinue }
        Clear-Stale
    }
}

$results=@()
try{
    $results += Run-Mode "winsock-only" @{ MAPLEFORGE_WINDOWER_DISABLE_D3D="1" }
    $results += Run-Mode "d3d-only"     @{ MAPLEFORGE_WINDOWER_DISABLE_WINSOCK="1" }
    $results += Run-Mode "both-off"     @{ MAPLEFORGE_WINDOWER_DISABLE_WINSOCK="1"; MAPLEFORGE_WINDOWER_DISABLE_D3D="1" }
}
finally{
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
    if($now -and $origRes -and $now -ne $origRes){ Write-Host "restore display $origRes <- $now"; [WI]::RestoreDisplay()|Out-Null }
    Write-Host "`n======== ISOLATION SUMMARY ========" -ForegroundColor Green
    $results | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "res $origRes ; done"
}
