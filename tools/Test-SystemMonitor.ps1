param([string]$Artifacts=(Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$Artifacts=[IO.Path]::GetFullPath($Artifacts)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $framework 'csc.exe'}
$refs=@('WindowsBase','PresentationCore','PresentationFramework') | ForEach-Object {'/reference:'+(Join-Path $framework ('WPF\'+$_+'.dll'))}
$refs+='/reference:System.dll','/reference:System.Core.dll','/reference:System.Xaml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Management.dll','/reference:System.Web.Extensions.dll',('/reference:'+(Join-Path $Artifacts 'ReFix.Shared.dll'))
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules\SystemMonitor') -Filter '*.cs'|ForEach-Object FullName)
$sources+=(Join-Path $root 'src\Modules\ThemedSurface.cs'),(Join-Path $root 'tests\SystemMonitorTests.cs')
$test=Join-Path $Artifacts 'SystemMonitor.Tests.exe'
& $compiler /nologo /utf8output /langversion:5 /target:exe /define:SYSTEM_MONITOR_FIXTURE /main:ReFix.Tests.SystemMonitorTests "/out:$test" @refs @sources
if($LASTEXITCODE){throw 'System monitor test compilation failed'}
& $test
if($LASTEXITCODE){throw 'System monitor tests failed'}
