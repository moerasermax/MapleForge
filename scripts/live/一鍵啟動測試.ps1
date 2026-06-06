# MapleForge 一鍵啟動測試（server LiteDb + windower 視窗化 + client）
# 用法：
#   雙擊 ▶開始測試.bat              → 全部啟動，留著給你手動測
#   雙擊 ■結束測試.bat              → 全部關閉
#   或：pwsh -File 一鍵啟動測試.ps1 [-Stop] [-Build]
param(
    [switch]$Stop,    # 關閉所有（server/client/windower）
    [switch]$Build    # 啟動前先重新編譯 server（改過程式碼才需要）
)
$ErrorActionPreference = 'Stop'

$Root     = (Resolve-Path "$PSScriptRoot\..\..").Path
$CliDir   = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$CliExe   = Join-Path $CliDir "MapleStory.exe"
$Windower = Join-Path $Root "tools\windower\bin\windower_host.exe"
$HostProj = Join-Path $Root "src\Maple.Host.Login\Maple.Host.Login.csproj"
$HostDll  = Join-Path $Root "src\Maple.Host.Login\bin\Debug\net10.0\Maple.Host.Login.dll"
$Log      = Join-Path $Root "live.log"
$ErrLog   = Join-Path $Root "live.err.log"
$PidDir   = $Root

function Clear-StaleHosts {
    Write-Host "[0] 清理殘留 server/test host（只殺本專案）..." -ForegroundColor Cyan
    $n = 0
    Get-Process -Name "Maple.Host.Login","Maple.Host.Channel" -EA SilentlyContinue | ForEach-Object { $_ | Stop-Process -Force -EA SilentlyContinue; $n++ }
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -EA SilentlyContinue |
        Where-Object { $_.CommandLine -and ($_.CommandLine -match 'Maple\.Host\.Login' -or $_.CommandLine -match 'Maple\.Host\.Channel' -or ($_.CommandLine -match 'vstest|testhost' -and $_.CommandLine -match 'MapleForge')) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue; $n++ }
    foreach ($p in 8484,8585) {
        $c = Get-NetTCPConnection -LocalPort $p -State Listen -EA SilentlyContinue
        if ($c) { Stop-Process -Id ($c.OwningProcess | Select-Object -First 1) -Force -EA SilentlyContinue; $n++ }
    }
    Write-Host ("    " + $(if($n -eq 0){'乾淨，無殘留。'}else{"清掉 $n 個。"}))
    if ($n -gt 0) { Start-Sleep -Milliseconds 600 }
}

# ── 關閉模式 ──────────────────────────────────────────────────────────────────
if ($Stop) {
    Write-Host "=== 結束測試：關閉 server / client / windower ===" -ForegroundColor Yellow
    Clear-StaleHosts
    # client + windower（用 .pid 優先，找不到就按進程名）
    foreach ($f in @(".live-client.pid",".live-windower.pid")) {
        $pf = Join-Path $PidDir $f
        if (Test-Path $pf) { Get-Content $pf | ForEach-Object { Stop-Process -Id ([int]$_) -Force -EA SilentlyContinue }; Remove-Item $pf -Force -EA SilentlyContinue }
    }
    Get-Process -Name "MapleStory","windower_host" -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
    Remove-Item (Join-Path $PidDir ".live-server.pid") -Force -EA SilentlyContinue
    Write-Host "已全部關閉 ✓" -ForegroundColor Green
    return
}

# ── 啟動模式 ──────────────────────────────────────────────────────────────────
Write-Host "=== MapleForge 一鍵啟動測試 ===" -ForegroundColor Cyan
Clear-StaleHosts

# [1] 視需要編譯（首次或加 -Build）
if ($Build -or -not (Test-Path $HostDll)) {
    Write-Host "[1] 編譯 server..." -ForegroundColor Cyan
    $env:MSBUILDDISABLENODEREUSE = '1'
    dotnet build $HostProj -v quiet --nologo -p:UseSharedCompilation=false -nr:false
    if ($LASTEXITCODE -ne 0) { throw "server 編譯失敗" }
} else { Write-Host "[1] server 已編譯（要套用新程式碼請加 -Build）" -ForegroundColor DarkGray }

