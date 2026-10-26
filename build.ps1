param([switch]$Test,[switch]$BuildTests,[switch]$Package)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
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
Compile 'HUB-ReFIX.Core.exe' @($hub+$engine) @('/target:winexe',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')),('/win32manifest:'+(Join-Path $root 'app.manifest')))
$bootstrap=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Bootstrap') -Filter '*.cs'|ForEach-Object FullName)
Compile 'HUB-ReFIX.exe' $bootstrap @('/target:winexe',('/win32manifest:'+(Join-Path $root 'app.manifest')))
$helper=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\UpdateHelper') -Filter '*.cs'|ForEach-Object FullName)
Compile 'ReFix.UpdateHelper.exe' $helper @('/target:winexe',('/win32manifest:'+(Join-Path $root 'app.manifest')))
Copy-Item -LiteralPath (Join-Path $root 'HUB-ReFIX.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'HUB-ReFIX.Core.exe.config') -Destination $out -Force
& (Join-Path $root 'tools\Build-Modules.ps1') -Artifacts $out
if($LASTEXITCODE -ne 0){throw 'Module build failed'}
if($Test -or $BuildTests){
    $tests=@(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs'|ForEach-Object FullName)
    Compile 'ReFix.Tests.exe' @($hub+$tests+$engine+(Join-Path $root 'src\Bootstrap\BundleAudit.cs')+(Join-Path $root 'src\Bootstrap\InstallFileTransaction.cs')) @('/target:exe','/main:ReFix.Tests.Runner',('/reference:'+(Join-Path $out 'ReFix.Shared.dll')))
    if($Test){
        & (Join-Path $out 'ReFix.Tests.exe')
        if($LASTEXITCODE -ne 0){throw 'Tests failed'}
    }
}
if($Package){& (Join-Path $root 'tools\Package.ps1') -Artifacts $out}
Write-Host ('Build ready: '+$out)
