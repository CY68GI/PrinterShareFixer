@echo off
setlocal

rem Friendly launcher for the "framework dependent" package.
rem Checks for the .NET 10 runtime and opens the download page when it is missing.
rem This file is intentionally ASCII-only; see the Chinese readme next to it.

set "APP=%~dp0PrinterShareFixer-win-x64-requires-dotnet\PrinterShareFixer.exe"

if not exist "%APP%" (
    echo Application not found:
    echo   %APP%
    echo Make sure you unpacked the whole archive before running this file.
    pause
    exit /b 1
)

set "RUNTIME_FOUND="
for /d %%D in ("%ProgramFiles%\dotnet\shared\Microsoft.NETCore.App\10.*") do set "RUNTIME_FOUND=1"
if not defined RUNTIME_FOUND for /d %%D in ("%ProgramFiles(x86)%\dotnet\shared\Microsoft.NETCore.App\10.*") do set "RUNTIME_FOUND=1"
if not defined RUNTIME_FOUND for /d %%D in ("%LOCALAPPDATA%\Microsoft\dotnet\shared\Microsoft.NETCore.App\10.*") do set "RUNTIME_FOUND=1"

if not defined RUNTIME_FOUND (
    echo.
    echo ============================================================
    echo  .NET 10 runtime was not found on this computer.
    echo  This slim download needs it; install it once and retry.
    echo.
    echo  Download page: https://dotnet.microsoft.com/download/dotnet/10.0
    echo  Choose ".NET Runtime 10.x" - Windows - x64.
    echo  ".NET Desktop Runtime 10.x" works too and includes it.
    echo ============================================================
    echo.
    echo Opening the download page in your browser...
    start "" "https://dotnet.microsoft.com/download/dotnet/10.0"
    echo.
    echo After installing, run this file again.
    pause
    exit /b 1
)

start "" "%APP%"
exit /b 0
