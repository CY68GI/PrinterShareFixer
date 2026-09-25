<#
    Builds the ready-to-run package into the "release" folder.

    Output:
      release\PrinterShareFixer-win-x64\       runnable app (self contained)
      release\psfix-cli-win-x64\               optional command line tool
      release\<guide>.txt                      usage guide copied from the repository root
      release\PrinterShareFixer-<version>-win-x64.zip
                                               one archive with everything, copy it anywhere

    The published app is x64, self contained and includes the Windows App SDK,
    so a target computer does not need the .NET runtime or any extra installer.

    Usage:
      powershell -ExecutionPolicy Bypass -File .\package.ps1
      powershell -ExecutionPolicy Bypass -File .\package.ps1 -FrameworkDependent   # needs .NET 10 on target

    This file is intentionally ASCII-only (see build.ps1 for the reason).
#>
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = $PSScriptRoot
$releaseRoot = "$root\release"
$appDir = "$releaseRoot\PrinterShareFixer-$RuntimeIdentifier"
$cliDir = "$releaseRoot\psfix-cli-$RuntimeIdentifier"

# Version comes from the app project so the archive name always matches the binaries.
$versionMatch = [regex]::Match(
    (Get-Content "$root\src\PrinterShareFixer.App\PrinterShareFixer.App.csproj" -Raw),
    '<Version>([^<]+)</Version>')
$version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value.Trim() } else { '0.0.0' }
$packageName = "PrinterShareFixer-$version-$RuntimeIdentifier"
$archive = "$releaseRoot\$packageName.zip"

if ($FrameworkDependent) {
    $selfContained = "false"
    Write-Host "Mode: framework dependent (.NET 10 desktop runtime required on the target computer)" -ForegroundColor Yellow
} else {
    $selfContained = "true"
    Write-Host "Mode: self contained (target computer needs nothing extra)" -ForegroundColor Cyan
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

# Clean only the folders this script owns, never the release root itself.
foreach ($dir in @($appDir, $cliDir)) {
    if (Test-Path -LiteralPath $dir) {
        $resolved = (Resolve-Path -LiteralPath $dir).Path
        if (-not $resolved.StartsWith($releaseRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete outside the release folder: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

Write-Host "== Publishing WinUI 3 app ==" -ForegroundColor Cyan
dotnet publish "$root\src\PrinterShareFixer.App\PrinterShareFixer.App.csproj" `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained $selfContained `
    -p:WindowsAppSDKSelfContained=true `
    -p:Platform=x64 `
    -o $appDir

Write-Host "== Publishing command line tool ==" -ForegroundColor Cyan
dotnet publish "$root\src\PrinterShareFixer.Cli\PrinterShareFixer.Cli.csproj" `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained $selfContained `
    -o $cliDir

# Copy the usage guide (repository root .txt) into the release folder and the app folder,
# re-encoded as UTF-8 with BOM so Notepad shows the Chinese text correctly.
$guide = Get-ChildItem -Path $root -Filter '*.txt' -File | Select-Object -First 1
if ($guide) {
    $text = Get-Content -LiteralPath $guide.FullName -Raw -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $appDir $guide.Name) -Value $text -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $releaseRoot $guide.Name) -Value $text -Encoding UTF8
    Write-Host ("Guide copied: " + $guide.Name) -ForegroundColor Green
} else {
    Write-Host "No usage guide (.txt) found in the repository root." -ForegroundColor Yellow
}

if (-not $SkipArchive) {
    Write-Host "== Creating one archive with everything ==" -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    # 只保留一个压缩包：先清掉 release 根目录下旧的 .zip
    Get-ChildItem -LiteralPath $releaseRoot -Filter '*.zip' -File -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }

    $zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($mapping in @(
                @{ Source = $appDir; Prefix = "PrinterShareFixer-$RuntimeIdentifier" },
                @{ Source = $cliDir; Prefix = "psfix-cli-$RuntimeIdentifier" })) {
            $base = (Resolve-Path -LiteralPath $mapping.Source).Path.TrimEnd('\')
            $files = Get-ChildItem -LiteralPath $base -Recurse -File
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
                $entryName = "$packageName/$($mapping.Prefix)/$relative"
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }

        if ($guide) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $guide.FullName, "$packageName/$($guide.Name)",
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }

    $archiveMb = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1)
    Write-Host ("Created " + (Split-Path $archive -Leaf) + " ($archiveMb MB)") -ForegroundColor Green
}

$appFiles = (Get-ChildItem -Path $appDir -File).Count
$appSize = [math]::Round(((Get-ChildItem -Path $appDir -File | Measure-Object Length -Sum).Sum / 1MB), 1)

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "App folder : $appDir ($appFiles files, $appSize MB)"
Write-Host "CLI folder : $cliDir"
Write-Host "Archive    : $archive"
Write-Host "Unpack the archive on the target computer, then run" -ForegroundColor Yellow
Write-Host "  $packageName\PrinterShareFixer-$RuntimeIdentifier\PrinterShareFixer.exe   (UAC prompt)" -ForegroundColor Yellow
