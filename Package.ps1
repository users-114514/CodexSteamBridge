param([switch]$SkipTests)
$ErrorActionPreference='Stop'
if(-not $SkipTests){& (Join-Path $PSScriptRoot 'Test.ps1')}
$bridgeOutput=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $bridgeOutput -Force | Out-Null
$bridgeRelease=Join-Path $bridgeOutput 'CodexSteamBridge-v0.2.0-windows-x64.zip'
$bridgeBinaryPaths=@('CodexSteamBridge.exe','CodexSteamBridge.Hook.exe','README.md','README.en.md','COMPATIBILITY.md','LICENSE','artwork') | ForEach-Object {Join-Path $PSScriptRoot ('artifacts\app\'+$_)}
Compress-Archive -LiteralPath $bridgeBinaryPaths -DestinationPath $bridgeRelease -Force
$bridgeSource=Join-Path $bridgeOutput 'CodexSteamBridge-v0.2.0-source.zip'
$bridgeSourcePaths=@('src','tests','tools','.github','Build.ps1','Test.ps1','Package.ps1','README.md','README.en.md','COMPATIBILITY.md','LICENSE','.gitignore','CHANGELOG.md','RELEASE-CHECKLIST.md')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bridgeMemory=New-Object IO.MemoryStream
$bridgeArchive=New-Object IO.Compression.ZipArchive($bridgeMemory,[IO.Compression.ZipArchiveMode]::Create,$true)
try {
    foreach($bridgeRelative in $bridgeSourcePaths){
        $bridgePath=Join-Path $PSScriptRoot $bridgeRelative
        $bridgeItems=if(Test-Path -LiteralPath $bridgePath -PathType Container){Get-ChildItem -LiteralPath $bridgePath -File -Recurse}else{Get-Item -LiteralPath $bridgePath}
        foreach($bridgeItem in $bridgeItems){
            $bridgeName=$bridgeItem.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/')
            if($bridgeName -match '(^|/)(obj|bin|artifacts|runtime)(/|$)' -or $bridgeName -match '\.(user|log|suo)$'){continue}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bridgeArchive,$bridgeItem.FullName,$bridgeName,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
        }
    }
} finally {$bridgeArchive.Dispose()}
[IO.File]::WriteAllBytes($bridgeSource,$bridgeMemory.ToArray());$bridgeMemory.Dispose()
$bridgeSums=@(Get-FileHash -LiteralPath $bridgeRelease,$bridgeSource -Algorithm SHA256 | ForEach-Object {$_.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_.Path)})
[IO.File]::WriteAllLines((Join-Path $bridgeOutput 'SHA256SUMS.txt'),$bridgeSums,(New-Object Text.UTF8Encoding($false)))
Write-Output "Packaged: $bridgeOutput"
