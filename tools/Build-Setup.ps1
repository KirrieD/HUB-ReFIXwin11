param([string]$Artifacts = (Join-Path $PSScriptRoot '..\artifacts'), [string]$Payload)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$versionText = Get-Content -LiteralPath (Join-Path $root 'src\Shared\ProductInfo.cs') -Raw
if ($versionText -notmatch 'public const string Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'Missing canonical product version' }
$version = $Matches[1]
if (!$Payload) { $Payload = Join-Path $Artifacts ('HUB-ReFIXwin11-' + $version + '.zip') }
$Payload = [IO.Path]::GetFullPath($Payload)
if (!(Test-Path -LiteralPath $Payload -PathType Leaf)) { throw "Portable archive required: $Payload" }
$compiler = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\Bootstrap') -Filter '*.cs' | ForEach-Object FullName)
$output = Join-Path $Artifacts ('HUB-ReFIX-Setup-' + $version + '.exe')
& $compiler /nologo /utf8output /langversion:5 /optimize+ /target:winexe "/out:$output" '/reference:System.dll' '/reference:System.Core.dll' '/reference:System.Drawing.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Web.Extensions.dll' '/reference:System.IO.Compression.dll' '/reference:System.IO.Compression.FileSystem.dll' '/reference:Microsoft.CSharp.dll' ('/win32manifest:' + (Join-Path $root 'app.manifest')) ('/resource:' + $Payload + ',HubPayload.zip') @sources
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed' }
Copy-Item -LiteralPath $output -Destination (Join-Path $Artifacts 'Setup.exe') -Force
Write-Output "Setup: $output"
