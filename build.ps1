param([switch]$Test,[switch]$BuildTests,[switch]$Package)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if($Test){& (Join-Path $root 'tools\Verify-PowerShell.ps1') -Root $root}
$out=Join-Path $root 'artifacts'
if($env:REFIX_BUILD_OUTPUT){$out=[IO.Path]::GetFullPath($env:REFIX_BUILD_OUTPUT)}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('WindowsBase','PresentationCore','PresentationFramework','UIAutomationClient','UIAutomationTypes','WindowsFormsIntegration')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_+'.dll'))}
$refs+='/reference:System.dll','/reference:System.Core.dll','/reference:System.Xaml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:Microsoft.CSharp.dll'
function Compile([string]$target,[string[]]$files,[string[]]$extra){
    & $compiler /nologo /utf8output /langversion:5 /optimize+ "/out:$out\$target" @refs @extra @files
    if($LASTEXITCODE -ne 0){throw ('Compilation failed: '+$target)}
}
$shared=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Shared') -Filter '*.cs'|ForEach-Object FullName)
Compile 'ReFix.Shared.dll' $shared @('/target:library')
$hub=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Hub') -Filter '*.cs'|ForEach-Object FullName)
$engine=Join-Path $root 'src\UpdateHelper\UpdateEngine.cs'
$bootstrap=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Bootstrap') -Filter '*.cs'|ForEach-Object FullName)
Compile 'HUB-ReFIX.exe' $bootstrap @('/target:winexe',('/win32icon:'+(Join-Path $root 'assets\icons\hub.ico')),('/win32manifest:'+(Join-Path $root 'app.manifest')))
$helper=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\UpdateHelper') -Filter '*.cs'|ForEach-Object FullName)
Compile 'ReFix.UpdateHelper.exe' $helper @('/target:winexe',('/win32manifest:'+(Join-Path $root 'app.manifest')))
Copy-Item -LiteralPath (Join-Path $root 'HUB-ReFIX.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'HUB-ReFIX.Core.exe.config') -Destination $out -Force
& (Join-Path $root 'tools\Build-Modules.ps1') -Artifacts $out
if($LASTEXITCODE -ne 0){throw 'Module build failed'}
$versionText=Get-Content -LiteralPath (Join-Path $root 'src\Shared\ProductInfo.cs') -Raw
if($versionText -notmatch 'public const string Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"'){throw 'Missing canonical product version'}
$menuResource='/resource:'+(Join-Path $out ('packages\task-menu-'+$Matches[1]+'.zip'))+',ReFix.TaskMenu.zip'
$miniResource='/resource:'+(Join-Path $out ('packages\mini-icons-'+$Matches[1]+'.zip'))+',ReFix.MiniIcons.zip'
Compile 'HUB-ReFIX.Core.exe' @($hub+$engine) @('/target:winexe',('/win32icon:'+(Join-Path $root 'assets\icons\hub.ico')),('/reference:'+(Join-Path $out 'ReFix.Shared.dll')),('/win32manifest:'+(Join-Path $root 'app.manifest')),$menuResource,$miniResource)
if($Test -or $BuildTests){
    Compile 'ThemeLazy.Tests.exe' @((Join-Path $root 'tests\ThemeLoadTests.cs')) @('/target:exe','/main:ReFix.Tests.ThemeLoadTests',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    $gridUi=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\TaskMenu') -Filter '*.cs'|ForEach-Object FullName)
    $gridUi+=(Join-Path $root 'src\Modules\ThemedSurface.cs'),(Join-Path $root 'tests\TaskMenuGridUiTests.cs')
    Compile 'TaskMenuGrid.Ui.Tests.exe' $gridUi @('/target:exe','/define:TASK_MENU_GRID_UI_FIXTURE','/main:ReFix.Tests.TaskMenuGridUiTests',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    $mixerUi=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\QuietMixer') -Filter '*.cs'|ForEach-Object FullName)
    $mixerUi+=(Join-Path $root 'src\Modules\ThemedSurface.cs'),(Join-Path $root 'tests\MixerLayoutUiTests.cs')
    Compile 'MixerLayout.Ui.Tests.exe' @($hub+$engine+$mixerUi) @('/target:exe','/define:MIXER_LAYOUT_UI_FIXTURE,MIXER_LAYOUT_EDITOR_UI_FIXTURE','/main:ReFix.Tests.MixerLayoutUiTests',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    Compile 'IconArtwork.Tests.exe' @((Join-Path $root 'tests\IconArtworkTests.cs'),(Join-Path $root 'tests\IconArtworkRunner.cs')) @('/target:exe','/define:ICON_ARTWORK_FIXTURE','/main:IconArtworkRunner',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    Compile 'MiniIcons.External.Tests.exe' @((Join-Path $root 'src\Modules\MiniIcons\ExternalWindowsOverflow.cs'),(Join-Path $root 'tests\MiniIconsExternalTests.cs')) @('/target:exe','/define:MINI_ICONS_EXTERNAL_FIXTURE','/main:ReFix.Tests.MiniIconsExternalTests')
    Compile 'MiniTrayClient.Tests.exe' @((Join-Path $root 'tests\MiniTrayClientTests.cs')) @('/target:exe','/define:MINI_TRAY_CLIENT_FIXTURE','/main:ReFix.Tests.MiniTrayClientTests',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    $miniUi=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\MiniIcons') -Filter '*.cs'|ForEach-Object FullName)
    $miniUi+=(Join-Path $root 'src\Modules\TaskMenu\Topology.cs'),(Join-Path $root 'src\Modules\TaskMenu\DockPresentation.cs'),(Join-Path $root 'src\Modules\ThemedSurface.cs'),(Join-Path $root 'tests\MiniIconsUiTests.cs')
    Compile 'MiniIcons.Ui.Tests.exe' $miniUi @('/target:exe','/define:MINI_ICONS_UI_FIXTURE','/main:ReFix.Tests.MiniIconsUiTests',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    $tests=@(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs'|ForEach-Object FullName)
    Compile 'ReFix.Tests.exe' @($hub+$tests+$engine+(Join-Path $root 'src\Bootstrap\BundleAudit.cs')+(Join-Path $root 'src\Bootstrap\InstallFileTransaction.cs')) @('/target:exe','/main:ReFix.Tests.Runner',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')),$menuResource,$miniResource)
    if($Test){
        foreach($fixture in @('IconArtwork.Tests.exe','MiniTrayClient.Tests.exe','MiniIcons.Ui.Tests.exe','MiniIcons.External.Tests.exe')) { & (Join-Path $out $fixture);if($LASTEXITCODE -ne 0){throw ('Fixture failed: '+$fixture)} }
        & (Join-Path $out 'ThemeLazy.Tests.exe')
        if($LASTEXITCODE -ne 0){throw 'Lazy theme startup test failed'}
        & (Join-Path $out 'TaskMenuGrid.Ui.Tests.exe')
        if($LASTEXITCODE -ne 0){throw 'Task menu grid UI tests failed'}
        & (Join-Path $out 'MixerLayout.Ui.Tests.exe')
        if($LASTEXITCODE -ne 0){throw 'Mixer layout UI tests failed'}
        & (Join-Path $out 'ReFix.Tests.exe')
        if($LASTEXITCODE -ne 0){throw 'Tests failed'}
    }
}
if($Package){& (Join-Path $root 'tools\Package.ps1') -Artifacts $out}
Write-Host ('Build ready: '+$out)
