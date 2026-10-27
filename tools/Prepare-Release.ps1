param([Parameter(Mandatory=$true)][string]$Tag, [string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versionText = Get-Content -LiteralPath (Join-Path $root 'src\Shared\ProductInfo.cs') -Raw
if ($versionText -notmatch 'public const string Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'Missing canonical product version' }
$version = $Matches[1]
if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $Tag -cne ('v' + $version)) { throw "Tag must match canonical product version v$version" }
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$catalogPath = Join-Path $Artifacts 'catalog.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expected = @('quiet-mixer','quick-note','task-menu')
if ($catalog.SchemaVersion -ne 1 -or @($catalog.Modules).Count -ne 3) { throw 'Unexpected catalogue schema or module count' }
foreach ($module in $catalog.Modules) {
    if ($module.Id -cnotin $expected -or $module.Version -cne $version -or $module.MinHubVersion -cne $version) { throw 'Unexpected module/version' }
    $expected = @($expected | Where-Object { $_ -cne $module.Id })
    $name = $module.Id + '-' + $module.Version + '.zip'
    $module.Sha256 = (Get-FileHash -LiteralPath (Join-Path $Artifacts "packages\$name") -Algorithm SHA256).Hash.ToLowerInvariant()
    $module.PackageUrl = "https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/$Tag/$name"
}
$json = $catalog | ConvertTo-Json -Depth 8
$json | Set-Content -LiteralPath $catalogPath -Encoding UTF8
$catalogDirectory = Join-Path $root 'catalog'
New-Item -ItemType Directory -Path $catalogDirectory -Force | Out-Null
$json | Set-Content -LiteralPath (Join-Path $catalogDirectory 'catalog.json') -Encoding UTF8
Write-Output "Prepared catalogue for $Tag. URLs become usable only after publishing release assets. No commit, push or publish performed."
