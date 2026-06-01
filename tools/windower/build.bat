@echo off
REM MapleForge Windower build (x86 MSVC)
setlocal

set "VCVARS=C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvarsall.bat"
set "OUT=%~dp0bin"

if not exist "%OUT%" mkdir "%OUT%"

echo [Build] Init x86 MSVC env...
call "%VCVARS%" x86

echo [Build] Build windower.dll...
cl /nologo /LD /O2 /W3 ^
   /TP /utf-8 ^
   /DWIN32 /D_WINDOWS /DNDEBUG ^
   "%~dp0windower.cpp" ^
   /Fe"%OUT%\windower.dll" ^
   /link /SUBSYSTEM:WINDOWS ^
   kernel32.lib user32.lib ws2_32.lib

if %ERRORLEVEL% NEQ 0 (
    echo [Build] windower.dll failed
    exit /b 1
)
echo [Build] windower.dll OK

echo [Build] Build windower_host.exe...
cl /nologo /O2 /W3 ^
   /TP /utf-8 ^
   /DWIN32 /D_WINDOWS ^
   "%~dp0host.cpp" ^
   /Fe"%OUT%\windower_host.exe" ^
   /link /SUBSYSTEM:WINDOWS ^
   kernel32.lib user32.lib

if %ERRORLEVEL% NEQ 0 (
    echo [Build] windower_host.exe failed
    exit /b 1
)
echo [Build] windower_host.exe OK

echo [Build] Done. Output: %OUT%\
dir "%OUT%"
