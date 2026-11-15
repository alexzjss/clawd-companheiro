# Compila os Clawds da area de trabalho e a Central dos Clawds com o csc do .NET Framework 4
# (vem com o Windows). Uso: powershell -File build.ps1 [-Out <pasta>] [-Only desktop|central] [-Setup]
param(
  [string]$Out = (Join-Path $env:USERPROFILE '.claude\clawd\app'),
  [string]$Only = '',
  [switch]$Setup   # tambem monta o instalador ClawdSetup.exe, com os dois programas dentro
)

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $fw 'csc.exe'))) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
if (-not (Test-Path (Join-Path $fw 'csc.exe'))) { Write-Error 'Nao achei o csc.exe do .NET Framework 4.'; exit 1 }
$wpf = Join-Path $fw 'WPF'
New-Item -ItemType Directory -Force $Out | Out-Null

$refs = @(
  (Join-Path $wpf 'PresentationCore.dll'),
  (Join-Path $wpf 'PresentationFramework.dll'),
  (Join-Path $wpf 'WindowsBase.dll'),
  (Join-Path $fw 'System.Xaml.dll'),
  (Join-Path $fw 'System.Management.dll'),
  (Join-Path $fw 'System.Web.Extensions.dll'),
  (Join-Path $fw 'System.Windows.Forms.dll'),
  (Join-Path $fw 'System.Drawing.dll'),
  (Join-Path $fw 'Microsoft.CSharp.dll'),
  (Join-Path $fw 'System.Core.dll')
) | ForEach-Object { "/r:$_" }

# O desenho do Clawd e a config sao os mesmos nos dois programas.
$shared = @('ClawdSprite.cs', 'ClawdConfig.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$icon = Join-Path $PSScriptRoot 'clawd.ico'

function Build([string]$name, [string]$source) {
  $exe = Join-Path $Out "$name.exe"
  $cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/nowarn:0649', "/out:$exe") + $refs
  if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
  $cscArgs += (Join-Path $PSScriptRoot $source)
  $cscArgs += $shared
  & (Join-Path $fw 'csc.exe') @cscArgs
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  Write-Output "ok: $exe"
}

if ($Only -ne 'central') { Build 'ClawdDesktop' 'ClawdDesktop.cs' }
if ($Only -ne 'desktop') { Build 'ClawdCentral' 'ClawdCentral.cs' }

if ($Setup) {
  $exe = Join-Path $Out 'ClawdSetup.exe'
  $cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', "/out:$exe") + $refs
  foreach ($p in 'ClawdDesktop.exe', 'ClawdCentral.exe') { $cscArgs += "/resource:$(Join-Path $Out $p),$p" }
  if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
  $cscArgs += (Join-Path $PSScriptRoot 'ClawdSetup.cs')
  & (Join-Path $fw 'csc.exe') @cscArgs
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  Write-Output "ok: $exe"
}
