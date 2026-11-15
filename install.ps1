# Instala os programas da area de trabalho: compila, cria os atalhos e liga os Clawds.
#   powershell -ExecutionPolicy Bypass -File install.ps1              instala ou atualiza
#   powershell -ExecutionPolicy Bypass -File install.ps1 -NoStartup   sem abrir com o Windows
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall   remove programas e atalhos
param([switch]$Uninstall, [switch]$NoStartup)

$app = Join-Path $env:USERPROFILE '.claude\clawd\app'
$menu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$links = @(
  @{ Path = (Join-Path $menu 'Clawd.lnk'); Exe = 'ClawdDesktop.exe'; Text = 'Os Clawds na area de trabalho' },
  @{ Path = (Join-Path $menu 'Central dos Clawds.lnk'); Exe = 'ClawdCentral.exe'; Text = 'Contas do Claude, temas e Clawds' },
  @{ Path = (Join-Path $menu 'Startup\Clawd.lnk'); Exe = 'ClawdDesktop.exe'; Text = 'Os Clawds na area de trabalho' }
)

Get-Process ClawdDesktop, ClawdCentral -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

if ($Uninstall) {
  foreach ($l in $links) { Remove-Item -LiteralPath $l.Path -Force -ErrorAction SilentlyContinue }
  foreach ($exe in 'ClawdDesktop.exe', 'ClawdCentral.exe') { Remove-Item -LiteralPath (Join-Path $app $exe) -Force -ErrorAction SilentlyContinue }
  Write-Output 'Removido. As suas escolhas continuam em ~\.claude\clawd\config.json.'
  exit 0
}

& (Join-Path $PSScriptRoot 'desktop\build.ps1') -Out $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$shell = New-Object -ComObject WScript.Shell
foreach ($l in $links) {
  if ($NoStartup -and $l.Path -like '*\Startup\*') {
    Remove-Item -LiteralPath $l.Path -Force -ErrorAction SilentlyContinue
    continue
  }
  $s = $shell.CreateShortcut($l.Path)
  $s.TargetPath = Join-Path $app $l.Exe
  $s.WorkingDirectory = $app
  $s.Description = $l.Text
  $s.Save()
}

Start-Process (Join-Path $app 'ClawdDesktop.exe')
Write-Output 'Pronto. Abra "Central dos Clawds" no menu Iniciar para escolher cores e skins.'
