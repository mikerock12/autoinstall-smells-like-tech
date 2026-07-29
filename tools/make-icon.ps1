# Gera icon.ico a partir de Guaxinim.jpg (recorte quadrado do rosto,
# redimensionado em varios tamanhos, PNGs embutidos num container ICO).
# Roda no Windows PowerShell 5.1 (powershell.exe).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz = Split-Path -Parent $PSScriptRoot
$origem = Join-Path $raiz 'Guaxinim.jpg'
$alvo = Join-Path $raiz 'icon.ico'
if (Test-Path $alvo) { Write-Host "Ja existe: $alvo"; exit 0 }

$img = [System.Drawing.Image]::FromFile($origem)
try {
    # Recorte quadrado (largura x largura) a partir do topo, pegando o rosto
    $lado = [Math]::Min($img.Width, $img.Height)
    $y = [int]($img.Height * 0.05)
    if ($y + $lado -gt $img.Height) { $y = 0 }

    $quadrado = New-Object System.Drawing.Bitmap($lado, $lado)
    $g = [System.Drawing.Graphics]::FromImage($quadrado)
    $g.DrawImage($img,
        (New-Object System.Drawing.Rectangle(0, 0, $lado, $lado)),
        (New-Object System.Drawing.Rectangle(0, $y, $lado, $lado)),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    $tamanhos = @(256, 128, 64, 48, 32, 16)
    $pngs = @()
    foreach ($t in $tamanhos) {
        $bmp = New-Object System.Drawing.Bitmap($t, $t)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($quadrado, 0, 0, $t, $t)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs += ,@($t, $ms.ToArray())
        $ms.Dispose()
    }

    $fs = [System.IO.File]::Create($alvo)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0)                # reservado
    $bw.Write([UInt16]1)                # tipo: icone
    $bw.Write([UInt16]$pngs.Count)      # quantidade
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $t = $p[0]; $dados = $p[1]
        $bw.Write([Byte]($(if ($t -ge 256) { 0 } else { $t })))  # largura (0 = 256)
        $bw.Write([Byte]($(if ($t -ge 256) { 0 } else { $t })))  # altura
        $bw.Write([Byte]0)              # cores na paleta
        $bw.Write([Byte]0)              # reservado
        $bw.Write([UInt16]1)            # planos
        $bw.Write([UInt16]32)           # bits por pixel
        $bw.Write([UInt32]$dados.Length)
        $bw.Write([UInt32]$offset)
        $offset += $dados.Length
    }
    foreach ($p in $pngs) { $bw.Write([Byte[]]$p[1]) }
    $bw.Flush(); $bw.Close()
    Write-Host "OK: $alvo gerado."
}
finally {
    $img.Dispose()
}
