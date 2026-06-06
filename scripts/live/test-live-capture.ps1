# Packet-capture mode [1] -- live bidirectional capture wrapper.
# Sets the two capture env flags, then delegates to the mature test-live.ps1
# (full-auto: start server + inject windower + auto-click Play! + login).
# $env: changes are inherited by Start-Process children (server / windower / client),
# so windower.dll inside the client process reads the capture flag, and the server
# also runs its dual-track capture (slice 2). Uses $PSScriptRoot to avoid hardcoding
# a path with non-ASCII chars (which mis-decodes under Windows PowerShell 5.1).
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path

$env:MAPLEFORGE_CAPTURE = "1"
$env:MAPLEFORGE_WINDOWER_CAPTURE = "1"
$env:MAPLEFORGE_WINDOWER_CAPTURE_DIR = (Join-Path $Root "tools\windower\captures")

Write-Host "=== capture flags set ===" -ForegroundColor Magenta
Write-Host "  MAPLEFORGE_CAPTURE=$($env:MAPLEFORGE_CAPTURE)" -ForegroundColor DarkMagenta
Write-Host "  MAPLEFORGE_WINDOWER_CAPTURE=$($env:MAPLEFORGE_WINDOWER_CAPTURE)" -ForegroundColor DarkMagenta
Write-Host "  WINDOWER_CAPTURE_DIR=$($env:MAPLEFORGE_WINDOWER_CAPTURE_DIR)" -ForegroundColor DarkMagenta

& (Join-Path $PSScriptRoot "test-live.ps1") -Mode Auto
