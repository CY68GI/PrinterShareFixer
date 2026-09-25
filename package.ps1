<#
    Builds the ready-to-run packages into the "release" folder.

    Two variants are produced:

      1. self-contained   (best default download)
         release\PrinterShareFixer-win-x64\                      runnable app
         release\psfix-cli-win-x64\                              command line tool
         release\PrinterShareFixer-<version>-win-x64.zip         unzip anywhere and run

      2. framework dependent  (about half the size, needs the .NET 10 runtime)
         release\PrinterShareFixer-win-x64-requires-dotnet\
         release\psfix-cli-win-x64-requires-dotnet\
         release\PrinterShareFixer-<version>-win-x64-requires-dotnet.zip

    Both variants bundle the Windows App SDK, so the only optional dependency
    is the .NET runtime.

    Layout inside every archive:
      <package>/PrinterShareFixer-win-x64[-requires-dotnet]/   the app
      <package>/psfix-cli-win-x64[-requires-dotnet]/           the CLI
      <package>/<guide>.txt                                    usage guide
      <package>/<extra>                                        only for the slim package

    Usage:
      powershell -ExecutionPolicy Bypass -File .\package.ps1
      powershell -ExecutionPolicy Bypass -File .\package.ps1 -Variant FrameworkDependent
      powershell -ExecutionPolicy Bypass -File .\package.ps1 -SkipArchive

    This file is intentionally ASCII-only: Windows PowerShell 5.1 reads .ps1
    files without a BOM using the ANSI code page, which corrupts non-ASCII text.
#>
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [ValidateSet('All', 'SelfContained', 'FrameworkDependent')]
    [string]$Variant = 'All',
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = $PSScriptRoot
$releaseRoot = "$root\release"
$docsRoot = "$root\docs"
$extraRoot = "$root\packaging"

$versionMatch = [regex]::Match(
    (Get-Content "$root\src\PrinterShareFixer.App\PrinterShareFixer.App.csproj" -Raw),
    '<Version>([^<]+)</Version>')
