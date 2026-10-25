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
$files = @('HUB-ReFIX.exe','ReFix.Shared.dll','HUB-ReFIX.exe.config','catalog.json','README.md','packages\quiet-mixer-1.0.0.zip','packages\quick-note-1.0.0.zip','tools\Install.ps1','tools\Install.cmd','tools\Uninstall.ps1','tools\Uninstall.cmd')
foreach ($file in $files) { if (!(Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)) { throw "Incomplete portable archive: $file" } }
$exe = Join-Path $target 'HUB-ReFIX.exe'
$sourceExe = Join-Path $source 'HUB-ReFIX.exe'
$running = @(Get-Process -Name 'HUB-ReFIX' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -or $_.Path -eq $sourceExe })
if ($running.Count) {
    $request = Start-Process -FilePath $sourceExe -ArgumentList '--exit' -PassThru
    if (!$request.WaitForExit(5000)) { throw 'Exit request timed out. Close HUB from its tray menu.' }
    foreach ($process in $running) { if (!$process.WaitForExit(10000)) { throw 'HUB did not exit. Close it from its tray menu and retry.' } }
}
New-Item -ItemType Directory -Path $target,(Join-Path $target 'packages'),(Join-Path $target 'tools') -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $target $file) -Force }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -LiteralPath $runKey -Name 'ReFIXHub' -Value ('"' + $exe + '" --background')
if (!$NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'HUB ReFIX.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = 'HUB ReFIX - Ctrl+Alt+H'
    $shortcut.Save()
}
if (!$NoLaunch) { Start-Process -FilePath $exe | Out-Null }
Write-Output "Installed for current user: $target"
Write-Output 'Data, modules and notes are kept separately in LOCALAPPDATA\ReFIXHub.'
