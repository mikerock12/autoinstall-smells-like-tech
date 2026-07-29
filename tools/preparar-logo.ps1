# Prepara assets\guaxinim.png (o que vai embutido no executavel) a partir de
# assets\guaxinim-origem.png (o recorte original, preservado intacto).
#
# Duas coisas sao feitas aqui:
#
# 1) SANGRIA DE COR NAS BORDAS. O recorte veio de uma remocao de fundo verde:
#    os pixels 100% transparentes continuam guardando o verde do croma no RGB.
#    Isso e invisivel parado, mas o redimensionamento (a abertura desenha a
#    imagem menor) interpola RGB de vizinhos INCLUSIVE dos transparentes, e o
#    verde vaza como franja em volta do personagem. A correcao padrao e
#    espalhar a cor dos pixels opacos para dentro da area transparente,
#    mantendo o alfa como esta.
#
# 2) DESVANECIMENTO DAS BORDAS CORTADAS. A base do personagem (e um trecho da
#    lateral direita) e cortada reto pela moldura da imagem. Numa janela sem
#    fundo, flutuando sobre a area de trabalho, esse corte reto denuncia o
#    recorte; dissolvendo, o personagem "some" suavemente. So as bordas onde
#    ele realmente encosta sao tratadas - detectado automaticamente.
#
# Roda no Windows PowerShell 5.1 (powershell.exe).

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $raiz 'assets'
$origem = Join-Path $dir 'guaxinim-origem.png'
$alvo = Join-Path $dir 'guaxinim.png'

if (-not (Test-Path $origem)) { Write-Error "Faltando $origem"; exit 1 }

$codigo = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public static class Logo
{
    const int ALFA_OPACO = 250;
    const int PASSOS_SANGRIA = 14;     // quantos pixels a cor avanca para fora
    const double FADE_BASE = 0.11;     // fracao da altura dissolvida embaixo
    const int FADE_LATERAL = 40;       // largura da dissolucao lateral
    const int RAMPA_LATERAL = 80;      // subida suave antes de onde encosta

    public static void Preparar(string origem, string destino)
    {
        using (var src = new Bitmap(origem))
        {
            int w = src.Width, h = src.Height;
            var area = new Rectangle(0, 0, w, h);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(src, area);
            }

            BitmapData dados = bmp.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            byte[] px = new byte[w * h * 4];
            System.Runtime.InteropServices.Marshal.Copy(dados.Scan0, px, 0, px.Length);

            Sangrar(px, w, h);
            Dissolver(px, w, h);

            System.Runtime.InteropServices.Marshal.Copy(px, 0, dados.Scan0, px.Length);
            bmp.UnlockBits(dados);
            bmp.Save(destino, ImageFormat.Png);
            bmp.Dispose();
        }
    }

    // Espalha a cor dos opacos para os transparentes (alfa nao muda).
    static void Sangrar(byte[] px, int w, int h)
    {
        bool[] temCor = new bool[w * h];
        for (int i = 0; i < w * h; i++) temCor[i] = px[i * 4 + 3] >= ALFA_OPACO;

        int pintados = 0;
        for (int passo = 0; passo < PASSOS_SANGRIA; passo++)
        {
            var novos = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (temCor[i]) continue;
                    int somaB = 0, somaG = 0, somaR = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (!temCor[j]) continue;
                            somaB += px[j * 4];
                            somaG += px[j * 4 + 1];
                            somaR += px[j * 4 + 2];
                            n++;
                        }
                    if (n == 0) continue;
                    px[i * 4] = (byte)(somaB / n);
                    px[i * 4 + 1] = (byte)(somaG / n);
                    px[i * 4 + 2] = (byte)(somaR / n);
                    novos.Add(i);
                }
            foreach (int i in novos) temCor[i] = true;
            pintados += novos.Count;
            if (novos.Count == 0) break;
        }
        Console.WriteLine("Sangria de cor: " + pintados + " pixels transparentes receberam a cor da borda.");
    }

    static void Dissolver(byte[] px, int w, int h)
    {
        // Base: so se o personagem for cortado pela borda de baixo.
        if (Encosta(px, w, h, "baixo"))
        {
            int faixa = (int)(h * FADE_BASE);
            for (int y = h - faixa; y < h; y++)
            {
                int fator = 255 - 255 * (y - (h - faixa)) / faixa;
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4 + 3;
                    px[i] = (byte)(px[i] * fator / 255);
                }
            }
            Console.WriteLine("Base dissolvida nos ultimos " + faixa + " px.");
        }

        DissolverLateral(px, w, h, true);
        DissolverLateral(px, w, h, false);
    }

    static bool Encosta(byte[] px, int w, int h, string borda)
    {
        int y = h - 1;
        for (int x = 0; x < w; x++)
            if (px[(y * w + x) * 4 + 3] > 25) return true;
        return false;
    }

    static void DissolverLateral(byte[] px, int w, int h, bool esquerda)
    {
        int coluna = esquerda ? 0 : w - 1;
        int primeira = -1, ultima = -1;
        for (int y = 0; y < h; y++)
            if (px[(y * w + coluna) * 4 + 3] > 25)
            {
                if (primeira < 0) primeira = y;
                ultima = y;
            }
        if (primeira < 0)
        {
            Console.WriteLine((esquerda ? "Esquerda" : "Direita") + ": nao encosta, nada a fazer.");
            return;
        }

        for (int y = 0; y < h; y++)
        {
            // peso vertical: entra suave um pouco antes de onde encosta
            int peso;
            if (y >= primeira) peso = 255;
            else if (y >= primeira - RAMPA_LATERAL) peso = 255 * (y - (primeira - RAMPA_LATERAL)) / RAMPA_LATERAL;
            else continue;

            for (int d = 0; d < FADE_LATERAL; d++)
            {
                int x = esquerda ? d : w - 1 - d;
                int i = (y * w + x) * 4 + 3;
                int fatorH = 255 * d / FADE_LATERAL;             // 0 na borda, 255 para dentro
                int fator = 255 - (255 - fatorH) * peso / 255;   // so vale onde o peso manda
                px[i] = (byte)(px[i] * fator / 255);
            }
        }
        Console.WriteLine((esquerda ? "Esquerda" : "Direita") + ": encosta de y=" + primeira +
                          " a y=" + ultima + " - dissolvida em " + FADE_LATERAL + " px.");
    }
}
'@

Add-Type -TypeDefinition $codigo -ReferencedAssemblies System.Drawing
[Logo]::Preparar($origem, $alvo)
Write-Host "OK: $alvo gerado a partir de $origem."
