param([switch]$NoShortcut, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
if ($env:REFIX_DATA_ROOT) { throw 'Clear REFIX_DATA_ROOT before installing for the current user.' }
$source = Split-Path $PSScriptRoot -Parent
$parent = Join-Path $env:LOCALAPPDATA 'ReFIXHub'
$target = [IO.Path]::GetFullPath((Join-Path $parent 'app'))
if ($target -ne [IO.Path]::Combine([IO.Path]::GetFullPath($env:LOCALAPPDATA),'ReFIXHub','app')) { throw 'Invalid installation path' }
foreach ($path in @($parent,$target,(Join-Path $target 'packages'),(Join-Path $target 'tools'))) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked installation path: $path" }
}
if ([IO.Path]::GetFullPath($source) -eq $target) { throw 'Run Install.cmd from the extracted portable archive, outside the installed app directory.' }
foreach ($directory in @($source,(Join-Path $source 'packages'),(Join-Path $source 'tools'))) {
    if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked source directory: $directory" }
}
$manifestPath = Join-Path $source 'bundle-files.json'
$manifestFile = Get-Item -LiteralPath $manifestPath
if ($manifestFile.Length -gt 65536 -or ($manifestFile.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid bundle manifest file' }
$bundle = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($bundle.SchemaVersion -ne 1 -or $bundle.Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid bundle manifest' }
$fixed = @('HUB-ReFIX.exe','HUB-ReFIX.exe.config','HUB-ReFIX.Core.exe','HUB-ReFIX.Core.exe.config','ReFix.Shared.dll','ReFix.UpdateHelper.exe','catalog.json','README.md','tools\Install.ps1','tools\Install.cmd','tools\Uninstall.ps1','tools\Uninstall.cmd')
$files = @()
foreach ($entry in $bundle.Files) {
    $file = [string]$entry.Path
    if (($file -cnotin $fixed -and $file -cnotmatch '^packages\\(quiet-mixer|quick-note)-(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.zip$') -or $file -cin $files -or $entry.Sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "Invalid bundle file: $file" }
    $files += $file
}
foreach ($file in $fixed) { if ($file -cnotin $files) { throw "Missing bundle entry: $file" } }
if (@($files | Where-Object { $_ -clike 'packages\*' }).Count -ne 2) { throw 'Bundle must contain two module packages' }
foreach ($id in @('quiet-mixer','quick-note')) { if (('packages\' + $id + '-' + $bundle.Version + '.zip') -cnotin $files) { throw "Missing current module: $id" } }
foreach ($entry in $bundle.Files) {
    $path = Join-Path $source $entry.Path
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.Sha256) { throw "Incomplete or modified portable archive: $($entry.Path)" }
}
$files += 'bundle-files.json'
$installationMarker = Join-Path $target 'installation.json'
if (Test-Path -LiteralPath $installationMarker) {
    $markerFile = Get-Item -LiteralPath $installationMarker
    if ($markerFile.Length -gt 4096 -or ($markerFile.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid installation marker file' }
    $priorInstallation = Get-Content -LiteralPath $installationMarker -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($priorInstallation.Mode -cne 'Setup' -or $priorInstallation.Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Unrecognized installation marker preserved; installation stopped.' }
}
$exe = Join-Path $target 'HUB-ReFIX.exe'
$sourceExe = Join-Path $source 'HUB-ReFIX.exe'
$running = @(Get-Process -Name @('HUB-ReFIX','HUB-ReFIX.Core') -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -or $_.Path -eq $sourceExe -or $_.Path -eq (Join-Path $target 'HUB-ReFIX.Core.exe') -or $_.Path -eq (Join-Path $source 'HUB-ReFIX.Core.exe') })
if ($running.Count) {
    $request = Start-Process -FilePath $sourceExe -ArgumentList '--exit' -WindowStyle Hidden -PassThru
    if (!$request.WaitForExit(5000)) { throw 'Exit request timed out. Close HUB from its tray menu.' }
    foreach ($process in $running) { if (!$process.WaitForExit(10000)) { throw 'HUB did not exit. Close it from its tray menu and retry.' } }
}
New-Item -ItemType Directory -Path $target,(Join-Path $target 'packages'),(Join-Path $target 'tools') -Force | Out-Null
foreach ($file in $files) {
    $existing = Join-Path $target $file
    if ((Test-Path -LiteralPath $existing) -and ((Get-Item -LiteralPath $existing).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked destination file: $existing" }
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $target $file) -Force }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -LiteralPath $runKey -Name 'ReFIXHub' -Value ('"' + $exe + '" --background')
$startupSettings = 'HKCU:\Software\ReFIXHub'
New-Item -Path $startupSettings -Force | Out-Null
New-ItemProperty -LiteralPath $startupSettings -Name 'AutoStartEnabled' -Value 1 -PropertyType DWord -Force | Out-Null
if (!$NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'HUB ReFIX.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = 'HUB ReFIX'
    $shortcut.Save()
}
$legacyPath = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'QuietMixer\QuietMixer.exe'))
if ((Test-Path -LiteralPath $legacyPath -PathType Leaf) -and (Test-Path -LiteralPath $runKey)) {
    $legacyRun = (Get-Item -LiteralPath $runKey).GetValue('QuietMixer')
    if ($legacyRun -cin @(('"' + $legacyPath + '" --background'),('"' + $legacyPath + '"'),$legacyPath)) {
        $wasRunning = @(Get-Process -Name 'QuietMixer' -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $legacyPath }).Count -gt 0
        $marker = Join-Path $parent 'migration-quiet-mixer.json'
        if (!(Test-Path -LiteralPath $marker)) {
            [ordered]@{ LegacyPath=$legacyPath; RunValue=$legacyRun; WasRunning=$wasRunning } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
        }
    }
}
$installationMarker = Join-Path $target 'installation.json'
if ((Test-Path -LiteralPath $installationMarker) -and ((Get-Item -LiteralPath $installationMarker).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Refusing linked installation marker' }
[ordered]@{ Mode='Setup'; Version=$bundle.Version } | ConvertTo-Json | Set-Content -LiteralPath $installationMarker -Encoding UTF8
if (!$NoLaunch) { Start-Process -FilePath $exe -WindowStyle Hidden | Out-Null }
Write-Output "Installed for current user: $target"
Write-Output 'Data, modules and notes are kept separately in LOCALAPPDATA\ReFIXHub.'
