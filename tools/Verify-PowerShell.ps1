param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$scripts = @((Get-Item -LiteralPath (Join-Path $Root 'build.ps1'))) + @(Get-ChildItem -LiteralPath (Join-Path $Root 'tools') -Filter '*.ps1')
foreach ($script in $scripts) {
    $bytes = [IO.File]::ReadAllBytes($script.FullName)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
    $nonAscii = $false
    foreach ($value in $bytes) { if ($value -gt 127) { $nonAscii = $true; break } }
    if ($nonAscii -and !$bom) { throw "Windows PowerShell 5.1 needs UTF-8 BOM for non-ASCII script: $($script.Name)" }
    $parseTokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) { throw "Invalid PowerShell syntax in $($script.Name): $($parseErrors[0].Message)" }
}
Write-Output "PASS PowerShell script encodings and syntax ($($scripts.Count) files)"
