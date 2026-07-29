# Gera assets\guaxinim.png: o Guaxinim recortado do Guaxinim.jpg, com fundo
# transparente, para a tela de abertura (janela sem moldura) e a tela final.
#
# Como o capuz preto e o fundo escuro tem quase o mesmo brilho, um limiar
# simples nao separa os dois. A estrategia e hibrida:
#   1) um poligono tracado a mao delimita a silhueta (grosseiro na cabeca,
#      justo no corpo) - tudo fora dele ja sai transparente;
#   2) dentro do poligono, um preenchimento por inundacao entra a partir da
#      borda pelos pixels escuros e apaga o fundo que sobrou. O limiar e alto
#      na regiao do pelo (fundo escuro x pelo claro) e baixo no corpo (capuz
#      preto), com limite de profundidade para a inundacao nao "vazar" para
#      dentro do capuz;
#   3) na faixa de transicao do pelo, o alfa vira proporcional ao brilho -
#      e isso que preserva bigodes e tufos das orelhas em vez de deixar a
#      borda recortada "na tesoura";
#   4) ilhas opacas soltas (faiscas do fundo) sao removidas e a base do corpo
#      se dissolve suavemente, ja que ela e cortada pela moldura da foto.
# Roda no Windows PowerShell 5.1 (powershell.exe).

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$origem = Join-Path $raiz 'Guaxinim.jpg'
$dirAssets = Join-Path $raiz 'assets'
if (-not (Test-Path $dirAssets)) { New-Item -ItemType Directory -Path $dirAssets | Out-Null }
$alvo = Join-Path $dirAssets 'guaxinim.png'

