# Compila o Clawdesk (Clawdesk.exe) com o csc do .NET Framework 4, que vem com o
# Windows, e opcionalmente o instalador ClawdeskSetup.exe, com o programa dentro.
# A versão vem do arquivo VERSION, na raiz; o plugin.json precisa estar igual.
#   powershell -File build.ps1 [-Out <pasta>] [-Setup]
param(
  [string]$Out = (Join-Path $env:USERPROFILE '.claude\clawd\app'),
  [switch]$Setup   # também monta o instalador ClawdeskSetup.exe
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Write-Error "VERSION invalida: '$version'"; exit 1 }
$plugin = (Get-Content (Join-Path $root '.claude-plugin\plugin.json') -Raw | ConvertFrom-Json).version
if ($plugin -ne $version) { Write-Error "plugin.json esta na versao $plugin, mas VERSION diz $version."; exit 1 }

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
$icon = Join-Path $PSScriptRoot 'clawdesk.ico'

# Os atributos de versão de cada programa, gerados a partir do VERSION.
function VersionFile([string]$title) {
  $f = Join-Path ([IO.Path]::GetTempPath()) ("clawd-version-" + [Guid]::NewGuid().ToString('N') + '.cs')
  @"
[assembly: System.Reflection.AssemblyTitle("$title")]
[assembly: System.Reflection.AssemblyProduct("Clawdesk")]
[assembly: System.Reflection.AssemblyCompany("alexzjss")]
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@ | Set-Content -Encoding UTF8 $f
  return $f
}

function Compile([string]$exe, [string]$title, [string[]]$sources, [string[]]$extra) {
  $ver = VersionFile $title
  $cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/nowarn:0649', "/out:$exe") + $refs + $extra
  if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
  $cscArgs += ($sources | ForEach-Object { Join-Path $PSScriptRoot $_ })
  $cscArgs += $ver
  & (Join-Path $fw 'csc.exe') @cscArgs
  $code = $LASTEXITCODE
  Remove-Item $ver -ErrorAction SilentlyContinue
  if ($code -ne 0) { exit $code }
  Write-Output "ok: $exe ($version)"
}

$app = Join-Path $Out 'Clawdesk.exe'
Compile $app 'Clawdesk' @(
  'ClawdApp.cs', 'ClawdDesktop.cs', 'ClawdHome.cs', 'ClawdOffice.cs', 'ClawdHistory.cs', 'ClawdContas.cs',
  'ClawdServices.cs', 'ClawdUi.cs', 'ClawdShared.cs', 'ClawdSprite.cs', 'ClawdConfig.cs'
) @()

if ($Setup) {
  Compile (Join-Path $Out 'ClawdeskSetup.exe') 'Instalador do Clawdesk' @('ClawdeskSetup.cs', 'ClawdShared.cs') @("/resource:$app,Clawdesk.exe")
}
