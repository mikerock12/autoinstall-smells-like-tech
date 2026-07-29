# Gera icon.ico a partir do logo recortado (assets\guaxinim.png).
#
# O icone mostra so a cabeca do Guaxinim: em 16x16 o personagem inteiro vira
# uma mancha. A cabeca e localizada pela propria transparencia do PNG - acha a
# caixa do que e opaco e recorta um quadrado na faixa de cima dela -, entao
# isso continua funcionando se o logo for trocado por outro recorte.
# Roda no Windows PowerShell 5.1 (powershell.exe).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz = Split-Path -Parent $PSScriptRoot
$origem = Join-Path $raiz 'assets\guaxinim.png'
$alvo = Join-Path $raiz 'icon.ico'
if (Test-Path $alvo) { Write-Host "Ja existe: $alvo"; exit 0 }
if (-not (Test-Path $origem)) { Write-Error "Coloque o logo recortado em $origem"; exit 1 }

$codigo = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;

public static class Cabeca
{
    const double FAIXA = 0.38;   // fracao do alto do sujeito onde esta a cabeca
    const int ALFA_MIN = 25;

    // Devolve o quadrado (x,y,lado) que enquadra a cabeca.
    public static int[] Achar(string caminho)
    {
        using (var bmp = new Bitmap(caminho))
        {
            int w = bmp.Width, h = bmp.Height;
            var dados = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            byte[] buf = new byte[w * h * 4];
            System.Runtime.InteropServices.Marshal.Copy(dados.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(dados);

            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (buf[(y * w + x) * 4 + 3] > ALFA_MIN)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
            if (x1 < 0) throw new Exception("PNG sem nada opaco.");

            int fim = y0 + (int)((y1 - y0 + 1) * FAIXA);
            int fx0 = w, fx1 = -1;
            for (int y = y0; y <= fim; y++)
                for (int x = 0; x < w; x++)
                    if (buf[(y * w + x) * 4 + 3] > ALFA_MIN)
                    {
                        if (x < fx0) fx0 = x;
                        if (x > fx1) fx1 = x;
                    }

            int larguraCabeca = fx1 - fx0 + 1;
            int alturaCabeca = fim - y0 + 1;
            int lado = Math.Max(larguraCabeca, alturaCabeca);
            lado += lado / 12;                       // respiro em volta
            int cx = (fx0 + fx1) / 2;
            int qx = cx - lado / 2;
            int qy = y0 - lado / 20;
            Console.WriteLine("Sujeito: " + x0 + "," + y0 + " ate " + x1 + "," + y1 +
                              "  ->  cabeca: " + qx + "," + qy + " lado " + lado);
            return new int[] { qx, qy, lado };
        }
    }
}
'@
Add-Type -TypeDefinition $codigo -ReferencedAssemblies System.Drawing

$q = [Cabeca]::Achar($origem)
$img = New-Object System.Drawing.Bitmap($origem)
try {
    $quadrado = New-Object System.Drawing.Bitmap($q[2], $q[2])
    $g = [System.Drawing.Graphics]::FromImage($quadrado)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img,
        (New-Object System.Drawing.Rectangle(0, 0, $q[2], $q[2])),
        (New-Object System.Drawing.Rectangle($q[0], $q[1], $q[2], $q[2])),
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
    $quadrado.Dispose()

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
