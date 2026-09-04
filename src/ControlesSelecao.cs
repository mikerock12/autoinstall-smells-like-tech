using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoInstall
{
    // Base dos itens marcaveis da primeira tela. O CheckBox do WinForms nao
    // aceita tema: o quadradinho vem do sistema, branco, e destoa do fundo
    // escuro. Aqui a caixa e desenhada junto com o resto do item, e o clique
    // vale na linha inteira — bem mais facil de acertar.
    public abstract class ItemMarcavel : Control
    {
        bool marcado;
        protected bool SobreOMouse;

        public event Action AoMudar;

        protected ItemMarcavel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            // Mesmo fundo do cartao que os contem: com a cor da janela, cada
            // item vira um retangulo escuro visivel dentro do painel claro.
            BackColor = Tema.Cartao;
        }

        public bool Marcado
        {
            get { return marcado; }
            set
            {
                if (marcado == value) return;
                marcado = value;
                Invalidate();
                var h = AoMudar;
                if (h != null) h();
            }
        }

        // Troca sem disparar o evento — usado pelos botoes "Recomendados",
        // "Todos" e "Limpar", que avisam o total uma vez so no fim.
        public void DefinirSemAvisar(bool v)
        {
            marcado = v;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SobreOMouse = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SobreOMouse = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Marcado = !Marcado;
        }

        // Caixa de marcacao: contorno cinza quando vazia, preenchida de laranja
        // com o "certo" branco quando marcada.
        protected void DesenharCaixa(Graphics g, int x, int y, int lado)
        {
            var r = new Rectangle(x, y, lado, lado);
            if (Marcado)
            {
                using (var b = new SolidBrush(Enabled ? Tema.Laranja : Tema.Borda))
                    g.FillRectangle(b, r);
                using (var caneta = new Pen(Enabled ? Color.FromArgb(24, 16, 6) : Tema.TextoSuave, 2f))
                {
                    caneta.StartCap = LineCap.Round;
                    caneta.EndCap = LineCap.Round;
                    var suave = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLines(caneta, new Point[]
                    {
                        new Point(x + lado * 22 / 100, y + lado / 2),
                        new Point(x + lado * 42 / 100, y + lado * 70 / 100),
                        new Point(x + lado * 78 / 100, y + lado * 28 / 100)
                    });
                    g.SmoothingMode = suave;
                }
            }
            else
            {
                using (var b = new SolidBrush(Color.FromArgb(18, 20, 26)))
                    g.FillRectangle(b, r);
                using (var caneta = new Pen(SobreOMouse && Enabled ? Tema.Laranja : Tema.Borda))
                    g.DrawRectangle(caneta, r);
            }
        }

        protected static void Texto(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, s, f, r, c,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        protected static void TextoDireita(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, s, f, r, c,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    // Uma das tres etapas da primeira tela: caixa grande, titulo e explicacao.
    public class CartaoEtapa : ItemMarcavel
    {
        public static readonly Font FonteTitulo = new Font("Segoe UI Semibold", 12.5f);
        public static readonly Font FonteTexto = new Font("Segoe UI", 9.25f);

        readonly string numero;
        readonly string titulo;
        readonly string explicacao;

        // Quando true, o cartao nao pinta a propria borda: ele e o cabecalho
        // de um painel maior, que ja tem a dele (o caso dos programas).
        public bool SemBorda;

        public CartaoEtapa(string numero, string titulo, string explicacao)
        {
            this.numero = numero;
            this.titulo = titulo;
            this.explicacao = explicacao;
            Height = 84;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            var area = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var b = new SolidBrush(SobreOMouse ? Tema.CartaoHover : Tema.Cartao))
                g.FillRectangle(b, ClientRectangle);
            if (!SemBorda)
                using (var caneta = new Pen(Marcado ? Tema.LaranjaBorda : Tema.Borda))
                    g.DrawRectangle(caneta, area);

            // Faixa laranja na lateral esquerda marca a etapa escolhida.
            if (Marcado)
                using (var b = new SolidBrush(Tema.Laranja))
                    g.FillRectangle(b, 0, 0, 4, Height);

            DesenharCaixa(g, 24, 21, 22);

            Texto(g, string.IsNullOrEmpty(numero) ? titulo : numero + ". " + titulo, FonteTitulo,
                Marcado ? Tema.Texto : Tema.TextoSuave, new Rectangle(62, 16, Width - 86, 26));

            // Altura do texto acompanha a do cartao: os cartoes de etapa tem
            // 84 px e duas linhas; um cartao mais alto ganha mais linhas sem
            // precisar de outra classe.
            TextRenderer.DrawText(g, explicacao, FonteTexto,
                new Rectangle(62, 42, Width - 86, Height - 48), Tema.TextoSuave,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Top);
        }
    }

    // Um programa do catalogo: caixa, nome, por onde ele e instalado e o
    // resumo de uma linha que ajuda o tecnico a decidir.
    public class ItemPrograma : ItemMarcavel
    {
        public static readonly Font FonteNome = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font FonteVia = new Font("Segoe UI", 8f);
        public static readonly Font FonteDesc = new Font("Segoe UI", 8.25f);

        public readonly AppCatalogo App;

        public ItemPrograma(AppCatalogo app)
        {
            App = app;
            Height = 46;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (SobreOMouse && Enabled)
                using (var b = new SolidBrush(Tema.CartaoHover))
                    g.FillRectangle(b, ClientRectangle);

            DesenharCaixa(g, 2, 7, 16);

            Color corNome = !Enabled ? Tema.Borda : (Marcado ? Tema.Texto : Tema.TextoSuave);
            Color corDesc = !Enabled ? Tema.Borda : Tema.TextoSuave;
            Color corVia = !Enabled ? Tema.Borda : Color.FromArgb(120, 126, 136);

            int larguraVia = TextRenderer.MeasureText(App.Vias, FonteVia).Width + 6;
            int fimNome = Width - larguraVia - 30;

            Texto(g, App.Nome, FonteNome, corNome, new Rectangle(28, 3, fimNome, 20));
            TextoDireita(g, App.Vias, FonteVia, corVia,
                new Rectangle(Width - larguraVia - 24, 4, larguraVia, 18));
            Texto(g, App.Descricao, FonteDesc, corDesc, new Rectangle(28, 23, Width - 32, 18));
        }
    }
}
