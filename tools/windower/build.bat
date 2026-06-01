@echo off
REM MapleForge Windower 編譯腳本（x86 MSVC）
setlocal

set VCVARS="C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvarsall.bat"
set OUT=%~dp0bin

if not exist "%OUT%" mkdir "%OUT%"

echo [Build] 初始化 x86 MSVC 環境...
call %VCVARS% x86

echo [Build] 編譯 windower.dll...
cl /nologo /LD /O2 /W3 ^
   /DWIN32 /D_WINDOWS /DNDEBUG ^
   "%~dp0windower.cpp" ^
   /Fe"%OUT%\windower.dll" ^
   /link /SUBSYSTEM:WINDOWS ^
   kernel32.lib user32.lib d3d8.lib

if %ERRORLEVEL% NEq 0 (
    echo [Build] windower.dll 編譯失敗！
    exit /b 1
)
echo [Build] windower.dll OK

echo [Build] 編譯 windower_host.exe...
cl /nologo /O2 /W3 ^
   /DWIN32 /D_CONSOLE ^
   "%~dp0host.cpp" ^
   /Fe"%OUT%\windower_host.exe" ^
   /link /SUBSYSTEM:CONSOLE ^
   kernel32.lib user32.lib

if %ERRORLEVEL% NEq 0 (
    echo [Build] windower_host.exe 編譯失敗！
    exit /b 1
)
echo [Build] windower_host.exe OK

echo [Build] 完成！輸出在 %OUT%\
dir "%OUT%"