$version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value.Trim() } else { '0.0.0' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Remove-Safely([string]$path) {
    if (Test-Path -LiteralPath $path) {
        $resolved = (Resolve-Path -LiteralPath $path).Path
        if (-not $resolved.StartsWith($releaseRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete outside the release folder: $resolved"
        }

        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Copy-GuideFile {
    param([string]$Source, [string]$Destination)

    $name = Split-Path -Leaf $Source
    $target = Join-Path $Destination $name
    if ($name -like '*.txt') {
        # re-encode as UTF-8 with BOM so Notepad shows the Chinese text correctly
        $text = Get-Content -LiteralPath $Source -Raw -Encoding UTF8
        Set-Content -LiteralPath $target -Value $text -Encoding UTF8
    } else {
        # .bat / .url must keep their exact bytes (a BOM would break the batch file)
        Copy-Item -LiteralPath $Source -Destination $target -Force
    }
}

function Get-GuideFiles([string]$ExtraSource) {
    $general = @()
    if (Test-Path -LiteralPath $docsRoot) {
        $general = @(Get-ChildItem -LiteralPath $docsRoot -Filter '*.txt' -File)
    }

    $extra = @()
    if ($ExtraSource -and (Test-Path -LiteralPath $ExtraSource)) {
        $extra = @(Get-ChildItem -LiteralPath $ExtraSource -File)
    }

    return @{
        General = $general
        Extra   = $extra
    }
}

function New-Archive {
    param(
        [string]$PackageName,
        [string[]]$Sources,
        [string]$ArchivePath
    )

    if (Test-Path -LiteralPath $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }

    $zip = [System.IO.Compression.ZipFile]::Open($ArchivePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($source in $Sources) {
            $resolved = (Resolve-Path -LiteralPath $source).Path
            if (Test-Path -LiteralPath $resolved -PathType Container) {
                $base = $resolved.TrimEnd('\')
                $folderName = Split-Path -Leaf $base
                # the staging folder holds files that belong to the package root,
                # every other folder keeps its own name inside the archive
                $prefix = if ($folderName -like '.staging*') { '' } else { "$folderName/" }
                foreach ($file in (Get-ChildItem -LiteralPath $base -Recurse -File)) {
                    $relative = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $zip, $file.FullName, "$PackageName/$prefix$relative",
                        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
            } else {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, $resolved, "$PackageName/$(Split-Path -Leaf $resolved)",
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
    } finally {
        $zip.Dispose()
    }

    return [math]::Round((Get-Item -LiteralPath $ArchivePath).Length / 1MB, 1)
}

function New-Package {
    param(
        [string]$Suffix,
        [bool]$SelfContained,
        [string]$Label,
        [string]$ExtraDocs
    )

    $appDir = "$releaseRoot\PrinterShareFixer-$RuntimeIdentifier$Suffix"
    $cliDir = "$releaseRoot\psfix-cli-$RuntimeIdentifier$Suffix"
    $staging = "$releaseRoot\.staging$Suffix"
    $packageName = "PrinterShareFixer-$version-$RuntimeIdentifier$Suffix"
    $archive = "$releaseRoot\$packageName.zip"

    Write-Host "== Publishing $Label ==" -ForegroundColor Cyan
    Remove-Safely $appDir
    Remove-Safely $cliDir
    Remove-Safely $staging

    dotnet publish "$root\src\PrinterShareFixer.App\PrinterShareFixer.App.csproj" `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained $SelfContained `
        -p:WindowsAppSDKSelfContained=true `
        -p:Platform=x64 `
        -p:PathMap="$root=." `
        -o $appDir
    if ($LASTEXITCODE -ne 0) { throw "publish failed: app ($Label)" }

    dotnet publish "$root\src\PrinterShareFixer.Cli\PrinterShareFixer.Cli.csproj" `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained $SelfContained `
        -p:PathMap="$root=." `
        -o $cliDir
    if ($LASTEXITCODE -ne 0) { throw "publish failed: cli ($Label)" }

    $guides = Get-GuideFiles -ExtraSource $ExtraDocs

    # the app folder carries the text guides (so a user who copies only the app
    # folder still has the instructions)
    $appGuides = @($guides.General) + @($guides.Extra | Where-Object { $_.Extension -eq '.txt' })
    foreach ($file in $appGuides) {
        Copy-GuideFile -Source $file.FullName -Destination $appDir
    }

    # the package root carries every guide, plus helper files such as the
    # "run me" batch file and the download shortcut
    $rootGuides = @($guides.General) + @($guides.Extra)
    if ($rootGuides.Count -gt 0) {
        New-Item -ItemType Directory -Force -Path $staging | Out-Null
        foreach ($file in $rootGuides) {
            Copy-GuideFile -Source $file.FullName -Destination $staging
        }
    }

    Write-Host ("   app folder : " + $appDir)
    Write-Host ("   guides     : " + $appGuides.Count + " in app folder, " + $rootGuides.Count + " at package root")

    if (-not $SkipArchive) {
        $sources = @($appDir, $cliDir)
        if ($rootGuides.Count -gt 0) {
            $sources += $staging
        }

        $sizeMb = New-Archive -PackageName $packageName -Sources $sources -ArchivePath $archive
        Write-Host ("   archive    : " + (Split-Path -Leaf $archive) + " ($sizeMb MB)") -ForegroundColor Green
    }

    Remove-Safely $staging
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

# remove stale artifacts from earlier runs (archives and the guide copies that
# older versions of this script left in the release root)
foreach ($pattern in @('*.zip', '*.txt', '*.bat', '*.url')) {
    Get-ChildItem -LiteralPath $releaseRoot -Filter $pattern -File -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Safely $_.FullName }
}

if ($Variant -eq 'All' -or $Variant -eq 'SelfContained') {
    New-Package -Suffix '' -SelfContained $true `
        -Label 'self contained app (no .NET runtime needed)' `
        -ExtraDocs $null
}

if ($Variant -eq 'All' -or $Variant -eq 'FrameworkDependent') {
    New-Package -Suffix '-requires-dotnet' -SelfContained $false `
        -Label 'framework dependent app (needs the .NET 10 runtime)' `
        -ExtraDocs "$extraRoot\framework-dependent"
}

Write-Host ""
Write-Host "Done. Everything is under: $releaseRoot" -ForegroundColor Green
Write-Host "Self contained : release\PrinterShareFixer-$version-$RuntimeIdentifier.zip"
Write-Host "Slim (needs .NET 10) : release\PrinterShareFixer-$version-$RuntimeIdentifier-requires-dotnet.zip"
Write-Host "Upload both zip files to the GitHub release page."
