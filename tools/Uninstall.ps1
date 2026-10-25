param()
$ErrorActionPreference = 'Stop'
if ($env:REFIX_DATA_ROOT) { throw 'Clear REFIX_DATA_ROOT before uninstalling the current-user app.' }
$parent = Join-Path $env:LOCALAPPDATA 'ReFIXHub'
$target = [IO.Path]::GetFullPath((Join-Path $parent 'app'))
if ($target -ne [IO.Path]::Combine([IO.Path]::GetFullPath($env:LOCALAPPDATA),'ReFIXHub','app')) { throw 'Invalid uninstall path' }
foreach ($path in @($parent,$target,(Join-Path $target 'packages'),(Join-Path $target 'tools'))) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked installation path: $path" }
}
$exe = Join-Path $target 'HUB-ReFIX.exe'
$running = @(Get-Process -Name 'HUB-ReFIX' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($running.Count) {
    $request = Start-Process -FilePath $exe -ArgumentList '--exit' -PassThru
    if (!$request.WaitForExit(5000)) { throw 'Exit request timed out. Close HUB from its tray menu.' }
    foreach ($process in $running) { if (!$process.WaitForExit(10000)) { throw 'HUB did not exit. Close it from its tray menu and retry.' } }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKey) {
    $value = (Get-Item -LiteralPath $runKey).GetValue('ReFIXHub')
    if ($value -eq ('"' + $exe + '" --background')) { Remove-ItemProperty -LiteralPath $runKey -Name 'ReFIXHub' }
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'HUB ReFIX.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcutPath).TargetPath -eq $exe) { Remove-Item -LiteralPath $shortcutPath -Force }
}
# Exact owned-file allowlist. Preserve unknown files and every data directory.
$files = @('HUB-ReFIX.exe','ReFix.Shared.dll','HUB-ReFIX.exe.config','catalog.json','README.md','packages\quiet-mixer-1.0.0.zip','packages\quick-note-1.0.0.zip','tools\Install.ps1','tools\Install.cmd','tools\Uninstall.ps1','tools\Uninstall.cmd')
foreach ($file in $files) { $path = Join-Path $target $file; if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force } }
foreach ($path in @((Join-Path $target 'packages'),(Join-Path $target 'tools'),$target)) {
    if ((Test-Path -LiteralPath $path -PathType Container) -and !(Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $path -Force }
}
Write-Output 'HUB app removed. Notes, theme, assets, installed modules and module state are preserved in LOCALAPPDATA\ReFIXHub.'
Write-Output 'Running module processes are preserved. Stop modules with OFF in HUB before uninstalling if desired.'
