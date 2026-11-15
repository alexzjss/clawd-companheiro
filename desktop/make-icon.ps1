# Desenha o icone dos programas: um Clawd branco em pixels num quadrado preto.
# Gera clawd.ico (16 a 256 px) e docs\icon.png. Uso: powershell -File make-icon.ps1
Add-Type -AssemblyName System.Drawing

$art = @(
  '.##########.',
  '.##########.',
  '.##.####.##.',
  '###.####.###',
  '###.####.###',
  '.##########.',
  '.##########.',
  '.#.#....#.#.',
  '.#.#....#.#.'
)
$sizes = 16, 24, 32, 48, 64, 128, 256

function Draw([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.SmoothingMode = 'AntiAlias'
  $d = 2 * [Math]::Max(2, [int]($s * 0.22))
  $e = $s - $d
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($e, 0, $d, $d, 270, 90)
  $path.AddArc($e, $e, $d, $d, 0, 90); $path.AddArc(0, $e, $d, $d, 90, 90)
  $path.CloseFigure()
  $g.FillPath([System.Drawing.Brushes]::Black, $path)
  # o Clawd em celulas inteiras, para ficar nitido em qualquer tamanho
  $g.SmoothingMode = 'None'
  $cell = [Math]::Max(1, [Math]::Floor($s * 0.74 / 12))
  $ox = [Math]::Floor(($s - 12 * $cell) / 2)
  $oy = [Math]::Floor(($s - 9 * $cell) / 2)
  for ($y = 0; $y -lt $art.Count; $y++) {
    for ($x = 0; $x -lt 12; $x++) {
      if ($art[$y][$x] -eq '#') { $g.FillRectangle([System.Drawing.Brushes]::White, $ox + $x * $cell, $oy + $y * $cell, $cell, $cell) }
    }
  }
  $g.Dispose()
  return $bmp
}

$pngs = @()
foreach ($s in $sizes) {
  $bmp = Draw $s
  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , $ms.ToArray()
  if ($s -eq 256) {
    $docs = Join-Path (Split-Path $PSScriptRoot -Parent) 'docs'
    New-Item -ItemType Directory -Force $docs | Out-Null
    $bmp.Save((Join-Path $docs 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  }
  $bmp.Dispose()
}

# O .ico: um cabecalho, uma entrada por tamanho e os PNGs em seguida.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $b = [byte]($sizes[$i] -band 255)   # 256 vira 0
  $w.Write($b); $w.Write($b); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Flush()
$ico = Join-Path $PSScriptRoot 'clawd.ico'
[IO.File]::WriteAllBytes($ico, $out.ToArray())
Write-Output "ok: $ico"
