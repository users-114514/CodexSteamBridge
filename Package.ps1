param([switch]$SkipTests)
$ErrorActionPreference='Stop'
if(-not $SkipTests){& (Join-Path $PSScriptRoot 'Test.ps1')}
$bridgeOutput=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $bridgeOutput -Force | Out-Null
$bridgeRelease=Join-Path $bridgeOutput 'CodexSteamBridge-v0.1.1-windows-x64.zip'
Compress-Archive -Path (Join-Path $PSScriptRoot 'artifacts\app\*') -DestinationPath $bridgeRelease -Force
$bridgeSource=Join-Path $bridgeOutput 'CodexSteamBridge-v0.1.1-source.zip'
$bridgeSourcePaths=@('src','tests','tools','.github','Build.ps1','Test.ps1','Package.ps1','README.md','README.en.md','COMPATIBILITY.md','LICENSE','.gitignore','CHANGELOG.md','RELEASE-CHECKLIST.md') | ForEach-Object {Join-Path $PSScriptRoot $_}
Compress-Archive -LiteralPath $bridgeSourcePaths -DestinationPath $bridgeSource -Force
$bridgeSums=@(Get-FileHash -LiteralPath $bridgeRelease,$bridgeSource -Algorithm SHA256 | ForEach-Object {$_.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_.Path)})
[IO.File]::WriteAllLines((Join-Path $bridgeOutput 'SHA256SUMS.txt'),$bridgeSums,(New-Object Text.UTF8Encoding($false)))
Write-Output "Packaged: $bridgeOutput"
