# Gera icon.ico a partir do recorte transparente (assets\guaxinim.png):
# a cabeca do Guaxinim, centralizada num quadrado, com fundo transparente.
# Roda no Windows PowerShell 5.1 (powershell.exe).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz = Split-Path -Parent $PSScriptRoot
$origem = Join-Path $raiz 'assets\guaxinim.png'
$alvo = Join-Path $raiz 'icon.ico'
if (Test-Path $alvo) { Write-Host "Ja existe: $alvo"; exit 0 }
if (-not (Test-Path $origem)) { Write-Error "Gere antes o $origem (tools\make-guaxinim-png.ps1)"; exit 1 }

$img = New-Object System.Drawing.Bitmap($origem)
try {
    # So a cabeca: os 62% de cima do recorte, num quadrado centralizado.
    $alturaCabeca = [int]($img.Height * 0.62)
    $lado = [Math]::Max($alturaCabeca, $img.Width)
    $quadrado = New-Object System.Drawing.Bitmap($lado, $lado)
    $g = [System.Drawing.Graphics]::FromImage($quadrado)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img,
        (New-Object System.Drawing.Rectangle(([int](($lado - $img.Width) / 2)), 0, $img.Width, $alturaCabeca)),
        (New-Object System.Drawing.Rectangle(0, 0, $img.Width, $alturaCabeca)),
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
