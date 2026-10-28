$Public = $true
$ErrorActionPreference = 'Stop'
$voiceFlowCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$voiceFlowSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$voiceFlowOptions = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll',('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/out:' + (Join-Path $PSScriptRoot 'VoiceFlow.exe')))
if ($Public) { $voiceFlowOptions += '/define:PUBLIC' }
$voiceFlowIcon = Join-Path $PSScriptRoot 'assets\voice.ico'
if (Test-Path -LiteralPath $voiceFlowIcon) { $voiceFlowOptions += '/win32icon:' + $voiceFlowIcon }
& $voiceFlowCompiler @voiceFlowOptions @voiceFlowSources
if ($LASTEXITCODE -ne 0) { throw 'Voice Flow build failed.' }
