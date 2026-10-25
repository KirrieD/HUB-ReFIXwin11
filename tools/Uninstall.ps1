param()
$ErrorActionPreference = 'Stop'
if ($env:REFIX_DATA_ROOT) { throw 'Clear REFIX_DATA_ROOT before uninstalling the current-user app.' }
$parent = Join-Path $env:LOCALAPPDATA 'ReFIXHub'
$target = [IO.Path]::GetFullPath((Join-Path $parent 'app'))
if ($target -ne [IO.Path]::Combine([IO.Path]::GetFullPath($env:LOCALAPPDATA),'ReFIXHub','app')) { throw 'Invalid uninstall path' }
foreach ($path in @($parent,$target,(Join-Path $target 'packages'),(Join-Path $target 'tools'))) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked installation path: $path" }
}
$source = $target
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
$files += 'bundle-files.json'
$exe = Join-Path $target 'HUB-ReFIX.exe'
$running = @(Get-Process -Name @('HUB-ReFIX','HUB-ReFIX.Core') -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -or $_.Path -eq (Join-Path $target 'HUB-ReFIX.Core.exe') })
if ($running.Count) {
    $request = Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -PassThru
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
$installationMarker = Join-Path $target 'installation.json'
if ((Test-Path -LiteralPath $installationMarker -PathType Leaf) -and !((Get-Item -LiteralPath $installationMarker).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    try {
        $installation = Get-Content -LiteralPath $installationMarker -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($installation.Mode -ceq 'Setup' -and $installation.Version -cmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { Remove-Item -LiteralPath $installationMarker -Force }
    } catch { Write-Warning 'Unrecognized installation marker preserved.' }
}
foreach ($file in $files) { $path = Join-Path $target $file; if ((Test-Path -LiteralPath $path -PathType Leaf) -and !((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { Remove-Item -LiteralPath $path -Force } }
foreach ($path in @((Join-Path $target 'packages'),(Join-Path $target 'tools'),$target)) {
    if ((Test-Path -LiteralPath $path -PathType Container) -and !(Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $path -Force }
}
Write-Output 'HUB app removed. Notes, theme, assets, installed modules and module state are preserved in LOCALAPPDATA\ReFIXHub.'
Write-Output 'Running module processes are preserved. Stop modules with OFF in HUB before uninstalling if desired.'
