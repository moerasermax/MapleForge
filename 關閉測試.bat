@echo off
chcp 65001 >nul
title MapleForge 關閉測試
where pwsh >nul 2>nul && (set "PS=pwsh") || (set "PS=powershell")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\live\一鍵啟動測試.ps1" -Stop
echo.
pause
