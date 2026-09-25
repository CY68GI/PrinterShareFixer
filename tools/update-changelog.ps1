<#
    Regenerates CHANGELOG.md from the release notes that the app itself shows
    (src/PrinterShareFixer.Core/AppInfo.cs). Run this after adding a release note
    so the repository changelog and the in-app "settings" dialog never drift apart.

    This file is intentionally ASCII-only.
#>
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'CHANGELOG.md'

$markdown = & dotnet run --project "$root\src\PrinterShareFixer.Cli" --no-build -- version --markdown
if ($LASTEXITCODE -ne 0) {
    throw 'psfix version --markdown failed; build the CLI first (build.ps1).'
}

$markdown | Out-File -LiteralPath $target -Encoding utf8
Write-Host "CHANGELOG.md updated: $target" -ForegroundColor Green
