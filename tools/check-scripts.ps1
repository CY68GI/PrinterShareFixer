<#
    Extracts every embedded PowerShell script from the Core sources and feeds it
    to the PowerShell parser, so that syntax problems (smart quotes, missing
    brackets, ...) are caught at build time instead of at runtime.

    This file is intentionally ASCII-only: Windows PowerShell 5.1 reads .ps1
    files without a BOM using the ANSI code page, which corrupts non-ASCII text.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$files = @(
    "$root\src\PrinterShareFixer.Core\Profiles\CommonSteps.cs"
    "$root\src\PrinterShareFixer.Core\Profiles\RoleSteps.cs"
    "$root\src\PrinterShareFixer.Core\Runtime\SystemSnapshot.cs"
)

$total = 0
$bad = 0

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8
    $matches = [regex]::Matches($text, '(?s)"""\r?\n(?<body>.*?)\r?\n\s*"""')
    $index = 0
    foreach ($match in $matches) {
        $index++
        $script = $match.Groups['body'].Value -replace '__TARGET__', 'PC-TEST'
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseInput($script, [ref]$tokens, [ref]$errors) | Out-Null
        $total++
        if ($errors.Count -gt 0) {
            $bad++
            Write-Host ("[SYNTAX ERROR] " + (Split-Path $file -Leaf) + " script #$index") -ForegroundColor Red
            foreach ($parseError in ($errors | Select-Object -First 3)) {
                Write-Host ("    line " + $parseError.Extent.StartLineNumber + ": " + $parseError.Message)
            }

            $lineNumber = $errors[0].Extent.StartLineNumber
            $scriptLines = $script -split "`n"
            $from = [Math]::Max(0, $lineNumber - 2)
            for ($i = $from; $i -lt [Math]::Min($scriptLines.Count, $from + 4); $i++) {
                Write-Host ("      | " + $scriptLines[$i].TrimEnd())
            }
        }
    }
}

Write-Host ""
if ($bad -eq 0) {
    Write-Host "OK: $total embedded scripts, no syntax errors." -ForegroundColor Green
} else {
    Write-Host "FAILED: $bad of $total embedded scripts have syntax errors." -ForegroundColor Red
}

exit $bad
