@echo off
chcp 65001 >nul
title MapleForge 啟動測試
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\live\一鍵啟動測試.ps1"
echo.
echo 視窗可關閉，server/client 會繼續執行。結束測試請雙擊「關閉測試.bat」。
pause
