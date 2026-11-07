param([string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'), [switch]$Test, [ValidateSet("quick-note","quiet-mixer","task-menu","mini-icons")][string]$Only, [switch]$PublishedPackages)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$compiler = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$shared = Join-Path $Artifacts 'ReFix.Shared.dll'
if (!(Test-Path -LiteralPath $shared)) { throw "Build shared SDK first: $shared" }
$refs = @('WindowsBase','PresentationCore','PresentationFramework','ReachFramework','System.Printing','UIAutomationClient','UIAutomationTypes') | ForEach-Object { '/reference:' + (Join-Path $wpf ($_ + '.dll')) }
$refs += '/reference:System.dll','/reference:System.Core.dll','/reference:System.Xaml.dll','/reference:System.Web.Extensions.dll','/reference:System.Xml.dll','/reference:System.Xml.Linq.dll','/reference:System.IO.Compression.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',"/reference:$shared"
$packages = Join-Path $Artifacts 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$modules = @(
    @{ Id='quiet-mixer'; Name='Микшер громкости'; Description='Громкость устройства и приложений, настраиваемая горячая клавиша.'; Source='QuietMixer'; Exe='QuietMixer.exe'; Preview='mixer' },
    @{ Id='quick-note'; Name='Быстрая заметка'; Description='Заметки с форматированием, изображениями и плавающими списками.'; Source='QuickNote'; Exe='QuickNote.exe'; Preview='notes' },
    @{ Id='task-menu'; Name='Меню задач'; Description='Независимые панели файлов и папок по краям каждого экрана. Плавное раскрытие при наведении.'; Source='TaskMenu'; Exe='TaskMenu.exe'; Preview='menu' },
    @{ Id='mini-icons'; Name='Мини значки'; Description='Общее мини-меню значков HUB и расширений рядом с панелью задач.'; Source='MiniIcons'; Exe='MiniIcons.exe'; Preview='mini-icons' }
)
$versionSource = Get-Content -LiteralPath (Join-Path $root 'src\Shared\ModuleVersions.cs') -Raw
function ModuleVersion([string]$key) {
    $pattern = 'public const string ' + $key + '\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"'
    if ($versionSource -notmatch $pattern) { throw "Missing module version: $key" }
    return $Matches[1]
}
$previous = @{}
if ($PublishedPackages) {
    $published = Get-Content -LiteralPath (Join-Path $root 'catalog\catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in $published.Modules) { $previous[$entry.Id] = $entry }
}
if ($Only) { $modules = @($modules | Where-Object { $_.Id -ceq $Only }) }
$catalog = @()
foreach ($module in $modules) {
    $version = ModuleVersion $module.Source
    $minimum = ModuleVersion ($module.Source + 'MinHub')
    $zip = Join-Path $packages ($module.Id + '-' + $version + '.zip')
    $old = $previous[$module.Id]
    if ($old -and [version]$old.Version -gt [version]$version) { throw "Module version decreased: $($module.Id)" }
    if ($old -and $old.Version -ceq $version) {
        $urlPattern = '^https://github\.com/KirrieD/HUB-ReFIXwin11/releases/download/[a-zA-Z0-9._-]+/' + [regex]::Escape([IO.Path]::GetFileName($zip)) + '$'
        if ($old.MinHubVersion -cne $minimum -or $old.PackageUrl -cnotmatch $urlPattern -or $old.Sha256 -cnotmatch '^[0-9a-f]{64}$') { throw "Invalid published package identity: $($module.Id)" }
        Invoke-WebRequest -UseBasicParsing -Uri $old.PackageUrl -OutFile $zip
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -cne $old.Sha256) { throw "Published package changed: $($module.Id)" }
        $catalog += $old
        continue
    }

    $stage = Join-Path $Artifacts ('module-build\' + $module.Id)
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root ('src\Modules\' + $module.Source)) -Filter '*.cs' | ForEach-Object FullName)
    if ($module.Id -eq 'mini-icons') { $sources += (Join-Path $root 'src\Modules\TaskMenu\Topology.cs'),(Join-Path $root 'src\Modules\TaskMenu\DockPresentation.cs'),(Join-Path $root 'src\Modules\TaskMenu\DockStacking.cs') }
    $sources += Join-Path $root 'src\Modules\ThemedSurface.cs'
    $manifest = Join-Path $root 'src\Modules\app.manifest'
    & $compiler /nologo /utf8output /langversion:5 /target:winexe /optimize+ /platform:anycpu "/win32manifest:$manifest" ("/win32icon:"+(Join-Path $root ("assets\icons\"+$module.Id+".ico"))) "/out:$stage\$($module.Exe)" @refs @sources
    if ($LASTEXITCODE -ne 0) { throw "Module build failed: $($module.Id)" }
    Copy-Item -LiteralPath $shared -Destination $stage -Force
    $metadata = [ordered]@{ Id=$module.Id; Name=$module.Name; Description=$module.Description; Version=$version; Kind='process'; EntryPoint=$module.Exe; PackageUrl=''; Sha256=''; Preview=$module.Preview; MinHubVersion=$minimum; EnableArgument=''; DisableArgument=''; StatusArgument='' }
    $metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'module.json') -Encoding UTF8
    # Use an explicit list so stale compiler outputs cannot enter a package.
    Compress-Archive -LiteralPath @((Join-Path $stage $module.Exe),(Join-Path $stage 'ReFix.Shared.dll'),(Join-Path $stage 'module.json')) -DestinationPath $zip -Force
    $metadata.Sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $catalog += [pscustomobject]$metadata
}
$catalogFile = Join-Path $Artifacts $(if ($Only) { 'module-catalog.json' } else { 'catalog.json' })
[ordered]@{ SchemaVersion=1; Modules=@($catalog) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $catalogFile -Encoding UTF8
Write-Output "Built $($catalog.Count) local packages and $catalogFile"

if ($Test) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\QuietMixer') -Filter '*.cs' | ForEach-Object FullName)
    $testSources += Join-Path $root 'src\Modules\Tests\ThemeTests.cs'
    $testSources += Join-Path $root 'src\Modules\ThemedSurface.cs'
    $testSources += @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\QuickNote') -Filter '*.cs' | ForEach-Object FullName)
    $testExe = Join-Path $Artifacts 'ModuleThemeTests.exe'
    & $compiler /nologo /utf8output /langversion:5 /target:exe /main:ModuleThemeTests "/out:$testExe" @refs @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Module regression test compilation failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Module regression tests failed' }
}
