# [視覺最後一哩] 延續 diag11 登入流程，登入後續點：世界(雪吉拉)→頻道→選角 TestHero→進地圖。
# 每步截圖到 diag-shots\enter-*.png，靠看圖逐次逼近座標。跑時機器淨空、勿搶前景焦點。
# 續點座標（808x631 主窗的比例）— 首跑為估值，看截圖後逐畫面修正：
$WorldXY = @(0.16, 0.28)   # 伺服器「雪吉拉」木牌(使用者:直接單擊雪吉拉即可)
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
    [DllImport("user32.dll")] public static extern short GetKeyState(int k);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(IntPtr dm, int f);
    // 注入大小寫敏感密碼前確保 CAPS LOCK 關閉(否則 test1234→TEST1234→密碼不符)
    public static bool ClearCapsLock(){ bool was=(GetKeyState(0x14)&1)!=0; if(was){ keybd_event(0x14,0x45,0,IntPtr.Zero); Thread.Sleep(40); keybd_event(0x14,0x45,2,IntPtr.Zero); Thread.Sleep(60); } return was; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
    public static IntPtr FindByPid(uint pid){ IntPtr f=IntPtr.Zero; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid){f=h;return false;} return true;},IntPtr.Zero); return f; }
    public static string ClassOf(IntPtr h){ var sb=new System.Text.StringBuilder(128); GetClassName(h,sb,128); return sb.ToString(); }
    public static IntPtr FindLargestByClass(uint pid,string cls){ IntPtr best=IntPtr.Zero; int bw=-1; EnumWindows((h,_)=>{ if(!IsWindowVisible(h))return true; uint p; GetWindowThreadProcessId(h,out p); if(p==pid && ClassOf(h)==cls){ int w=Width(h); if(w>bw){bw=w;best=h;} } return true;},IntPtr.Zero); return best; }
    public static string RectStr(IntPtr h){ R r; GetWindowRect(h,out r); return string.Format("L={0} T={1} W={2} H={3}", r.L,r.T,r.Ri-r.L,r.B-r.T); }
    public static int Width(IntPtr h){ R r; GetWindowRect(h,out r); return r.Ri-r.L; }
    public static void Click(IntPtr h,double fx,double fy){ R r; GetWindowRect(h,out r); int x=r.L+(int)((r.Ri-r.L)*fx); int y=r.T+(int)((r.B-r.T)*fy); SetForegroundWindow(h); Thread.Sleep(250); SetCursorPos(x,y); Thread.Sleep(120); mouse_event(0x0002,0,0,0,IntPtr.Zero); Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); Thread.Sleep(200); }
    public static int RestoreDisplay(){ return ChangeDisplaySettings(IntPtr.Zero,0); }
}
"@
Add-Type -AssemblyName System.Drawing
$Root=(Resolve-Path "$PSScriptRoot\..\..").Path; $CliDir="D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"; $CliExe="$CliDir\MapleStory.exe"
$WHost="$Root\tools\windower\bin\windower_host.exe"; $Log="$Root\live-enter-diag.log"; $Shot="$Root\diag-shots"
$KbdFile="$Root\tools\windower\captures\kbd.txt"
$env:MAPLEFORGE_WINDOWER_CAPTURE="1"; $env:MAPLEFORGE_WINDOWER_CAPTURE_DIR="$Root\tools\windower\captures"; $env:MAPLEFORGE_CAPTURE="1"
$env:Persistence__Provider = 'LiteDb'
$env:MAPLEFORGE_WINDOWER_KBD_INJECT="1"; $env:MAPLEFORGE_WINDOWER_KBD_FILE=$KbdFile; $env:MAPLEFORGE_WINDOWER_KBD_DEBUG="1"
Set-Content -Path $KbdFile -Value "" -NoNewline -Encoding ASCII
$origRes=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"})
function Shot([IntPtr]$h,[string]$t){ [WJ+R]$r=New-Object 'WJ+R'; [void][WJ]::GetWindowRect($h,[ref]$r); $w=$r.Ri-$r.L;$ht=$r.B-$r.T; if($w-le 0){return}; $b=New-Object System.Drawing.Bitmap($w,$ht);$gg=[System.Drawing.Graphics]::FromImage($b);$gg.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size($w,$ht)));$b.Save("$Shot\enter-$t.png",[System.Drawing.Imaging.ImageFormat]::Png);$gg.Dispose();$b.Dispose(); Write-Host "  shot enter-$t" }
function Hits([string]$p){ if(Test-Path $Log){return (Select-String -Path $Log -Pattern $p -AllMatches -EA SilentlyContinue).Matches.Count} return 0 }
function InjectType([string]$s){ Set-Content -Path $KbdFile -Value $s -NoNewline -Encoding ASCII; Start-Sleep -Seconds 3 }
function ClickXY([IntPtr]$h,[double[]]$xy,[string]$tag){ [WJ]::Click($h,$xy[0],$xy[1]); Write-Host ("  click ({0},{1})" -f $xy[0],$xy[1]); Start-Sleep -Milliseconds 2500; Shot $h $tag }
function DblXY([IntPtr]$h,[double[]]$xy,[string]$tag){ [WJ]::Click($h,$xy[0],$xy[1]); Start-Sleep -Milliseconds 120; [WJ]::Click($h,$xy[0],$xy[1]); Write-Host ("  dblclick ({0},{1})" -f $xy[0],$xy[1]); Start-Sleep -Milliseconds 2500; Shot $h $tag }
function SrvHit([string]$p){ if(Test-Path $Log){ return (Select-String -Path $Log -Pattern $p -EA SilentlyContinue).Count } return 0 }
Get-Process MapleStory,windower_host,Maple.Host.Login -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue; Start-Sleep 1
$srv=$null;$wh=$null;$cli=$null
try{
    & "$Root\tools\seed-test-data.ps1" -Root $Root -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" 2>&1|Out-Null
    $srv=Start-Process dotnet -ArgumentList "run --project src/Maple.Host.Login/Maple.Host.Login.csproj --no-build" -WorkingDirectory $Root -RedirectStandardOutput $Log -NoNewWindow -PassThru; Start-Sleep 5
    $wh=Start-Process $WHost -PassThru -WindowStyle Minimized; Start-Sleep 1
    $cli=Start-Process $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
    $h=[IntPtr]::Zero; for($i=0;$i -lt 25;$i++){ Start-Sleep 1; $h=[WJ]::FindByPid([uint]$cli.Id); if($h -ne [IntPtr]::Zero -and [WJ]::Width($h) -ge 600){break} }
    Start-Sleep 5; if($h -ne [IntPtr]::Zero){ [WJ]::Click($h,0.5,0.965); Write-Host "Play!" }
    $g=[IntPtr]::Zero; for($i=0;$i -lt 45;$i++){ Start-Sleep 1; $g=[WJ]::FindLargestByClass([uint]$cli.Id,"MapleStoryClass"); if($g -ne [IntPtr]::Zero -and [WJ]::Width($g) -ge 400){ Write-Host "  MapleStoryClass 主窗 hWnd=$g $([WJ]::RectStr($g))"; break } }
    if($g -eq [IntPtr]::Zero){ Write-Host "  !! 逾時未見 MapleStoryClass" -ForegroundColor Red; return }
    Write-Host "等 0x17(握手)..."; for($i=0;$i -lt 40;$i++){ Start-Sleep 1; if((Hits 'opcode=0x17') -gt 0){ break } }
    # ── 登入（自我修復：驗 server「登入成功」，沒中就重試整個登入，最多 3 次）──
    $loggedIn=$false
    for($try=1; $try -le 3 -and -not $loggedIn; $try++){
        Write-Host "--- 登入嘗試 $try ---"
        $wasCaps=[WJ]::ClearCapsLock(); if($wasCaps){ Write-Host "  (CAPS LOCK 原為 ON,已關閉)" -ForegroundColor Yellow }
        [WJ]::ShowWindow($g,9)|Out-Null; [WJ]::SetForegroundWindow($g)|Out-Null; Start-Sleep -Milliseconds 600
        [WJ]::Click($g,0.56,0.45); Start-Sleep -Milliseconds 400      # 焦點到帳號框
        InjectType ("`b" * 14); InjectType "testuser"; InjectType "`t"; InjectType ("`b" * 14); InjectType "test1234"
        [WJ]::Click($g,0.78,0.46)                                      # 登入鈕
        for($w=0;$w -lt 6;$w++){ Start-Sleep 1; if((SrvHit '登入成功') -gt 0){ $loggedIn=$true; break } }
        Write-Host ("  登入嘗試 $try → loggedIn=$loggedIn")
    }
    Shot $g "4-login-clicked"
    if(-not $loggedIn){ Write-Host "  !! 3 次都未登入成功，後續續點略過" -ForegroundColor Red }
    # ── 視覺續點：世界 → 頻道 → 選角 → 進圖（每步截圖）──
    Start-Sleep 2; Shot $g "5-worldselect"
    # 單擊「雪吉拉」→ 跳出頻道選擇面板(已驗)
    Write-Host "--- 單擊伺服器(雪吉拉) ---"; ClickXY $g $WorldXY "6-after-server"
    Start-Sleep 1; Shot $g "7-channelpanel"
    # 自適應選頻道(團隊綜整:每步動作後驗 server CHARLIST 訊號=到達選角,再決定下一步)
    # 修正:CH.1 在 y≈0.50(舊 0.55 落在第1/2列空隙);CH.1≈(0.32,0.50)、確認鈕≈(0.68,0.42)
    $reached=$false
    Write-Host "--- ① 單擊 CH.1 (0.32,0.50) ---"; [WJ]::Click($g,0.32,0.50); Start-Sleep 2; Shot $g "8a-ch1-single"; $reached=(SrvHit 'CHARLIST') -gt 0
    if(-not $reached){ Write-Host "--- ② 按「前往選擇的伺服器」(0.68,0.42) ---"; [WJ]::Click($g,0.68,0.42); Start-Sleep 2; Shot $g "8b-gobtn"; $reached=(SrvHit 'CHARLIST') -gt 0 }
    if(-not $reached){ Write-Host "--- ③ 雙擊 CH.1 備援 (0.32,0.50) ---"; [WJ]::Click($g,0.32,0.50); Start-Sleep -Milliseconds 120; [WJ]::Click($g,0.32,0.50); Start-Sleep 2; Shot $g "8c-ch1-double"; $reached=(SrvHit 'CHARLIST') -gt 0 }
    Write-Host ("  到達選角(CHARLIST)={0}" -f $reached)
    Start-Sleep 2; Shot $g "9-charselect"
    # ── 選角→進地圖(團隊綜整:單擊角色→按右側木牌「選擇角色」鈕;雙擊角色備援)──
    # ⚠避開 新建角色(0.73,0.38)/刪除角色(0.74,0.45);「選擇角色」鈕在木牌頂行(0.74,0.31)
    if($reached){
        Write-Host "--- 單擊角色 TestHero (0.31,0.56) ---"; [WJ]::Click($g,0.31,0.56); Start-Sleep 1; Shot $g "10a-char-selected"
        Write-Host "--- 按「選擇角色」鈕 (0.74,0.31) ---"; [WJ]::Click($g,0.74,0.31); Start-Sleep 3; Shot $g "10b-after-selectchar"
        $inGame = (SrvHit 'SERVER_IP|PLAYER_LOGGEDIN|進入地圖') -gt 0
        if(-not $inGame){ Write-Host "--- 備援:雙擊角色 ---"; [WJ]::Click($g,0.31,0.56); Start-Sleep -Milliseconds 120; [WJ]::Click($g,0.31,0.56); Start-Sleep 3; Shot $g "10c-dblchar"; $inGame=(SrvHit 'SERVER_IP|PLAYER_LOGGEDIN|進入地圖') -gt 0 }
        # 進圖後「不馬上殺」,連續觀察客戶端是否續留地圖(每4s×6=~24s),enum any-class 視窗截 render
        Write-Host ("  進遊戲(SERVER_IP/PLAYER_LOGGEDIN/進入地圖)={0}" -f $inGame)
        for($k=1;$k -le 6;$k++){
            Start-Sleep 4
            $alive = (Get-Process -Id $cli.Id -EA SilentlyContinue) -ne $null
            $leftMap = (SrvHit '離開地圖') -gt 0
            $w2 = if($alive){ [WJ]::FindByPid([uint]$cli.Id) } else { [IntPtr]::Zero }
            $cls = if($w2 -ne [IntPtr]::Zero){ [WJ]::ClassOf($w2) } else { "(無可見窗)" }
            Write-Host ("  [t+{0}s] client存活={1} channel離開={2} 窗class={3} {4}" -f ($k*4),$alive,$leftMap,$cls,$(if($w2 -ne [IntPtr]::Zero){[WJ]::RectStr($w2)}else{""}))
            if($w2 -ne [IntPtr]::Zero -and [WJ]::Width($w2) -ge 200){ [WJ]::SetForegroundWindow($w2)|Out-Null; Start-Sleep -Milliseconds 400; Shot $w2 ("map-{0}" -f $k) }
            if(-not $alive){ Write-Host "  → 客戶端進程已關閉(非我kill,是自行結束/崩潰)" -ForegroundColor Red; break }
            if($leftMap){ Write-Host "  → channel 已自行斷線(離開地圖,非我kill)" -ForegroundColor Red; break }
        }
    }
    Write-Host "`n=== server channel 端訊號 ==="
    if(Test-Path $Log){ Get-Content $Log | Select-String -Pattern "CHARLIST|SERVER_IP|PLAYER_LOGGEDIN|SET_FIELD|進入地圖|TestHero|角色" | Select-Object -Last 12 | %{ Write-Host "  SRV: $($_.Line)" } }
}
finally{
    if($cli){$cli|Stop-Process -Force -EA SilentlyContinue}; if($wh){$wh|Stop-Process -Force -EA SilentlyContinue}; if($srv){$srv|Stop-Process -Force -EA SilentlyContinue}
    Get-Process -Name "Maple.Host.Login" -EA SilentlyContinue|Stop-Process -Force -EA SilentlyContinue
    $now=(Get-CimInstance Win32_VideoController|?{$_.CurrentHorizontalResolution}|Select -First 1|%{"$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"}); if($now -ne $origRes){ [WJ]::RestoreDisplay()|Out-Null }
    Write-Host "done"
}
