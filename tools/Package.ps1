param([string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$versionText = Get-Content -LiteralPath (Join-Path $root 'src\Shared\ProductInfo.cs') -Raw
if ($versionText -notmatch 'public const string Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'Missing canonical product version' }
$version = $Matches[1]
$catalog = Get-Content -LiteralPath (Join-Path $Artifacts 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$files = @('HUB-ReFIX.exe','HUB-ReFIX.exe.config','HUB-ReFIX.Core.exe','HUB-ReFIX.Core.exe.config','ReFix.Shared.dll','ReFix.UpdateHelper.exe','catalog.json')
$packages = @()
$ids = @()
foreach ($module in $catalog.Modules) {
    if ($module.Id -cnotin @('quiet-mixer','quick-note','task-menu') -or $module.Id -cin $ids -or $module.Version -cne $version -or $module.MinHubVersion -cne $version) { throw 'Unexpected module/version in catalogue' }
    $ids += $module.Id
    $name = $module.Id + '-' + $version + '.zip'
    $path = Join-Path $Artifacts "packages\$name"
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $module.Sha256) { throw "Catalogue hash mismatch: $name" }
    if ($module.Id -cne 'task-menu') { $packages += $name }
}
if ($catalog.SchemaVersion -ne 1 -or $ids.Count -ne 3) { throw 'Unexpected catalogue schema/module count' }
foreach ($file in $files) { if (!(Test-Path -LiteralPath (Join-Path $Artifacts $file) -PathType Leaf)) { throw "Build first: missing $file" } }
$destination = Join-Path $Artifacts 'HUB-ReFIXwin11'
if ([IO.Path]::GetFullPath($destination) -ne [IO.Path]::Combine($Artifacts,'HUB-ReFIXwin11')) { throw 'Invalid package destination' }
if (Test-Path -LiteralPath $destination) {
    if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked staging directory' }
}
foreach ($path in @((Join-Path $destination 'packages'),(Join-Path $destination 'tools'))) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked staging directory: $path" }
}
$owned = @($files) + @('README.md','bundle-files.json') + @($packages | ForEach-Object { 'packages\' + $_ }) + @('tools\Install.ps1','tools\Install.cmd','tools\Uninstall.ps1','tools\Uninstall.cmd')
foreach ($file in $owned) {
    $path = Join-Path $destination $file
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked staging file: $path" }
}
New-Item -ItemType Directory -Path (Join-Path $destination 'packages'),(Join-Path $destination 'tools') -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $Artifacts $file) -Destination $destination -Force }
# Older updaters validate exactly these two bundled package identities. The new
# offline menu package is embedded in Core, whose SHA256 is already verified.
[ordered]@{ SchemaVersion=1; Modules=@($catalog.Modules | Where-Object Id -cne 'task-menu') } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'catalog.json') -Encoding UTF8
foreach ($file in $packages) { Copy-Item -LiteralPath (Join-Path $Artifacts "packages\$file") -Destination (Join-Path $destination 'packages') -Force }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination -Force
foreach ($file in @('Install.ps1','Install.cmd','Uninstall.ps1','Uninstall.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $destination 'tools') -Force }
$bundleFiles = @($files) + @('README.md') + @($packages | ForEach-Object { 'packages\' + $_ }) + @('tools\Install.ps1','tools\Install.cmd','tools\Uninstall.ps1','tools\Uninstall.cmd')
$entries = @($bundleFiles | ForEach-Object { [ordered]@{ Path=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $destination $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
[ordered]@{ SchemaVersion=1; Version=$version; Files=$entries } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'bundle-files.json') -Encoding UTF8
$zip = Join-Path $Artifacts ('HUB-ReFIXwin11-' + $version + '.zip')
# Archive only the manifest list; retained older packages/unknown staging files stay out.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $zip) {
    if ((Get-Item -LiteralPath $zip).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked release archive' }
    Remove-Item -LiteralPath $zip -Force
}
$archive = [IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @($bundleFiles) + @('bundle-files.json')) {
        $entry = 'HUB-ReFIXwin11/' + $file.Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $destination $file),$entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Output "Portable folder: $destination"
Write-Output "Release archive: $zip"
# Embed this exact, completed portable archive; never rebuild it after hashing.
& (Join-Path $PSScriptRoot 'Build-Setup.ps1') -Artifacts $Artifacts -Payload $zip
$setup = Join-Path $Artifacts ('HUB-ReFIX-Setup-' + $version + '.exe')
if (!(Test-Path -LiteralPath $setup -PathType Leaf)) { throw "Setup build did not produce $setup" }
$releaseBase = 'https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/v' + $version + '/'
[ordered]@{
    SchemaVersion=1
    Version=$version
    PackageUrl=$releaseBase + [IO.Path]::GetFileName($zip)
    Sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    SetupUrl=$releaseBase + [IO.Path]::GetFileName($setup)
    SetupSha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Notes='HUB ReFIX ' + $version + ': Настройки поведения разделены по приложениям. Меню задач поддерживает три режима наложения и невидимые грани с открытием при наведении.'
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Artifacts 'update.json') -Encoding UTF8
Write-Output "Setup executable: $setup"
Write-Output "Update metadata: $Artifacts\update.json"
