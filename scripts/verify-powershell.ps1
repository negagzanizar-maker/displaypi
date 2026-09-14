[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$failures = @()
foreach ($file in Get-ChildItem -LiteralPath $repositoryRoot -Recurse -Filter '*.ps1' -File |
    Where-Object { $_.FullName -notmatch '[\\/]node_modules[\\/]' }) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref] $tokens,
        [ref] $errors) | Out-Null
    foreach ($error in $errors) {
        $failures += "$($file.FullName):$($error.Extent.StartLineNumber): $($error.Message)"
    }
}

if ($failures.Count -gt 0) {
    throw "PowerShell syntax validation failed:`n$($failures -join "`n")"
}

Write-Output 'PowerShell syntax validation passed.'
