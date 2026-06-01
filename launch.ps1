# MapleForge 一鍵啟動腳本
# 啟動順序：DxWnd（視窗化 hook）→ MapleForge server → MapleStory 客戶端

$ServerRoot = $PSScriptRoot
$ClientDir  = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client"
$DxWndExe   = Join-Path $ClientDir "視窗化\dxwnd.exe"
$ClientExe  = Join-Path $ClientDir "MapleStory.exe"
$ServerIp   = "127.0.0.1"
$LoginPort  = "8484"

Write-Host "=== MapleForge 啟動器 ===" -ForegroundColor Cyan

# 1. 啟動 DxWnd（視窗化 hook，背景執行）
if (Test-Path $DxWndExe) {
    Write-Host "[1/3] 啟動 DxWnd 視窗化 hook..." -ForegroundColor Yellow
    $dxwnd = Start-Process -FilePath $DxWndExe -WorkingDirectory (Split-Path $DxWndExe) -PassThru -WindowStyle Minimized
    Start-Sleep -Milliseconds 800   # 等 hook 安裝完成
    Write-Host "      DxWnd PID=$($dxwnd.Id) ✓" -ForegroundColor Green
} else {
    Write-Host "[1/3] DxWnd 未找到，跳過視窗化" -ForegroundColor DarkYellow
}

# 2. 啟動 MapleForge server（在新視窗中，保留 log 可觀測）
Write-Host "[2/3] 啟動 MapleForge server..." -ForegroundColor Yellow
$serverArgs = @{
    FilePath         = "dotnet"
    ArgumentList     = "run --project src\Maple.Host.Login\Maple.Host.Login.csproj --no-build"
    WorkingDirectory = $ServerRoot
    PassThru         = $true
    WindowStyle      = "Normal"
}
$server = Start-Process @serverArgs
Write-Host "      Server PID=$($server.Id) ✓" -ForegroundColor Green
Write-Host "      等待 server 啟動（3 秒）..." -ForegroundColor DarkYellow
Start-Sleep -Seconds 3

# 3. 啟動 MapleStory 客戶端
Write-Host "[3/3] 啟動 MapleStory 客戶端（$ServerIp`:$LoginPort）..." -ForegroundColor Yellow
if (Test-Path $ClientExe) {
    $client = Start-Process -FilePath $ClientExe -ArgumentList "$ServerIp $LoginPort" -WorkingDirectory $ClientDir -PassThru
    Write-Host "      Client PID=$($client.Id) ✓" -ForegroundColor Green
} else {
    Write-Host "      MapleStory.exe 未找到：$ClientExe" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== 全部啟動完成 ===" -ForegroundColor Cyan
Write-Host "  按 ESC 跳過啟動器畫面直接進遊戲" -ForegroundColor White
Write-Host "  帳號密碼：任意（autoRegister=true，首次登入自動建帳）" -ForegroundColor White
Write-Host ""
Write-Host "  關閉 server：在 server 視窗按 Ctrl+C" -ForegroundColor DarkGray
Write-Host "  關閉 DxWnd：在工作列右鍵關閉" -ForegroundColor DarkGray
Write-Host ""

# 等待 server 結束（按 Enter 可提前退出）
Write-Host "按 Enter 結束此啟動器視窗（不影響 server/client）..." -ForegroundColor DarkGray
Read-Host | Out-Null
