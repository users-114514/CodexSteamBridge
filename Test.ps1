param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$bridgeApp=Join-Path $PSScriptRoot 'artifacts\app'
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'Build.ps1')}
$bridgeCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bridgeTests=Join-Path $PSScriptRoot 'artifacts\tests'
New-Item -ItemType Directory -Path $bridgeTests -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $bridgeApp 'CodexSteamBridge.exe') -Destination $bridgeTests -Force
& $bridgeCompiler /nologo /target:exe /platform:x64 /warn:4 /warnaserror+ /utf8output ('/r:'+(Join-Path $bridgeApp 'CodexSteamBridge.exe')) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll ('/out:'+(Join-Path $bridgeTests 'Tests.exe')) (Join-Path $PSScriptRoot 'tests\Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Test compilation failed.'}
$bridgeFixture=Join-Path $bridgeTests ('fixture-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $bridgeTests 'Tests.exe') $bridgeFixture $bridgeApp
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
& $bridgeCompiler /nologo /target:exe /platform:x64 /warn:4 /warnaserror+ /utf8output ('/r:'+(Join-Path $bridgeApp 'CodexSteamBridge.exe')) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll ('/out:'+(Join-Path $bridgeTests 'ProcessTests.exe')) (Join-Path $PSScriptRoot 'tests\ProcessTests.cs')
if($LASTEXITCODE -ne 0){throw 'Process test compilation failed.'}
& (Join-Path $bridgeTests 'ProcessTests.exe') $bridgeApp (Join-Path $bridgeFixture 'processes')
if($LASTEXITCODE -ne 0){throw 'Process tests failed.'}
& $bridgeCompiler /nologo /target:exe /platform:x64 /warn:4 /warnaserror+ /utf8output ('/r:'+(Join-Path $bridgeApp 'CodexSteamBridge.exe')) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:'+(Join-Path $bridgeTests 'CliTests.exe')) (Join-Path $PSScriptRoot 'tests\CliTests.cs')
if($LASTEXITCODE -ne 0){throw 'CLI/profile test compilation failed.'}
& (Join-Path $bridgeTests 'CliTests.exe') $bridgeApp (Join-Path $bridgeFixture 'cli')
if($LASTEXITCODE -ne 0){throw 'CLI/profile tests failed.'}
