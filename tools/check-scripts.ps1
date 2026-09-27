<#
    Extracts every embedded PowerShell script from the Core sources and feeds it to
    the PowerShell parser, so syntax problems (smart quotes, missing brackets,
    unbalanced braces, bad pipelines, ...) are caught at build time instead of runtime.

    Handles plain raw strings ("""...""") and interpolated raw strings ($"""..."""):
    for the interpolated ones the C# escapes {{ }} and {placeholders} are normalised first.

    This file is intentionally ASCII-only.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$files = @(
    "$root\src\PrinterShareFixer.Core\Profiles\CommonSteps.cs"
    "$root\src\PrinterShareFixer.Core\Profiles\RoleSteps.cs"
    "$root\src\PrinterShareFixer.Core\Runtime\SystemSnapshot.cs"
    "$root\src\PrinterShareFixer.Core\Diagnostics\DiagnosticReport.cs"
)

$total = 0
$bad = 0
$normalised = 0

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8
    $blocks = [regex]::Matches($text, '(?s)(?<interp>\$?)"""\r?\n(?<body>.*?)\r?\n\s*"""')
    $index = 0
    foreach ($block in $blocks) {
        $index++
        $script = $block.Groups['body'].Value

        $dollars = $block.Groups['dollars'].Value
        if ($dollars -eq '$') {
            $normalised++
            $script = $script.Replace('{{', '{').Replace('}}', '}')
            $script = [regex]::Replace($script, '\{[A-Za-z_][A-Za-z0-9_]*\}', '123')
        } elseif ($dollars -eq '$$') {
            $normalised++
            $script = [regex]::Replace($script, '\{\{[A-Za-z_][A-Za-z0-9_]*\}\}', '123')
        }

        $script = $script -replace '__TARGET__', 'PC-TEST'

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
Write-Host ("interpolated blocks normalised: " + $normalised)
if ($bad -eq 0) {
    Write-Host "OK: $total embedded scripts, no syntax errors." -ForegroundColor Green
} else {
    Write-Host "FAILED: $bad of $total embedded scripts have syntax errors." -ForegroundColor Red
}

exit $bad