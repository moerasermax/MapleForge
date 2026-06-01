# A/B test: does the client complete handshake + reach login WITHOUT windower injected?
# If yes (server sees c2s/opcode traffic), windower's D3D detour is the cause of "無法登入伺服器".
# Server-side capture on (MAPLEFORGE_CAPTURE=1); NO windower_host. Display-restore safety included.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W3 {
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
$Log="$Root\live-ab.log"
$env:MAPLEFORGE_CAPTURE="1"
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
Write-Host "origRes=$origRes  (NO windower this run)"
$srv=$null;$cli=$null
try{
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru
    Start-Sleep 5
    $cli=Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    Write-Host "client PID=$($cli.Id)"
    $h=[IntPtr]::Zero
    for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[W3]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [W3]::Width($h) -ge 600){ Write-Host "launcher 636 t=$i"; break } }
    Start-Sleep 5
    if($h -ne [IntPtr]::Zero){ [W3]::Click($h,0.5,0.965); Write-Host "clicked Play! (no windower)" }
    Write-Host "waiting 30s for game to attempt full connect/login..."
    Start-Sleep 30
    Write-Host "=== analysis (NO windower) ==="
    if(Test-Path $Log){
        $accept = (Select-String -Path $Log -Pattern "接受連線" -AllMatches).Matches.Count
        $hello  = (Select-String -Path $Log -Pattern "握手送出" -AllMatches).Matches.Count
        $reset  = (Select-String -Path $Log -Pattern "10054|10053|強制關閉|中止" -AllMatches).Matches.Count
        $opc    = (Select-String -Path $Log -Pattern "opcode" -AllMatches).Matches.Count
        Write-Host ("  接受連線={0}  握手送出={1}  斷線(10054/53)={2}  opcode行={3}" -f $accept,$hello,$reset,$opc)
        if($opc -gt 0){ Write-Host "  => 客戶端越過 getHello、有送 c2s（握手成功，windower 才是 blocker）" -ForegroundColor Green }
        elseif($hello -gt 0 -and $reset -ge $hello){ Write-Host "  => 收 getHello 後即斷線（握手本身就被拒，windower 無關）" -ForegroundColor Yellow }
        else { Write-Host "  => 不確定（連線數不足）" -ForegroundColor DarkYellow }
        Write-Host "  --- opcode 行 ---"; Select-String -Path $Log -Pattern "opcode" -EA SilentlyContinue | Select -First 6 | %{ Write-Host "    $($_.Line)" }
    }
}
finally{
    if($cli){ $cli|Stop-Process -Force -EA SilentlyContinue }
    if($srv){ $srv|Stop-Process -Force -EA SilentlyContinue }
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
    if($now -and $origRes -and $now -ne $origRes){ Write-Host "restore display $origRes <- $now"; [W3]::RestoreDisplay()|Out-Null; Start-Sleep 1 } else { Write-Host "res $origRes" }
    Write-Host "done"
}
