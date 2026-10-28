$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$buildRoot = Join-Path $packageRoot 'packaging'
$payloadRoot = Join-Path $buildRoot 'payload'
$releaseRoot = Join-Path $packageRoot 'dist'
New-Item -ItemType Directory -Path $buildRoot,$releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $payloadRoot) {
    $resolvedPayload = (Resolve-Path -LiteralPath $payloadRoot).Path
    if (-not $resolvedPayload.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected build folder.' }
    Remove-Item -LiteralPath $resolvedPayload -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
& (Join-Path $packageRoot 'build.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $packageRoot 'src') -Filter '*.cs' | ForEach-Object FullName) + (Join-Path $packageRoot 'setup\Setup.cs')
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/main:SetupProgram','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',('/win32manifest:'+(Join-Path $packageRoot 'app.manifest')),('/win32icon:'+(Join-Path $packageRoot 'assets\voice.ico')))
& $compiler @options '/define:UNINSTALL' ('/out:'+(Join-Path $payloadRoot 'VoiceFlow.Uninstall.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed.' }
foreach ($file in @('VoiceFlow.exe','release.json','README.md','PRIVACY.md','TROUBLESHOOTING.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $packageRoot $file) -Destination $payloadRoot }
foreach ($directory in @('bridge','runtime','third-party','assets','node_modules')) { New-Item -ItemType Directory -Path (Join-Path $payloadRoot $directory) | Out-Null }
Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bridge') -Filter '*.cjs' | Where-Object { $_.Name -notlike '*.test.cjs' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadRoot 'bridge') }
# Offline speech library for the local engine; the model itself is downloaded into the user's data folder, not bundled.
foreach ($package in @('sherpa-onnx-node','sherpa-onnx-win-x64')) {
    $source = Join-Path $packageRoot ('node_modules\' + $package)
    if (-not (Test-Path -LiteralPath $source)) { throw ('Run npm install first: ' + $package + ' is missing.') }
    Copy-Item -LiteralPath $source -Destination (Join-Path $payloadRoot ('node_modules\' + $package)) -Recurse
}
$runtimeNode = Join-Path $env:ProgramFiles 'nodejs\node.exe'
if (-not (Test-Path -LiteralPath $runtimeNode)) { throw 'Build machine needs Node.js 24 or later.' }
Copy-Item -LiteralPath $runtimeNode -Destination (Join-Path $payloadRoot 'runtime\node.exe')
Copy-Item -LiteralPath (Join-Path $packageRoot 'third-party\NODE-LICENSE.txt') -Destination (Join-Path $payloadRoot 'third-party')
Copy-Item -LiteralPath (Join-Path $packageRoot 'assets\voice.ico') -Destination (Join-Path $payloadRoot 'assets')
$checksumLines = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | Sort-Object FullName | ForEach-Object { $relative=$_.FullName.Substring($payloadRoot.Length+1).Replace('\','/'); (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative })
[IO.File]::WriteAllLines((Join-Path $payloadRoot 'checksums.sha256'),$checksumLines,[Text.UTF8Encoding]::new($false))
$payloadZip = Join-Path $buildRoot 'payload.zip'
if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot,$payloadZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$installer = Join-Path $releaseRoot 'VoiceFlow-0.3.3-Setup.exe'
& $compiler @options ('/resource:'+$payloadZip+',payload.zip') ('/out:'+$installer) @sources
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $releaseRoot 'SHA256SUMS.txt'),($hash+'  VoiceFlow-0.3.3-Setup.exe'+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $installer | Select-Object FullName,Length