# [2] 種測試資料（testuser/TestHero，upsert；必須在 server 開 DB 前）
$Seed = Join-Path $Root "tools\seed-test-data.ps1"
if (Test-Path $Seed) {
    Write-Host "[2] 種測試資料 testuser/TestHero..." -ForegroundColor Cyan
    try { & $Seed -AccountName "testuser" -Password "test1234" -CharacterName "TestHero" | Out-Null; Write-Host "    OK" -ForegroundColor DarkGray }
    catch { Write-Host "    種子失敗（不致命，沿用現有 DB）：$_" -ForegroundColor DarkYellow }
}

# [3] 啟動 server（LiteDb provider；本機無 mongod 必須）
Write-Host "[3] 啟動 server（Persistence=LiteDb）..." -ForegroundColor Cyan
if (Test-Path $Log) { Remove-Item $Log -Force -EA SilentlyContinue }
$env:Persistence__Provider = 'LiteDb'
$srv = Start-Process dotnet -ArgumentList "run --project src\Maple.Host.Login\Maple.Host.Login.csproj --no-build" `
    -WorkingDirectory $Root -RedirectStandardOutput $Log -RedirectStandardError $ErrLog -NoNewWindow -PassThru
$srv.Id | Out-File (Join-Path $PidDir ".live-server.pid") -Encoding ascii
$ok = $false
for ($i=0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if (Get-NetTCPConnection -LocalPort 8484 -State Listen -EA SilentlyContinue) { $ok = $true; break }
    if ($srv.HasExited) { break }
}
if (-not $ok) { Write-Host "    ✗ server 未在 30s 內監聽 8484（看 $Log / $ErrLog）" -ForegroundColor Red; return }
Write-Host "    ✓ server PID=$($srv.Id)，8484/8585 listening" -ForegroundColor Green

# [4] windower（視窗化 hook，須在 client 前）
if (Test-Path $Windower) {
    Write-Host "[4] 注入 windower（視窗化）..." -ForegroundColor Cyan
    $wh = Start-Process -FilePath $Windower -PassThru -WindowStyle Minimized
    Start-Sleep -Milliseconds 1200
    if ($wh.HasExited) { Write-Host "    windower 提前退出 code=$($wh.ExitCode)" -ForegroundColor DarkYellow }
    else { $wh.Id | Out-File (Join-Path $PidDir ".live-windower.pid") -Encoding ascii; Write-Host "    ✓ windower PID=$($wh.Id)" -ForegroundColor Green }
} else { Write-Host "[4] 無 windower_host.exe → client 走原生模式" -ForegroundColor DarkYellow }

# [5] client
Write-Host "[5] 啟動 client..." -ForegroundColor Cyan
$cli = Start-Process -FilePath $CliExe -ArgumentList "127.0.0.1 8484" -WorkingDirectory $CliDir -PassThru
$cli.Id | Out-File (Join-Path $PidDir ".live-client.pid") -Encoding ascii
Write-Host "    ✓ client PID=$($cli.Id)" -ForegroundColor Green

Write-Host ""
Write-Host "=== 全部就緒，開始測試 ===" -ForegroundColor Green
Write-Host "  1) 啟動器視窗點 Play!（底部中央）→ 遊戲以 MapleForge 視窗開啟"
Write-Host "  2) 進遊戲按 ESC 跳過開場"
Write-Host "  3) 帳號 testuser　密碼 test1234"
Write-Host "  4) 雪吉拉 → 頻道 → TestHero → 進地圖"
Write-Host ""
Write-Host "  server log：$Log（要我看訊號就把這檔給我）" -ForegroundColor DarkGray
Write-Host "  關閉全部：雙擊 ■結束測試.bat（或本腳本加 -Stop）" -ForegroundColor DarkGray
