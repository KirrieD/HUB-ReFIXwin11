param([Parameter(Mandatory=$true)][string]$Tag, [string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
if ($Tag -cne 'v1.0.0') { throw 'This source builds version 1.0.0; use tag v1.0.0 or update versioned build/package metadata first.' }
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$catalogPath = Join-Path $Artifacts 'catalog.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expected = @('quiet-mixer','quick-note')
if ($catalog.SchemaVersion -ne 1 -or @($catalog.Modules).Count -ne 2) { throw 'Unexpected catalogue schema or module count' }
foreach ($module in $catalog.Modules) {
    if ($module.Id -cnotin $expected -or $module.Version -cne '1.0.0') { throw 'Unexpected module/version' }
    $expected = @($expected | Where-Object { $_ -cne $module.Id })
    $name = $module.Id + '-' + $module.Version + '.zip'
    $module.Sha256 = (Get-FileHash -LiteralPath (Join-Path $Artifacts "packages\$name") -Algorithm SHA256).Hash.ToLowerInvariant()
    $module.PackageUrl = "https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/$Tag/$name"
}
$json = $catalog | ConvertTo-Json -Depth 8
$json | Set-Content -LiteralPath $catalogPath -Encoding UTF8
$catalogDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'catalog'
New-Item -ItemType Directory -Path $catalogDirectory -Force | Out-Null
$json | Set-Content -LiteralPath (Join-Path $catalogDirectory 'catalog.json') -Encoding UTF8
Write-Output "Prepared catalogue for $Tag. URLs become usable only after publishing release assets. No commit, push or publish performed."
