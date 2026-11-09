param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts\app'))
$ErrorActionPreference='Stop'
$bridgeCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $bridgeCompiler)){throw '.NET Framework 4.8 x64 compiler is required.'}
$bridgeOutput=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $bridgeOutput -Force | Out-Null
$bridgeSource=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -File -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName)
$bridgeCommon=@('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/warnaserror+','/utf8output',('/win32manifest:'+(Join-Path $PSScriptRoot 'src\app.manifest')),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.Xml.dll')
& $bridgeCompiler @bridgeCommon ('/out:'+(Join-Path $bridgeOutput 'CodexSteamBridge.exe')) @bridgeSource
if($LASTEXITCODE -ne 0){throw 'Application compilation failed.'}
Copy-Item -LiteralPath (Join-Path $bridgeOutput 'CodexSteamBridge.exe') -Destination (Join-Path $bridgeOutput 'CodexSteamBridge.Hook.exe') -Force
foreach($bridgeFile in @('README.md','README.en.md','LICENSE','COMPATIBILITY.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $bridgeFile) -Destination $bridgeOutput -Force}
& (Join-Path $PSScriptRoot 'tools\Create-Artwork.ps1') -OutputDirectory (Join-Path $bridgeOutput 'artwork')
Write-Output "Built: $bridgeOutput"
