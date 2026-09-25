<#
    Builds the core library, the WinUI 3 app and the command line tool.
    All build output goes to the "build" folder, so "src" keeps source code only.
    Usage: powershell -ExecutionPolicy Bypass -File .\build.ps1 [-Configuration Release]

    Note: this file is intentionally ASCII-only, because Windows PowerShell 5.1
    reads .ps1 files without a BOM using the ANSI code page.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "== Building core library ==" -ForegroundColor Cyan
dotnet build "$root\src\PrinterShareFixer.Core\PrinterShareFixer.Core.csproj" -c $Configuration

Write-Host "== Building WinUI 3 app ==" -ForegroundColor Cyan
dotnet build "$root\src\PrinterShareFixer.App\PrinterShareFixer.App.csproj" -c $Configuration

Write-Host "== Building command line tool ==" -ForegroundColor Cyan
dotnet build "$root\src\PrinterShareFixer.Cli\PrinterShareFixer.Cli.csproj" -c $Configuration

$appDir = "$root\build\bin\PrinterShareFixer.App\$Configuration\net10.0-windows10.0.19041.0\win-x64"
Write-Host ""
Write-Host "Done. App: $appDir\PrinterShareFixer.exe" -ForegroundColor Green
Write-Host "The app requests administrator rights; a UAC prompt appears on launch." -ForegroundColor Yellow
Write-Host "Run .\package.ps1 to build the ready-to-copy release folder." -ForegroundColor Yellow
