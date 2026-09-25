<#
    Developer helper: builds a preview copy that does NOT request administrator rights,
    so the window can be opened while working on the interface.
    The two repair buttons stay disabled in this build and nothing is modified.
    Usage: powershell -ExecutionPolicy Bypass -File .\preview-ui.ps1

    This file is intentionally ASCII-only (see build.ps1 for the reason).
#>
param(
    [string]$OutputDirectory = "$PSScriptRoot\build\ui-preview",
    # A dedicated configuration makes sure the merged manifest is regenerated
    # instead of reusing the administrator one from a previous build.
    [string]$Configuration = "PreviewDebug"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifest = "$root\src\PrinterShareFixer.App\app.asinvoker.manifest"

Write-Host "== Building UI preview (no administrator manifest) ==" -ForegroundColor Cyan
dotnet publish "$root\src\PrinterShareFixer.App\PrinterShareFixer.App.csproj" `
    -c $Configuration `
    -r win-x64 `
    -p:ApplicationManifest="$manifest" `
    -p:WindowsAppSDKSelfContained=true `
    -p:Platform=x64 `
    -o $OutputDirectory

Write-Host ""
Write-Host "Preview app: $OutputDirectory\PrinterShareFixer.exe" -ForegroundColor Green
Write-Host "Note: this build never performs repairs; it is only for looking at the UI." -ForegroundColor Yellow
