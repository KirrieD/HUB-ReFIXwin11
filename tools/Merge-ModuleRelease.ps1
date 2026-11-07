param(
    [Parameter(Mandatory=$true)][string]$Tag,
    [Parameter(Mandatory=$true)][string]$Candidate,
    [Parameter(Mandatory=$true)][string]$Catalog,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^(quick-note|quiet-mixer|task-menu|mini-icons)-v([0-9]+\.[0-9]+\.[0-9]+)$') { throw 'Invalid module release tag' }
$id = $Matches[1]
$version = $Matches[2]
$built = Get-Content -LiteralPath $Candidate -Raw -Encoding UTF8 | ConvertFrom-Json
$current = Get-Content -LiteralPath $Catalog -Raw -Encoding UTF8 | ConvertFrom-Json
if ($built.SchemaVersion -ne 1 -or @($built.Modules).Count -ne 1 -or $current.SchemaVersion -ne 1) { throw 'Invalid catalogue shape' }
$entry = @($built.Modules)[0]
$previous = @($current.Modules | Where-Object { $_.Id -ceq $id })
if ($entry.Id -cne $id -or $entry.Version -cne $version -or $previous.Count -ne 1 -or [version]$previous[0].Version -ge [version]$version) { throw 'Release must increment exactly the tagged module version' }
if ($entry.Sha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Missing verified module hash' }
$filename = $id + '-' + $version + '.zip'
$package = Join-Path (Split-Path -Parent $Candidate) ('packages\' + $filename)
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.Sha256) { throw 'Module package hash mismatch' }
$entry.PackageUrl = "https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/$Tag/$filename"
$current.Modules = @($current.Modules | ForEach-Object { if ($_.Id -ceq $id) { $entry } else { $_ } })
$current | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding UTF8
Write-Output "Verified $filename. Other catalogue entries retained."
