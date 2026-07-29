using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoInstall
{
    // Abertura: SOMENTE o Guaxinim recortado (PNG com transparencia) e o texto
    // logo abaixo dele, sem moldura nem retangulo de janela. Isso exige uma
    // janela em camada (WS_EX_LAYERED + UpdateLayeredWindow), a unica forma de
    // o Windows desenhar uma janela com transparencia por pixel: o recorte
    // aparece direto sobre a area de trabalho.
    // Fade in de 5s, pausa e fade out de 5s; um clique pula direto para o fim
    // (clicar sobre o site abre o navegador em vez de pular).
    public class SplashGuaxinim : Form
    {
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int ULW_ALPHA = 0x00000002;
        const int AC_SRC_OVER = 0x00;
        const int AC_SRC_ALPHA = 0x01;
        const int WM_LBUTTONDOWN = 0x0201;

        [StructLayout(LayoutKind.Sequential)]
        struct PONTO { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct TAMANHO { public int Cx; public int Cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct MISTURA
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PONTO pptDst,
            ref TAMANHO psize, IntPtr hdcSrc, ref PONTO pptSrc, int crKey,
            ref MISTURA pblend, int dwFlags);

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr ho);

        readonly int entradaMs, pausaMs, saidaMs;
        Bitmap quadro;
        IntPtr dcMemoria = IntPtr.Zero, hBitmap = IntPtr.Zero, hAntigo = IntPtr.Zero;
        Rectangle areaSite;
        Timer timer;
        Stopwatch relogio;
        long deslocamentoMs;
        bool pulando;

        public SplashGuaxinim(int entradaMs, int pausaMs, int saidaMs)
        {
            this.entradaMs = entradaMs;
            this.pausaMs = pausaMs;
            this.saidaMs = saidaMs;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            quadro = MontarQuadro(out areaSite);
            Premultiplicar(quadro);
            Size = quadro.Size;
            Rectangle tela = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(tela.X + (tela.Width - Width) / 2,
                                 tela.Y + (tela.Height - Height) / 2);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        // Desenha o Guaxinim e os creditos num bitmap ARGB; nada de controles,
        // porque numa janela em camada quem manda e o bitmap inteiro.
        // Publico e estatico para poder ser conferido fora do app (previa).
        public static Bitmap MontarQuadro(out Rectangle areaSite)
        {
            Image guax = Recursos.CarregarGuaxinim();
            const int ALTURA_IMAGEM = 430;
            int larguraImagem = guax != null
                ? (int)Math.Round(guax.Width * (ALTURA_IMAGEM / (double)guax.Height))
                : 380;

            string credito = "Criado por Maicon Nunes, da Smells Like Tech Informática";
            var fonteCredito = new Font("Segoe UI", 12f);
            var fonteSite = new Font("Segoe UI Semibold", 12f, FontStyle.Underline);

            SizeF medCredito, medSite;
            using (var medidor = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(medidor))
            {
                medCredito = g.MeasureString(credito, fonteCredito);
                medSite = g.MeasureString(Tema.SITE_TEXTO, fonteSite);
            }

            int largura = (int)Math.Max(larguraImagem, Math.Max(medCredito.Width, medSite.Width)) + 60;
            int alturaTexto = (int)(medCredito.Height + medSite.Height) + 26;
            int altura = ALTURA_IMAGEM + alturaTexto + 24;

            var bmp = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (guax != null)
                    g.DrawImage(guax, (largura - larguraImagem) / 2, 0, larguraImagem, ALTURA_IMAGEM);

                int y = ALTURA_IMAGEM + 12;
                float xCredito = (largura - medCredito.Width) / 2f;
                Sombra(g, credito, fonteCredito, xCredito, y);
                using (var pincel = new SolidBrush(Color.FromArgb(240, 240, 240)))
                    g.DrawString(credito, fonteCredito, pincel, xCredito, y);

                y += (int)medCredito.Height + 6;
                float xSite = (largura - medSite.Width) / 2f;
                Sombra(g, Tema.SITE_TEXTO, fonteSite, xSite, y);
                using (var pincel = new SolidBrush(Tema.Laranja))
                    g.DrawString(Tema.SITE_TEXTO, fonteSite, pincel, xSite, y);

                areaSite = new Rectangle((int)xSite - 8, y - 4,
                    (int)medSite.Width + 16, (int)medSite.Height + 8);
            }

            fonteCredito.Dispose();
            fonteSite.Dispose();
            return bmp;
        }

        // Halo escuro atras do texto: a janela nao tem fundo proprio, entao o
        // texto cai direto sobre o papel de parede do cliente. Sem um halo
        // forte, letras claras somem sobre papel de parede claro. Duas camadas:
        // uma larga e suave (brilho) e uma colada (contorno).
        static void Sombra(Graphics g, string texto, Font fonte, float x, float y)
        {
            using (var largo = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                for (int dx = -4; dx <= 4; dx++)
                    for (int dy = -4; dy <= 4; dy++)
                        if (dx * dx + dy * dy > 4 && dx * dx + dy * dy <= 18)
                            g.DrawString(texto, fonte, largo, x + dx, y + dy);

            using (var colado = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                        if (dx != 0 || dy != 0)
                            g.DrawString(texto, fonte, colado, x + dx, y + dy);
        }

        // UpdateLayeredWindow com AC_SRC_ALPHA exige alfa pre-multiplicado.
        static void Premultiplicar(Bitmap bmp)
        {
            var area = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData dados = bmp.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int total = bmp.Width * bmp.Height * 4;
            byte[] buf = new byte[total];
            Marshal.Copy(dados.Scan0, buf, 0, total);
            for (int i = 0; i < total; i += 4)
            {
                int a = buf[i + 3];
                if (a == 255) continue;
                buf[i] = (byte)(buf[i] * a / 255);
                buf[i + 1] = (byte)(buf[i + 1] * a / 255);
                buf[i + 2] = (byte)(buf[i + 2] * a / 255);
            }
            Marshal.Copy(buf, 0, dados.Scan0, total);
            bmp.UnlockBits(dados);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            IntPtr dcTela = GetDC(IntPtr.Zero);
            dcMemoria = CreateCompatibleDC(dcTela);
            hBitmap = quadro.GetHbitmap(Color.FromArgb(0));
            hAntigo = SelectObject(dcMemoria, hBitmap);
            ReleaseDC(IntPtr.Zero, dcTela);

            Desenhar(0);
            relogio = Stopwatch.StartNew();
            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += delegate { Passo(); };
            timer.Start();
        }

        void Desenhar(byte alfa)
        {
            if (dcMemoria == IntPtr.Zero) return;
            IntPtr dcTela = GetDC(IntPtr.Zero);
            var origem = new PONTO();
            var destino = new PONTO();
            destino.X = Left;
            destino.Y = Top;
            var tamanho = new TAMANHO();
            tamanho.Cx = quadro.Width;
            tamanho.Cy = quadro.Height;
            var mistura = new MISTURA();
            mistura.BlendOp = AC_SRC_OVER;
            mistura.SourceConstantAlpha = alfa;
            mistura.AlphaFormat = AC_SRC_ALPHA;
            UpdateLayeredWindow(Handle, dcTela, ref destino, ref tamanho, dcMemoria,
                ref origem, 0, ref mistura, ULW_ALPHA);
            ReleaseDC(IntPtr.Zero, dcTela);
        }

        void Passo()
        {
            long t = deslocamentoMs + relogio.ElapsedMilliseconds;
            long total = entradaMs + pausaMs + saidaMs;
            if (t >= total)
            {
                timer.Stop();
                Close();
                return;
            }
            double op;
            if (t < entradaMs) op = t / (double)entradaMs;
            else if (t < entradaMs + pausaMs) op = 1.0;
            else op = 1.0 - (t - entradaMs - pausaMs) / (double)saidaMs;
            if (op < 0) op = 0;
            if (op > 1) op = 1;
            Desenhar((byte)(op * 255));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDOWN)
            {
                int x = (short)((int)m.LParam & 0xFFFF);
                int y = (short)(((int)m.LParam >> 16) & 0xFFFF);
                if (areaSite.Contains(x, y)) Tema.AbrirSite();
                else Pular();
                return;
            }
            base.WndProc(ref m);
        }

        void Pular()
        {
            if (pulando) return;
            pulando = true;
            long t = deslocamentoMs + relogio.ElapsedMilliseconds;
            long inicioSaida = entradaMs + pausaMs;
            if (t < inicioSaida)
            {
                deslocamentoMs = inicioSaida;
                relogio = Stopwatch.StartNew();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null) timer.Dispose();
                if (dcMemoria != IntPtr.Zero)
                {
                    SelectObject(dcMemoria, hAntigo);
                    DeleteObject(hBitmap);
                    DeleteDC(dcMemoria);
                    dcMemoria = IntPtr.Zero;
                }
                if (quadro != null) quadro.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
