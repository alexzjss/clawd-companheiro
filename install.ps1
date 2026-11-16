# Instala direto do código, sem o instalador: compila, cria os atalhos e abre o app.
#   powershell -ExecutionPolicy Bypass -File install.ps1              instala ou atualiza
#   powershell -ExecutionPolicy Bypass -File install.ps1 -NoStartup   sem abrir com o Windows
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall   remove programa e atalhos
param([switch]$Uninstall, [switch]$NoStartup)

$app = Join-Path $env:USERPROFILE '.claude\clawd\app'
$menu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$lnk = Join-Path $menu 'Clawdesk.lnk'
$boot = Join-Path $menu 'Startup\Clawdesk.lnk'
$old = @('Clawd.lnk', 'Central dos Clawds.lnk', 'Startup\Clawd.lnk') | ForEach-Object { Join-Path $menu $_ }

Get-Process Clawd, ClawdDesktop, ClawdCentral -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
foreach ($p in $old) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
foreach ($exe in 'ClawdDesktop.exe', 'ClawdCentral.exe') { Remove-Item -LiteralPath (Join-Path $app $exe) -Force -ErrorAction SilentlyContinue }

if ($Uninstall) {
  foreach ($p in $lnk, $boot) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
  Remove-Item -LiteralPath (Join-Path $app 'Clawdesk.exe') -Force -ErrorAction SilentlyContinue
  Write-Output 'Removido. As suas escolhas continuam em ~\.claude\clawd.'
  exit 0
}

& (Join-Path $PSScriptRoot 'desktop\build.ps1') -Out $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exePath = Join-Path $app 'Clawdesk.exe'
$shell = New-Object -ComObject WScript.Shell
foreach ($l in @(@{ Path = $lnk; Args = '' }, @{ Path = $boot; Args = '--fundo' })) {
  if ($NoStartup -and $l.Path -eq $boot) { Remove-Item -LiteralPath $boot -Force -ErrorAction SilentlyContinue; continue }
  $s = $shell.CreateShortcut($l.Path)
  $s.TargetPath = $exePath
  $s.Arguments = $l.Args
  $s.WorkingDirectory = $app
  $s.Description = 'Os Clawds e as sessoes do Claude'
  $s.Save()
}

Start-Process $exePath
Write-Output 'Pronto. O Clawdesk abriu e fica na bandeja do Windows.'