$codigo = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public static class Recortador
{
    // Silhueta aproximada (coordenadas da foto 784x1168), em sentido horario
    // a partir do topo da cabeca. Folgada em volta do pelo (a inundacao e o
    // matting cuidam do detalhe) e justa nos ombros e no braco.
    static readonly int[] POLIGONO = new int[] {
        255,185,  340,172,  430,168,  520,175,  580,188,  612,232,
        // lateral direita da cabeca: fica dentro de x=642 de proposito, para
        // deixar de fora a trilha laranja do circuito do fundo (x~645-665)
        628,300,  634,380,  638,460,  640,520,  642,560,
        // ombro direito descendo
        648,590,  658,625,  666,660,  672,690,  690,720,  706,755,  726,800,
        746,845,  762,880,  776,915,  784,945,  784,1075,
        // base
        0,1075,
        // braco esquerdo subindo
        0,985,  22,935,  50,895,  78,860,  95,815,  105,775,  112,742,
        // corredor da chave de fenda (haste em x~100-190) e lateral da cabeca
        100,700,  92,640,  88,560,  90,500,  96,470,  104,440,  114,400,
        124,355,  140,300,  168,240,  210,198
    };

    // Ate 550: so pelo claro contra fundo escuro (limiar alto pode ser usado).
    // Abaixo disso comeca a gola do capuz, escura como o fundo: se o limiar
    // agressivo chegasse ate la, a inundacao cortaria o pescoco e separaria a
    // cabeca do corpo.
    // Fronteira entre "regiao de pelo" (pelo claro sobre fundo escuro, onde da
    // para separar por brilho) e "regiao de capuz" (preto sobre fundo escuro,
    // onde nao da). Nao e uma reta: o capuz sobe atras da cabeca pela direita.
    // Pares x,y interpolados linearmente.
    // Ela passa logo ABAIXO do nariz e ACIMA da sombra do queixo: se descer
    // mais, a inundacao entra pela sombra sob a bochecha e come o focinho.
    static readonly int[] LINHA_PELO = new int[] {
        0,600,  170,600,  205,545,  300,545,  430,548,  490,542,
        540,528,  600,518,  645,545,  784,545
    };

    const int LIM_PELO = 70;       // na cabeca: escuro (<70) = fundo

    // No corpo o criterio NAO pode ser "mais escuro que X": medindo a foto, o
    // capuz preto tem brilho mediano 15 e o fundo colado nele, 29 (minimo 27)
    // -- ou seja, o fundo e MAIS CLARO que o capuz. Por isso aqui o fundo e
    // uma FAIXA: acima do capuz preto e abaixo do brilho das luzes de contorno
    // e das trilhas laranja, que funcionam como barreira natural da inundacao.
    const int FUNDO_MIN = 22, FUNDO_MAX = 75;
    // No corpo a inundacao so limpa uma faixa rasa junto do poligono (quem
    // define a silhueta ali e o poligono); no corredor da chave de fenda ela
    // pode ir fundo, porque tudo que e sujeito naquele trecho e claro (haste
    // laranja e pelo da pata) e o capuz preto barra sozinho.
    const int PROF_RASA = 12, PROF_FUNDA = 250;
    // Limite direito do corredor por altura: acompanha a borda da gola/peito
    // (que desce na diagonal). Um limite vertical fixo cortaria a gola reto.
    // Abaixo de 730 comeca a pata, cujo pelo escuro cai na faixa de "fundo" -
    // por isso o corredor termina ali.
    static readonly int[] BORDA_CORREDOR = new int[] {
        520,254,  600,248,  650,228,  690,206,  730,196
    };
    const int LO = 40, HI = 110;   // rampa de alfa do matting do pelo
    const int BANDA = 4;           // largura da faixa de matting (px)
    const int FADE_INI = 970, FADE_FIM = 1065;  // dissolucao da base
    const int FADE_LATERAL = 34;   // dissolucao das bordas cortadas pela foto
    const int Y_LATERAL = 700;     // ...valida so da altura do tronco para baixo

    public static void Gerar(string origem, string destino)
    {
        using (var jpg = new Bitmap(origem))
        {
            int w = jpg.Width, h = jpg.Height;
            var src = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(src)) g.DrawImage(jpg, 0, 0, w, h);

            byte[] px = Ler(src, w, h);
            int[] lum = new int[w * h];
            for (int i = 0; i < w * h; i++)
            {
                int b = px[i * 4], gr = px[i * 4 + 1], r = px[i * 4 + 2];
                lum[i] = (r * 299 + gr * 587 + b * 114) / 1000;
            }

            bool[] dentro = MascaraPoligono(w, h);
            bool[] fundo = Inundar(w, h, lum, dentro);
            byte[] alfa = MontarAlfa(w, h, lum, dentro, fundo);
            RemoverIlhas(w, h, alfa);
            Suavizar(w, h, alfa);
            DissolverBase(w, h, alfa);
            Aplicar(px, alfa, w * h);

            Escrever(src, px, w, h);
            using (Bitmap cortado = Cortar(src))
                cortado.Save(destino, ImageFormat.Png);
            src.Dispose();
        }
    }

    static byte[] Ler(Bitmap bmp, int w, int h)
    {
        var dados = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[w * h * 4];
        System.Runtime.InteropServices.Marshal.Copy(dados.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(dados);
        return buf;
    }

    static void Escrever(Bitmap bmp, byte[] buf, int w, int h)
    {
        var dados = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, dados.Scan0, buf.Length);
        bmp.UnlockBits(dados);
    }

    static bool[] MascaraPoligono(int w, int h)
    {
        var pontos = new Point[POLIGONO.Length / 2];
        for (int i = 0; i < pontos.Length; i++)
            pontos[i] = new Point(POLIGONO[i * 2], POLIGONO[i * 2 + 1]);

        using (var mascara = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(mascara))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.Clear(Color.Black);
                g.FillPolygon(Brushes.White, pontos);
            }
            byte[] buf = Ler(mascara, w, h);
            bool[] dentro = new bool[w * h];
            for (int i = 0; i < w * h; i++) dentro[i] = buf[i * 4] > 127;
            return dentro;
        }
    }

    static int LimitePelo(int x)
    {
        for (int i = 2; i < LINHA_PELO.Length; i += 2)
        {
            if (x > LINHA_PELO[i]) continue;
            int x0 = LINHA_PELO[i - 2], y0 = LINHA_PELO[i - 1];
            int x1 = LINHA_PELO[i], y1 = LINHA_PELO[i + 1];
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return LINHA_PELO[LINHA_PELO.Length - 1];
    }

    static bool ZonaPelo(int x, int y) { return y < LimitePelo(x); }

    static bool NoCorredor(int x, int y)
    {
        if (y < BORDA_CORREDOR[0] || y > BORDA_CORREDOR[BORDA_CORREDOR.Length - 2]) return false;
        for (int i = 2; i < BORDA_CORREDOR.Length; i += 2)
        {
            if (y > BORDA_CORREDOR[i]) continue;
            int y0 = BORDA_CORREDOR[i - 2], x0 = BORDA_CORREDOR[i - 1];
            int y1 = BORDA_CORREDOR[i], x1 = BORDA_CORREDOR[i + 1];
            return x < x0 + (x1 - x0) * (y - y0) / (y1 - y0);
        }
        return false;
    }

    static int ProfMax(int x, int y)
    {
        return NoCorredor(x, y) ? PROF_FUNDA : PROF_RASA;
    }

    static bool EhFundo(int x, int y, int lum)
    {
        if (ZonaPelo(x, y)) return lum < LIM_PELO;
        return lum >= FUNDO_MIN && lum <= FUNDO_MAX;
    }

    // Inundacao a partir da borda do poligono, so por pixels escuros.
    static bool[] Inundar(int w, int h, int[] lum, bool[] dentro)
    {
        bool[] fundo = new bool[w * h];
        int[] prof = new int[w * h];
        var fila = new Queue<int>();

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (dentro[i]) continue;
                // vizinhos de dentro do poligono viram sementes
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (!dentro[j] || fundo[j]) continue;
                    if (!EhFundo(nx, ny, lum[j])) continue;
                    fundo[j] = true;
                    prof[j] = ZonaPelo(nx, ny) ? 0 : 1;
                    fila.Enqueue(j);
                }
            }

        while (fila.Count > 0)
        {
            int i = fila.Dequeue();
            int x = i % w, y = i / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int j = ny * w + nx;
                if (fundo[j] || !dentro[j]) continue;
                if (!EhFundo(nx, ny, lum[j])) continue;
                int p = ZonaPelo(nx, ny) ? 0 : prof[i] + 1;
                if (p > ProfMax(nx, ny)) continue;
                fundo[j] = true;
                prof[j] = p;
                fila.Enqueue(j);
            }
        }
        return fundo;
    }

    static byte[] MontarAlfa(int w, int h, int[] lum, bool[] dentro, bool[] fundo)
    {
        // distancia (limitada) ate o fundo, para saber onde aplicar o matting
        int[] dist = new int[w * h];
        for (int i = 0; i < w * h; i++) dist[i] = int.MaxValue;
        var fila = new Queue<int>();
        for (int i = 0; i < w * h; i++)
            if (fundo[i] || !dentro[i]) { dist[i] = 0; fila.Enqueue(i); }
        while (fila.Count > 0)
        {
            int i = fila.Dequeue();
            if (dist[i] >= BANDA) continue;
            int x = i % w, y = i / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int j = ny * w + nx;
                if (dist[j] <= dist[i] + 1) continue;
                dist[j] = dist[i] + 1;
                fila.Enqueue(j);
            }
        }

        byte[] alfa = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!dentro[i] || fundo[i]) { alfa[i] = 0; continue; }
                if (ZonaPelo(x, y) && dist[i] <= BANDA)
                {
                    int v = lum[i];
                    if (v >= HI) alfa[i] = 255;
                    else if (v <= LO) alfa[i] = 0;
                    else alfa[i] = (byte)(255 * (v - LO) / (HI - LO));
                }
                else alfa[i] = 255;
            }
        return alfa;
    }

    // Remove pedacos opacos soltos (faiscas e ruido do fundo). Mantem o maior
    // componente - o proprio Guaxinim - e qualquer outro grande o bastante
    // (2% do maior), para nao perder partes que ficaram destacadas.
    static void RemoverIlhas(int w, int h, byte[] alfa)
    {
        int[] comp = new int[w * h];
        var tamanhos = new List<int>();
        tamanhos.Add(0);
        int atual = 0, maior = 0, tamMaior = 0;
        var fila = new Queue<int>();
        for (int s = 0; s < w * h; s++)
        {
            if (alfa[s] <= 10 || comp[s] != 0) continue;
            atual++;
            int tam = 0;
            comp[s] = atual;
            fila.Enqueue(s);
            while (fila.Count > 0)
            {
                int i = fila.Dequeue();
                tam++;
                int x = i % w, y = i / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (alfa[j] <= 10 || comp[j] != 0) continue;
                    comp[j] = atual;
                    fila.Enqueue(j);
                }
            }
            tamanhos.Add(tam);
            if (tam > tamMaior) { tamMaior = tam; maior = atual; }
        }
        int minimo = Math.Max(600, tamMaior / 50);
        for (int i = 0; i < w * h; i++)
            if (comp[i] != 0 && comp[i] != maior && tamanhos[comp[i]] < minimo) alfa[i] = 0;

        Console.Write("Componentes mantidos:");
        for (int c = 1; c < tamanhos.Count; c++)
            if (c == maior || tamanhos[c] >= minimo) Console.Write(" " + tamanhos[c] + "px");
        Console.WriteLine();
    }

    static void Suavizar(int w, int h, byte[] alfa)
    {
        byte[] copia = (byte[])alfa.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                int soma = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        soma += copia[i + dy * w + dx];
                alfa[i] = (byte)(soma / 9);
            }
    }

    // A base e as laterais do tronco nao sao silhueta: sao onde a moldura da
    // foto corta o Guaxinim. Um corte reto ali pareceria adesivo recortado,
    // entao essas bordas se dissolvem no transparente.
    static void DissolverBase(int w, int h, byte[] alfa)
    {
        for (int y = FADE_INI; y < h; y++)
        {
            int fator = y >= FADE_FIM ? 0 : 255 - 255 * (y - FADE_INI) / (FADE_FIM - FADE_INI);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                alfa[i] = (byte)(alfa[i] * fator / 255);
            }
        }
        for (int y = Y_LATERAL; y < h; y++)
            for (int x = 0; x < FADE_LATERAL; x++)
            {
                int fator = 255 * x / FADE_LATERAL;
                int i = y * w + x;
                alfa[i] = (byte)(alfa[i] * fator / 255);
                int d = y * w + (w - 1 - x);
                alfa[d] = (byte)(alfa[d] * fator / 255);
            }
    }

    static void Aplicar(byte[] px, byte[] alfa, int total)
    {
        for (int i = 0; i < total; i++) px[i * 4 + 3] = alfa[i];
    }

    static Bitmap Cortar(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        byte[] buf = Ler(src, w, h);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (buf[(y * w + x) * 4 + 3] > 4)
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
        if (x1 < 0) throw new Exception("Recorte vazio - revise o poligono.");
        x0 = Math.Max(0, x0 - 2); y0 = Math.Max(0, y0 - 2);
        x1 = Math.Min(w - 1, x1 + 2); y1 = Math.Min(h - 1, y1 + 2);
        Console.WriteLine("Recorte: " + (x1 - x0 + 1) + "x" + (y1 - y0 + 1) +
                          " (origem " + x0 + "," + y0 + ")");
        var destino = new Bitmap(x1 - x0 + 1, y1 - y0 + 1, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(destino))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(src, new Rectangle(0, 0, destino.Width, destino.Height),
                new Rectangle(x0, y0, destino.Width, destino.Height), GraphicsUnit.Pixel);
        }
        return destino;
    }
}
'@

Add-Type -TypeDefinition $codigo -ReferencedAssemblies System.Drawing
[Recortador]::Gerar($origem, $alvo)
Write-Host "OK: $alvo gerado."
