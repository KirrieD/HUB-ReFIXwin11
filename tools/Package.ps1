param([string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$destination = Join-Path $Artifacts 'HUB-ReFIXwin11'
$files = @('HUB-ReFIX.exe','ReFix.Shared.dll','HUB-ReFIX.exe.config','catalog.json')
$packages = @('quiet-mixer-1.0.0.zip','quick-note-1.0.0.zip')
foreach ($file in $files) { if (!(Test-Path -LiteralPath (Join-Path $Artifacts $file) -PathType Leaf)) { throw "Build first: missing $file" } }
foreach ($file in $packages) { if (!(Test-Path -LiteralPath (Join-Path $Artifacts "packages\$file") -PathType Leaf)) { throw "Build modules first: missing $file" } }
# Never package arbitrary build/test output; start with an empty, validated staging directory.
if ([IO.Path]::GetFullPath($destination) -ne ([IO.Path]::Combine($Artifacts,'HUB-ReFIXwin11'))) { throw 'Invalid package destination' }
if (Test-Path -LiteralPath $destination) {
    if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked staging directory' }
    Remove-Item -LiteralPath $destination -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $destination 'packages'),(Join-Path $destination 'tools') -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $Artifacts $file) -Destination $destination }
foreach ($file in $packages) { Copy-Item -LiteralPath (Join-Path $Artifacts "packages\$file") -Destination (Join-Path $destination 'packages') }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination
foreach ($file in @('Install.ps1','Install.cmd','Uninstall.ps1','Uninstall.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $destination 'tools') }
$zip = Join-Path $Artifacts 'HUB-ReFIXwin11-1.0.0.zip'
Compress-Archive -LiteralPath $destination -DestinationPath $zip -Force
Write-Output "Portable folder: $destination"
Write-Output "Release archive: $zip"
