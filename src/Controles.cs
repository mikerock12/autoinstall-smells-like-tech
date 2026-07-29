using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AutoInstall
{
    // Identidade visual: fundo escuro + laranja, as cores do Guaxinim.
    public static class Tema
    {
        public static readonly Color Fundo = Color.FromArgb(13, 15, 19);
        public static readonly Color FundoEscuro = Color.FromArgb(9, 10, 13);
        public static readonly Color Laranja = Color.FromArgb(255, 140, 26);
        public static readonly Color LaranjaClaro = Color.FromArgb(255, 178, 102);
        public static readonly Color Texto = Color.FromArgb(235, 235, 235);
        public static readonly Color TextoSuave = Color.FromArgb(168, 173, 180);
        public static readonly Color Borda = Color.FromArgb(52, 56, 64);

        public const string SITE_URL = "https://www.smellsliketech.com.br";
        public const string SITE_TEXTO = "www.smellsliketech.com.br";
        public const string INSTA_URL = "https://www.instagram.com/smellsliketechinfo";
        public const string INSTA_TEXTO = "@smellsliketechinfo";

        public static void AbrirSite()
        {
            try { System.Diagnostics.Process.Start(SITE_URL); } catch { }
        }

        public static void AbrirInstagram()
        {
            try { System.Diagnostics.Process.Start(INSTA_URL); } catch { }
        }
    }

    // O Guaxinim recortado (PNG com transparencia), embutido no executavel.
    // E gerado do Guaxinim.jpg por tools\make-guaxinim-png.ps1.
    public static class Recursos
    {
        static Image guaxinim;
        static bool tentou;

        public static Image CarregarGuaxinim()
        {
            if (tentou) return guaxinim;
            tentou = true;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                Stream st = asm.GetManifestResourceStream("guaxinim.png");
                if (st != null) { guaxinim = Image.FromStream(st); return guaxinim; }
            }
            catch { }
            try
            {
                string pasta = Path.GetDirectoryName(Application.ExecutablePath);
                foreach (string nome in new string[] { "guaxinim.png", @"assets\guaxinim.png", "Guaxinim.jpg" })
                {
                    string p = Path.Combine(pasta, nome);
                    if (File.Exists(p)) { guaxinim = Image.FromFile(p); return guaxinim; }
                }
            }
            catch { }
            return null;
        }
    }

    // Desenha uma imagem centralizada (proporcao preservada) com transparencia
    // controlavel de 0 a 1 — usada nos fades de entrada e saida do Guaxinim.
    public class FadeImagem : Control
    {
        Image imagem;
        float alfa;

        public FadeImagem()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public Image Imagem
        {
            get { return imagem; }
            set { imagem = value; Invalidate(); }
        }

        public float Alfa
        {
            get { return alfa; }
            set
            {
                float v = value;
                if (v < 0f) v = 0f;
                if (v > 1f) v = 1f;
                if (v != alfa) { alfa = v; Invalidate(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (imagem == null || alfa <= 0f || Width <= 0 || Height <= 0) return;

            float escala = Math.Min(Width / (float)imagem.Width, Height / (float)imagem.Height);
            int w = (int)(imagem.Width * escala);
            int h = (int)(imagem.Height * escala);
            var destino = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);

            var cm = new ColorMatrix();
            cm.Matrix33 = alfa;
            using (var atributos = new ImageAttributes())
            {
                atributos.SetColorMatrix(cm);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(imagem, destino, 0, 0, imagem.Width, imagem.Height,
                    GraphicsUnit.Pixel, atributos);
            }
        }
    }

    // Barra de progresso no tema escuro/laranja com o percentual no centro.
    public class BarraProgresso : Control
    {
        int valor;

        public BarraProgresso()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
        }

        public int Valor
        {
            get { return valor; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (v != valor) { valor = v; Invalidate(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var fundo = new SolidBrush(Color.FromArgb(28, 31, 38)))
                g.FillRectangle(fundo, ClientRectangle);

            int w = (int)(Width * (valor / 100.0));
            if (w > 0)
                using (var frente = new SolidBrush(Tema.Laranja))
                    g.FillRectangle(frente, 0, 0, w, Height);

            using (var borda = new Pen(Tema.Borda))
                g.DrawRectangle(borda, 0, 0, Width - 1, Height - 1);

            string txt = valor + "%";
            using (var fonte = new Font("Segoe UI Semibold", 10f))
            {
                SizeF medida = g.MeasureString(txt, fonte);
                float x = (Width - medida.Width) / 2f;
                float y = (Height - medida.Height) / 2f;
                using (var sombra = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                    g.DrawString(txt, fonte, sombra, x + 1, y + 1);
                using (var pincel = new SolidBrush(Color.White))
                    g.DrawString(txt, fonte, pincel, x, y);
            }
        }
    }
}
