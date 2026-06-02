# [方法A] Live 測：切英文鍵盤佈局(en-US 0x0409)繞過中文 IME 後注入登入框。
# 基於已驗證的 diag8-kbd-inject.ps1。核心觀察：windower_inject.log 的
#   kbd-layout verified en-US(non-IME)  +  WndProc probe 的 WM_KEYDOWN wParam
#   是否從 0xE5(VK_PROCESSKEY) 變成正常 VK(如 0x31/0x41) 並有 WM_CHAR。
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Threading;
public class WJ {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll",CharSet=CharSet.Ansi)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static string ClassOf(IntPtr h){ var sb=new System.Text.StringBuilder(128); GetClassName(h,sb,128); return sb.ToString(); }
    // 找該 pid 下 class=MapleStoryClass 的可見窗(真正的遊戲登入框，非 Play! launcher)。
    public static IntPtr FindByClass(uint pid,string cls){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid && ClassOf(h)==cls){f=h;return false;} return true;},IntPtr.Zero); return f; }
    // 找該 pid 下 class=cls 中 width 最大的可見窗(主遊戲畫面窗，非小子窗)。
    public static IntPtr FindLargestByClass(uint pid,string cls){ IntPtr best=IntPtr.Zero; int bw=-1; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid && ClassOf(h)==cls){ int w=Width(h); if(w>bw){bw=w;best=h;} } return true;},IntPtr.Zero); return best; }
    public static string RectStr(IntPtr h){ R r; GetWindowRect(h,out r); return string.Format("L={0} T={1} W={2} H={3}", r.L,r.T,r.Ri-r.L,r.B-r.T); }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(250); SetCursorPos(x,y); Thread.Sleep(120); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(200); }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
