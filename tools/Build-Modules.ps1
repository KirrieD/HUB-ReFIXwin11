param([string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'), [switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$compiler = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$shared = Join-Path $Artifacts 'ReFix.Shared.dll'
if (!(Test-Path -LiteralPath $shared)) { throw "Build shared SDK first: $shared" }
$refs = @('WindowsBase','PresentationCore','PresentationFramework','UIAutomationClient','UIAutomationTypes') | ForEach-Object { '/reference:' + (Join-Path $wpf ($_ + '.dll')) }
$refs += '/reference:System.dll','/reference:System.Core.dll','/reference:System.Xaml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',"/reference:$shared"
$packages = Join-Path $Artifacts 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$modules = @(
    @{ Id='quiet-mixer'; Name='Микшер громкости'; Description='Громкость устройства и приложений, настраиваемая горячая клавиша.'; Source='QuietMixer'; Exe='QuietMixer.exe'; Preview='mixer' },
    @{ Id='quick-note'; Name='Быстрая заметка'; Description='Локальная заметка с общей темой и сохранением при скрытии.'; Source='QuickNote'; Exe='QuickNote.exe'; Preview='notes' },
    @{ Id='task-menu'; Name='Меню задач'; Description='Независимые панели файлов и папок по краям каждого экрана. Плавное раскрытие при наведении.'; Source='TaskMenu'; Exe='TaskMenu.exe'; Preview='menu' }
)
$versionText = Get-Content -LiteralPath (Join-Path $root 'src\Shared\ProductInfo.cs') -Raw
if ($versionText -notmatch 'public const string Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'Missing canonical product version' }
$version = $Matches[1]
$catalog = @()
foreach ($module in $modules) {

    $stage = Join-Path $Artifacts ('module-build\' + $module.Id)
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root ('src\Modules\' + $module.Source)) -Filter '*.cs' | ForEach-Object FullName)
    $sources += Join-Path $root 'src\Modules\ThemedSurface.cs'
    $manifest = Join-Path $root 'src\Modules\app.manifest'
    & $compiler /nologo /utf8output /langversion:5 /target:winexe /optimize+ /platform:anycpu "/win32manifest:$manifest" "/out:$stage\$($module.Exe)" @refs @sources
    if ($LASTEXITCODE -ne 0) { throw "Module build failed: $($module.Id)" }
    Copy-Item -LiteralPath $shared -Destination $stage -Force
    $metadata = [ordered]@{ Id=$module.Id; Name=$module.Name; Description=$module.Description; Version=$version; Kind='process'; EntryPoint=$module.Exe; PackageUrl=''; Sha256=''; Preview=$module.Preview; MinHubVersion=$version; EnableArgument=''; DisableArgument=''; StatusArgument='' }
    $metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'module.json') -Encoding UTF8
    $zip = Join-Path $packages ($module.Id + '-' + $version + '.zip')
    # Use an explicit list so stale compiler outputs cannot enter a package.
    Compress-Archive -LiteralPath @((Join-Path $stage $module.Exe),(Join-Path $stage 'ReFix.Shared.dll'),(Join-Path $stage 'module.json')) -DestinationPath $zip -Force
    $metadata.Sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $catalog += [pscustomobject]$metadata
}
[ordered]@{ SchemaVersion=1; Modules=@($catalog) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Artifacts 'catalog.json') -Encoding UTF8
Write-Output "Built $($catalog.Count) local packages and $Artifacts\catalog.json"

if ($Test) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\QuietMixer') -Filter '*.cs' | ForEach-Object FullName)
    $testSources += Join-Path $root 'src\Modules\Tests\ThemeTests.cs'
    $testSources += Join-Path $root 'src\Modules\ThemedSurface.cs'
    $testSources += Join-Path $root 'src\Modules\QuickNote\Program.cs'
    $testExe = Join-Path $Artifacts 'ModuleThemeTests.exe'
    & $compiler /nologo /utf8output /langversion:5 /target:exe /main:ModuleThemeTests "/out:$testExe" @refs @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Module regression test compilation failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Module regression tests failed' }
}
