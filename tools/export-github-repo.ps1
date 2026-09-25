<#
    Builds a clean, publish-ready copy of this repository.

    Why: the development working copy also contains files that should not be
    published (a git pointer file, local build folders, editor and debug leftovers).
    This script exports only the committed files, replays every version tag as its
    own commit, and writes a neutral .gitignore, so the result is exactly what
    should live on GitHub.

    Output: <repo root>\github-repo  (a fresh git repository, branch main, all tags)

    The folder is listed in the working repository's .gitignore, so it never shows up
    in the development history.

    Usage:
      powershell -ExecutionPolicy Bypass -File .\tools\export-github-repo.ps1
      powershell -ExecutionPolicy Bypass -File .\tools\export-github-repo.ps1 -OutputDirectory D:\tmp\publish

    After the first export you keep working in this repository and publish with:
      powershell -ExecutionPolicy Bypass -File .\tools\export-github-repo.ps1 -Update
    That copies the current committed state into the publish folder and commits it
    on top of the existing history (no force push, no rewritten commits), creating
    any version tag that is still missing there.

    This file is intentionally ASCII-only.
#>
param(
    [string]$OutputDirectory = '',
    [string]$CommitName = 'PrinterShareFixer',
    [string]$CommitEmail = 'printer-share-fixer@users.noreply.github.com',
    [switch]$Update,
    [string]$Message = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'github-repo'
}

# .gitignore for the public repository: only rules that make sense there
$publicGitignore = @'
# build output
build/
bin/
obj/
dist/

# packages produced by package.ps1 - publish them as GitHub release assets instead
release/

# IDE / editor
.vs/
*.user
*.suo
*.binlog
'@

function Clear-WorkTree {
    Get-ChildItem -LiteralPath $OutputDirectory -Force |
        Where-Object { $_.Name -ne '.git' } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
}

function Expand-Ref([string]$reference) {
    $zip = Join-Path $env:TEMP ("psf-export-" + ($reference -replace '[\\/:*?"<>|]', '_') + '.zip')
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

    git -C $root archive --format=zip --output="$zip" $reference
    if ($LASTEXITCODE -ne 0) { throw "git archive failed for $reference" }

    Expand-Archive -LiteralPath $zip -DestinationPath $OutputDirectory -Force
    Remove-Item -LiteralPath $zip -Force

    # publish-time cleanup of the ignore rules
    Set-Content -LiteralPath (Join-Path $OutputDirectory '.gitignore') -Value $publicGitignore -Encoding utf8
}

if ($Update) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory '.git'))) {
        throw "There is no publish repository in $OutputDirectory yet. Run this script once without -Update."
    }

    Write-Host "== Updating $OutputDirectory from the current commit ==" -ForegroundColor Cyan
    git -C $OutputDirectory config user.name $CommitName
    git -C $OutputDirectory config user.email $CommitEmail
    Clear-WorkTree
    Expand-Ref 'HEAD'

    git -C $OutputDirectory add -A
    $staged = (git -C $OutputDirectory status --porcelain)
    if ([string]::IsNullOrWhiteSpace($staged)) {
        Write-Host '   nothing changed, no new commit created' -ForegroundColor Yellow
    } else {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = (git -C $root log -1 --pretty=%s)
        }

        git -C $OutputDirectory commit -q -m $Message
        Write-Host ("   new commit {0}: {1}" -f (git -C $OutputDirectory rev-parse --short HEAD), $Message)
    }

    # create version tags that do not exist yet in the publish repository
    $exportTags = @(git -C $OutputDirectory tag)
    foreach ($tag in @(git -C $root tag --sort=creatordate)) {
        if ($exportTags -notcontains $tag) {
            $tagMessage = (git -C $root tag -l --format='%(contents:subject)' $tag)
            if ([string]::IsNullOrWhiteSpace($tagMessage)) { $tagMessage = "release $tag" }
            git -C $OutputDirectory tag -a $tag -m $tagMessage
            Write-Host ("   new tag {0}" -f $tag) -ForegroundColor Green
        }
    }
} else {
    if (Test-Path -LiteralPath $OutputDirectory) {
        $resolved = (Resolve-Path -LiteralPath $OutputDirectory).Path
        if (-not $resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a folder outside the repository: $resolved"
        }

        Remove-Item -LiteralPath $resolved -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

    Write-Host "== Creating a fresh repository in $OutputDirectory ==" -ForegroundColor Cyan
    git -C $OutputDirectory init -q -b main
    git -C $OutputDirectory config user.name $CommitName
    git -C $OutputDirectory config user.email $CommitEmail
    git -C $OutputDirectory config core.quotepath false

    $tags = @(git -C $root tag --sort=creatordate)
    if ($tags.Count -eq 0) {
        throw 'No version tags found in the source repository; nothing to export.'
    }

    foreach ($tag in $tags) {
        Clear-WorkTree
        Expand-Ref $tag

        $message = (git -C $root tag -l --format='%(contents:subject)' $tag)
        if ([string]::IsNullOrWhiteSpace($message)) { $message = "release $tag" }

        git -C $OutputDirectory add -A
        git -C $OutputDirectory commit -q -m $message
        git -C $OutputDirectory tag -a $tag -m $message

        $short = (git -C $OutputDirectory rev-parse --short HEAD)
        Write-Host ("   {0,-8} -> commit {1}" -f $tag, $short)
    }
}

Write-Host ""
Write-Host "== Result ==" -ForegroundColor Green
Write-Host ("   folder : " + $OutputDirectory)
Write-Host ("   branch : " + (git -C $OutputDirectory branch --show-current))
Write-Host ("   files  : " + (git -C $OutputDirectory ls-files | Measure-Object).Count)
Write-Host ("   tags   : " + ((git -C $OutputDirectory tag) -join ', '))
Write-Host ("   status : " + $(if (-not (git -C $OutputDirectory status --porcelain)) { 'clean' } else { 'dirty' }))
Write-Host ""
Write-Host "Next steps:"
Write-Host "   1. git ls-files            (inside that folder, check the file list)"
Write-Host "   2. replace the badge placeholder in README.md"
Write-Host "   3. git remote add origin https://github.com/<you>/<repo>.git"
Write-Host "   4. git push -u origin main ; git push origin --tags"