Add-Type -AssemblyName System.Drawing
$Root="$PSScriptRoot"; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-inject-diag.log"; $Shot="$Root\diag-shots"
$KbdFile="$Root\tools\windower\captures\kbd.txt"
$Inj="$Root\tools\windower\captures\windower_inject.log"
# 開封包擷取 + 鍵盤注入 + 全鍵盤診斷(讓 WndProc probe / kbd-layout 全記錄)。
$env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"; $env:MAPLEFORGE_CAPTURE="1"
$env:MAPLEFORGE_WINDOWER_KBD_INJECT="1"; $env:MAPLEFORGE_WINDOWER_KBD_FILE=$KbdFile
$env:MAPLEFORGE_WINDOWER_KBD_DEBUG="1"
# 方法A 預設開啟；要 A/B 對照可在外層 set MAPLEFORGE_WINDOWER_KBD_LAYOUT_EN=0
Set-Content -Path $KbdFile -Value "" -NoNewline -Encoding ASCII
# 清掉舊 inject.log，確保抓到的是本輪的行。
if(Test-Path $Inj){ Remove-Item $Inj -Force -EA SilentlyContinue }
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Shot([IntPtr]$h,[string]$t){ [WJ+R]$r=New-Object 'WJ+R'; [void][WJ]::GetWindowRect($h,[ref]$r); $w=$r.Ri-$r.L;$ht=$r.B-$r.T; if($w-le 0){return}; $b=New-Object System.Drawing.Bitmap($w,$ht);$gg=[System.Drawing.Graphics]::FromImage($b);$gg.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size($w,$ht)));$b.Save("$Shot\full-$t.png",[System.Drawing.Imaging.ImageFormat]::Png);$gg.Dispose();$b.Dispose(); Write-Host "  shot full-$t" }
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
    # 鎖定真正的遊戲登入框 class=MapleStoryClass(非 Play! launcher 的 IE 窗)。
    $g=[IntPtr]::Zero; for($i=0;$i -lt 45;$i++){ Start-Sleep 1; $g=[WJ]::FindLargestByClass([uint]$cli.Id,"MapleStoryClass"); if($g -ne [IntPtr]::Zero -and [WJ]::Width($g) -ge 400){ Write-Host "  MapleStoryClass 主窗 hWnd=$g (t=$i s) $([WJ]::RectStr($g))"; break } }
    if($g -eq [IntPtr]::Zero){
        Write-Host "  !! 逾時未見 MapleStoryClass — 遊戲登入畫面未啟動(Play! launcher 沒成功帶起遊戲)。" -ForegroundColor Red
        $any=[WJ]::FindByPid([uint]$cli.Id); if($any -ne [IntPtr]::Zero){ Write-Host ("  目前可見窗 class={0} width={1}" -f [WJ]::ClassOf($any),[WJ]::Width($any)) }
        return
    }
    Write-Host "等 0x17(握手)..."; for($i=0;$i -lt 40;$i++){ Start-Sleep 1; if((Hits 'opcode=0x17') -gt 0){ Write-Host "0x17 t=$i"; break } }
    Start-Sleep 2; [WJ]::ShowWindow($g,9)|Out-Null; [WJ]::SetForegroundWindow($g)|Out-Null; Start-Sleep -Milliseconds 600
    Write-Host ("  注入目標 class={0}" -f [WJ]::ClassOf($g))
    Shot $g "1-login"
    # 完整登入：先點帳號框注入 testuser，再點密碼框注入 test1234，最後點登入鈕。
    # windower poll kbd.txt → 注入到「當前 focus 窗」→ 清空，故每框先點再寫檔。
    # 座標由 808x631 主窗截圖疊網格精算(2026-06-02)；先前 y 偏高約 0.1 全沒點中。
    # 穩定化注入：每欄位先 Backspace 清空(建立確定起點，不靠記憶帳號反白)，
    # 帳密分次注入、Tab 獨立步驟，避免高速連發掉字/錯位。
    [WJ]::Click($g,0.56,0.45); Start-Sleep -Milliseconds 400        # 焦點到帳號框
    InjectType ("`b" * 14)                                          # 清空帳號框(記憶帳號預填)
    InjectType "testuser"; Shot $g "2-id-injected"                  # 打帳號
    InjectType "`t"                                                 # Tab 跳密碼框
    InjectType ("`b" * 14)                                          # 清空密碼框(保險)
    InjectType "test1234"; Shot $g "3-pw-injected"                  # 打密碼
    [WJ]::Click($g,0.78,0.46); Start-Sleep -Milliseconds 3000; Shot $g "4-after-login"  # 登入鈕
    Write-Host "=== 登入訊號 ==="; Write-Host ("  0x01(Login)={0} 0x17={1} 任意opcode={2}" -f (Hits 'opcode=0x01'),(Hits 'opcode=0x17'),(Hits 'opcode=0x'))
    if(Test-Path $Log){ Get-Content $Log | Select-String -Pattern "opcode=0x|登入|LOGIN|Login|帳號|密碼|認證|Auth|AuthSuccess|gender|GENDER|PIN|CHANNEL|角色|彈回" | Select-Object -Last 18 | %{ Write-Host "  SRV: $($_.Line)" } }

    Write-Host "`n=== [方法A] kbd-layout 切換結果 ===" -ForegroundColor Cyan
    if(Test-Path $Inj){ Get-Content $Inj | Select-String -Pattern "kbd-layout" | Select-Object -Last 20 | %{ Write-Host "  $($_.Line)" } } else { Write-Host "  (無 inject.log)" }

    Write-Host "`n=== WndProc probe：WM_KEYDOWN 的 wParam (核心判讀) ===" -ForegroundColor Cyan
    if(Test-Path $Inj){
        $kd = Get-Content $Inj | Select-String -Pattern "WndProc probe.*WM_KEYDOWN"
        $kd | Select-Object -Last 15 | %{ Write-Host "  $($_.Line)" }
        $bad = ($kd | Select-String -Pattern "wParam=0x000000E5").Count
        $good = ($kd | Select-String -Pattern "WM_CHAR").Count
        Write-Host ("  >> VK_PROCESSKEY(0xE5) 出現次數={0}；另有 WM_CHAR 行={1}" -f $bad, ((Get-Content $Inj|Select-String 'WndProc probe.*WM_CHAR').Count)) -ForegroundColor Yellow
        if($bad -eq 0 -and (Get-Content $Inj|Select-String 'kbd-layout verified en-US').Count -gt 0){
            Write-Host "  >> 判讀：佈局已切 en-US 且未見 0xE5 — 方法A 可能突破！比對截圖密碼框是否有字。" -ForegroundColor Green
        } elseif($bad -gt 0){
            Write-Host "  >> 判讀：仍見 0xE5 — IME 仍攔截。檢查 'kbd-layout NOT verified'，下一招上 SendMessageTimeoutW 同步化。" -ForegroundColor Red
        }
    }

    Write-Host "`n=== SendInput vk 實際送鍵 ===" -ForegroundColor Cyan
    if(Test-Path $Inj){ Get-Content $Inj | Select-String -Pattern "AttachThreadInput SendInput (vk|unicode)" | Select-Object -Last 12 | %{ Write-Host "  $($_.Line)" } }
}
finally{
    if($cli){$cli|Stop-Process -Force -EA SilentlyContinue}; if($wh){$wh|Stop-Process -Force -EA SilentlyContinue}; if($srv){$srv|Stop-Process -Force -EA SilentlyContinue}
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"}); if($now -ne $origRes){ [WJ]::RestoreDisplay()|Out-Null }
    Write-Host "done"
}
