using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoInstall
{
    // Painel com rolagem e sem tremidas ao rolar (o Panel comum nao tem
    // buffer duplo e a lista de programas pisca inteira a cada roda do mouse).
    public class PainelRolagem : Panel
    {
        public PainelRolagem()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Tema.Fundo;
        }
    }

    // A primeira tela: o tecnico escolhe as etapas e, dentro da etapa de
    // instalacao, exatamente quais programas quer. Nada roda antes daqui.
    public class TelaSelecao : Panel
    {
        public event Action AoIniciar;

        const int MARGEM = 32;
        const int LARGURA = 836;   // 920 - margens - espaco da barra de rolagem

        readonly Dictionary<string, Label> titulos = new Dictionary<string, Label>();
        readonly ToolTip dicas = new ToolTip();
        TextBox buscaApps;
        ComboBox filtroCategoria;
        Label resultadoBusca;
        int inicioGrade;

        readonly CartaoEtapa cartaoUpdate;
        readonly CartaoEtapa cartaoApps;
        readonly CartaoEtapa cartaoUpgrade;
        readonly CartaoEtapa cartaoOusado;
        readonly Panel painelApps;
        readonly PainelRolagem rolagem;
        readonly Label lblResumo;
        readonly Button btnIniciar;
        readonly List<ItemPrograma> itens = new List<ItemPrograma>();

        public TelaSelecao()
        {
            BackColor = Tema.Fundo;

            var lblTitulo = new Label();
            lblTitulo.Text = "O que fazer nesta máquina?";
            lblTitulo.Font = new Font("Segoe UI Semibold", 17f);
            lblTitulo.ForeColor = Tema.Laranja;
            lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
            lblTitulo.SetBounds(0, 14, 920, 34);
            Controls.Add(lblTitulo);

            var lblSub = new Label();
            lblSub.Text = "Marque as etapas e os programas. Depois disso é tudo automático: o computador " +
                          "reinicia sozinho quantas vezes precisar e só para quando terminar.";
            lblSub.Font = new Font("Segoe UI", 9.75f);
            lblSub.ForeColor = Tema.TextoSuave;
            lblSub.TextAlign = ContentAlignment.TopCenter;
            lblSub.SetBounds(MARGEM, 50, 920 - MARGEM * 2, 38);
            Controls.Add(lblSub);

            rolagem = new PainelRolagem();
            rolagem.SetBounds(MARGEM, 92, 920 - MARGEM * 2, 438);
            Controls.Add(rolagem);

            // ---- Etapa 1: Windows Update ----
            cartaoUpdate = new CartaoEtapa("1", "Atualizar o Windows por completo",
                "Busca tudo o que está pendente, inclusive atualizações opcionais e drivers. " +
                "Reinicia e procura de novo, quantas vezes for preciso, até não sobrar nenhuma.");
            cartaoUpdate.Marcado = true;
            cartaoUpdate.SetBounds(0, 0, LARGURA, 84);
            cartaoUpdate.AoMudar += delegate { AtualizarResumo(); };
            rolagem.Controls.Add(cartaoUpdate);

            // ---- Etapa 2: programas escolhidos ----
            painelApps = new Panel();
            painelApps.BackColor = Tema.Cartao;
            painelApps.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var caneta = new Pen(cartaoApps.Marcado ? Tema.LaranjaBorda : Tema.Borda))
                    e.Graphics.DrawRectangle(caneta, 0, 0, painelApps.Width - 1, painelApps.Height - 1);
            };

            cartaoApps = new CartaoEtapa("2", "Instalar programas",
                "Escolha abaixo o que esta máquina precisa. Antes de instalar, o winget, a Loja e os " +
                "catálogos de pacotes são atualizados — instalador velho é a maior causa de falha.");
            cartaoApps.SemBorda = true;
            cartaoApps.Marcado = true;
            cartaoApps.SetBounds(1, 1, LARGURA - 2, 84);
            cartaoApps.AoMudar += delegate { AlternarLista(); };
            painelApps.Controls.Add(cartaoApps);

            int y = MontarAtalhos(painelApps, 90);
            inicioGrade = y + 6;
            y = MontarGrade(painelApps, inicioGrade);
            painelApps.SetBounds(0, 96, LARGURA, y + 14);
            rolagem.Controls.Add(painelApps);

            // ---- Etapa 3: atualizar o que já existe ----
            cartaoUpgrade = new CartaoEtapa("3", "Atualizar tudo o que já está instalado",
                "Programas de desktop pelo winget e apps da Microsoft Store pela mesma via do botão " +
                "\"Atualizar todos\" da Loja. Repete até não restar nenhuma atualização pendente.");
            cartaoUpgrade.Marcado = true;
            cartaoUpgrade.SetBounds(0, painelApps.Bottom + 12, LARGURA, 84);
            cartaoUpgrade.AoMudar += delegate { AtualizarResumo(); };
            rolagem.Controls.Add(cartaoUpgrade);

            // ---- Como a máquina fica no fim (não é uma etapa de trabalho,
            //      por isso sem número e fora da numeração acima) ----
            cartaoOusado = new CartaoEtapa("", "Sou ousado",
                "Ao terminar, deixa o plano de energia no desempenho máximo — nada desliga, suspende " +
                "ou hiberna — e o protetor de tela Faixas em " + ProtetorTela.MINUTOS +
                " min, que preserva o monitor. Desmarcado, aplica o plano Equilibrado (padrão).");
            cartaoOusado.Marcado = true;
            cartaoOusado.SetBounds(0, cartaoUpgrade.Bottom + 12, LARGURA, 90);
            cartaoOusado.AoMudar += delegate { AtualizarResumo(); };
            rolagem.Controls.Add(cartaoOusado);

            // ---- Barra de baixo: resumo e o botão que começa tudo ----
            lblResumo = new Label();
            lblResumo.Font = new Font("Segoe UI", 9.75f);
            lblResumo.ForeColor = Tema.TextoSuave;
            lblResumo.TextAlign = ContentAlignment.MiddleLeft;
            lblResumo.SetBounds(MARGEM, 540, 560, 52);
            Controls.Add(lblResumo);

            btnIniciar = new Button();
            btnIniciar.Text = "INICIAR";
            btnIniciar.Font = new Font("Segoe UI Semibold", 13f);
            btnIniciar.FlatStyle = FlatStyle.Flat;
            btnIniciar.FlatAppearance.BorderSize = 0;
            btnIniciar.BackColor = Tema.Laranja;
            btnIniciar.ForeColor = Color.FromArgb(24, 16, 6);
            btnIniciar.SetBounds(920 - MARGEM - 210, 544, 210, 46);
            btnIniciar.Cursor = Cursors.Hand;
            btnIniciar.Click += delegate
            {
                var h = AoIniciar;
                if (h != null) h();
            };
            Controls.Add(btnIniciar);

            buscaApps.TextChanged += delegate { FiltrarApps(); };
            filtroCategoria.SelectedIndexChanged += delegate { FiltrarApps(); };
            MarcarRecomendados();
            FiltrarApps();
        }

        // Linha de atalhos da lista de programas.
        int MontarAtalhos(Panel dono, int y)
        {
            var recomendados = Atalho("Recomendados", 24, y, 128);
            recomendados.Click += delegate { MarcarRecomendados(); };
            dono.Controls.Add(recomendados);

            var todos = Atalho("Todos visíveis", 160, y, 118);
            todos.Click += delegate { MarcarVisiveis(true); };
            dono.Controls.Add(todos);

            var limpar = Atalho("Limpar visíveis", 286, y, 118);
            limpar.Click += delegate { MarcarVisiveis(false); };
            dono.Controls.Add(limpar);

            resultadoBusca = new Label();
            resultadoBusca.Font = new Font("Segoe UI", 9f);
            resultadoBusca.ForeColor = Tema.TextoSuave;
            resultadoBusca.TextAlign = ContentAlignment.MiddleRight;
            resultadoBusca.SetBounds(416, y, 394, 28);
            dono.Controls.Add(resultadoBusca);

            var lblBusca = new Label();
            lblBusca.Text = "Buscar aplicativo ou finalidade";
            lblBusca.ForeColor = Tema.TextoSuave;
            lblBusca.SetBounds(24, y + 38, 390, 20);
            dono.Controls.Add(lblBusca);
            buscaApps = new TextBox();
            buscaApps.Font = new Font("Segoe UI", 10f);
            buscaApps.BackColor = Tema.FundoEscuro;
            buscaApps.ForeColor = Tema.Texto;
            buscaApps.SetBounds(24, y + 60, 390, 28);
            buscaApps.AccessibleName = "Buscar aplicativo ou finalidade";
            dono.Controls.Add(buscaApps);

            var lblCategoria = new Label();
            lblCategoria.Text = "Categoria";
            lblCategoria.ForeColor = Tema.TextoSuave;
            lblCategoria.SetBounds(432, y + 38, 378, 20);
            dono.Controls.Add(lblCategoria);
            filtroCategoria = new ComboBox();
            filtroCategoria.DropDownStyle = ComboBoxStyle.DropDownList;
            filtroCategoria.Font = new Font("Segoe UI", 10f);
            filtroCategoria.BackColor = Tema.FundoEscuro;
            filtroCategoria.ForeColor = Tema.Texto;
            filtroCategoria.DrawMode = DrawMode.OwnerDrawFixed;
            filtroCategoria.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                e.DrawBackground();
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, (string)filtroCategoria.Items[e.Index],
                        filtroCategoria.Font, e.Bounds, e.ForeColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            filtroCategoria.SetBounds(432, y + 60, 378, 28);
            filtroCategoria.AccessibleName = "Filtrar categoria de aplicativos";
            filtroCategoria.Items.Add("Todas as categorias");
            filtroCategoria.Items.AddRange(Catalogo.Categorias);
            filtroCategoria.SelectedIndex = 0;
            dono.Controls.Add(filtroCategoria);
            return y + 94;
        }

        static Button Atalho(string texto, int x, int y, int largura)
        {
            var b = new Button();
            b.Text = texto;
            b.Font = new Font("Segoe UI", 9f);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Tema.Borda;
            b.BackColor = Color.FromArgb(28, 31, 38);
            b.ForeColor = Tema.Texto;
            b.SetBounds(x, y, largura, 28);
            b.Cursor = Cursors.Hand;
            return b;
        }

        // Duas colunas equilibradas: cada categoria inteira vai para a coluna
        // mais curta no momento, para as duas terminarem na mesma altura sem
        // partir nenhuma categoria no meio.
        int MontarGrade(Panel dono, int inicio)
        {
            const int ESQ = 24;
            const int GAP = 20;
            int colunaL = (LARGURA - 2 - ESQ * 2 - GAP) / 2;
            int[] x = new int[] { ESQ, ESQ + colunaL + GAP };
            int[] y = new int[] { inicio, inicio };

            foreach (string categoria in Catalogo.Categorias)
            {
                List<AppCatalogo> apps = Catalogo.Da(categoria);
                if (apps.Count == 0) continue;
                int c = y[0] <= y[1] ? 0 : 1;

                var titulo = new Label();
                titulo.Text = categoria.ToUpperInvariant();
                titulo.Font = new Font("Segoe UI Semibold", 8.5f);
                titulo.ForeColor = Tema.Laranja;
                titulo.TextAlign = ContentAlignment.BottomLeft;
                titulo.SetBounds(x[c], y[c], colunaL, 26);
                dono.Controls.Add(titulo);
                titulos.Add(categoria, titulo);
                y[c] += 30;

                foreach (AppCatalogo app in apps)
                {
                    var item = new ItemPrograma(app);
                    item.SetBounds(x[c], y[c], colunaL, 46);
                    item.AoMudar += delegate { AtualizarResumo(); };
                    dono.Controls.Add(item);
                    dicas.SetToolTip(item, app.Nome + "\n" + app.Descricao + "\nInstalação: " + app.Vias);
                    itens.Add(item);
                    y[c] += 46;
                }
                y[c] += 12;
            }
            return Math.Max(y[0], y[1]);
        }

        void MarcarRecomendados()
        {
            Marcar(delegate(ItemPrograma i) { return i.App.Recomendado; });
        }

        bool Corresponde(ItemPrograma item)
        {
            string categoria = filtroCategoria.SelectedIndex > 0 ? (string)filtroCategoria.SelectedItem : null;
            return Catalogo.Corresponde(item.App, buscaApps.Text, categoria);
        }

        void MarcarVisiveis(bool marcado)
        {
            foreach (ItemPrograma item in itens)
                if (Corresponde(item)) item.DefinirSemAvisar(marcado);
            AtualizarResumo();
        }

        void FiltrarApps()
        {
            rolagem.SuspendLayout();
            painelApps.SuspendLayout();
            rolagem.AutoScrollPosition = Point.Empty;
            const int ESQ = 24, GAP = 20;
            int largura = (LARGURA - 2 - ESQ * 2 - GAP) / 2;
            int[] alturas = { inicioGrade, inicioGrade };
            foreach (string categoria in Catalogo.Categorias)
            {
                Label titulo = titulos[categoria];
                var visiveis = itens.FindAll(i => i.App.Categoria == categoria && Corresponde(i));
                titulo.Visible = visiveis.Count > 0;
                foreach (ItemPrograma item in itens)
                    if (item.App.Categoria == categoria) item.Visible = Corresponde(item);
                if (visiveis.Count == 0) continue;
                int coluna = alturas[0] <= alturas[1] ? 0 : 1;
                int x = ESQ + coluna * (largura + GAP);
                titulo.SetBounds(x, alturas[coluna], largura, 26);
                alturas[coluna] += 30;
                foreach (ItemPrograma item in visiveis)
                {
                    item.SetBounds(x, alturas[coluna], largura, 46);
                    alturas[coluna] += 46;
                }
                alturas[coluna] += 12;
            }
            painelApps.Height = Math.Max(alturas[0], alturas[1]) + 14;
            cartaoUpgrade.Top = painelApps.Bottom + 12;
            cartaoOusado.Top = cartaoUpgrade.Bottom + 12;
            painelApps.ResumeLayout();
            rolagem.ResumeLayout();
            AtualizarResumo();
        }

        void Marcar(Predicate<ItemPrograma> regra)
        {
            foreach (ItemPrograma i in itens) i.DefinirSemAvisar(regra(i));
            if (!cartaoApps.Marcado) cartaoApps.Marcado = true;
            AtualizarResumo();
        }

        // Desmarcar a etapa 2 apaga a lista inteira em vez de escondê-la: o
        // tecnico continua vendo o que deixou de fora.
        void AlternarLista()
        {
            foreach (Control c in painelApps.Controls)
            {
                if (c == cartaoApps) continue;
                c.Enabled = cartaoApps.Marcado;
            }
            painelApps.Invalidate();
            AtualizarResumo();
        }

        int Escolhidos
        {
            get
            {
                if (!cartaoApps.Marcado) return 0;
                int n = 0;
                foreach (ItemPrograma i in itens) if (i.Marcado) n++;
                return n;
            }
        }

        void AtualizarResumo()
        {
            if (resultadoBusca != null)
            {
                int visiveis = itens.FindAll(Corresponde).Count;
                resultadoBusca.Text = string.Format("{0} de {1} aplicativos · {2} selecionados",
                    visiveis, Catalogo.Apps.Length, Escolhidos);
                if (visiveis == 0) resultadoBusca.Text = "Nenhum resultado · " + Escolhidos + " selecionados";
            }
            var partes = new List<string>();
            if (cartaoUpdate.Marcado) partes.Add("Windows Update completo");
            int n = Escolhidos;
            if (n > 0) partes.Add(n == 1 ? "1 programa" : n + " programas");
            if (cartaoUpgrade.Marcado) partes.Add("atualização geral");

            string fim = cartaoOusado.Marcado
                ? "   Ao fim: desempenho máximo + protetor de tela."
                : "   Ao fim: plano Equilibrado (padrão).";

            if (partes.Count == 0)
            {
                lblResumo.Text = "Nada marcado — escolha pelo menos uma etapa acima.";
                lblResumo.ForeColor = Tema.LaranjaClaro;
                btnIniciar.Enabled = false;
                btnIniciar.BackColor = Color.FromArgb(64, 52, 38);
                btnIniciar.ForeColor = Tema.TextoSuave;
                return;
            }
            lblResumo.Text = "Vai executar: " + string.Join("  ·  ", partes.ToArray()) + "." +
                Environment.NewLine + fim;
            lblResumo.ForeColor = Tema.TextoSuave;
            btnIniciar.Enabled = true;
            btnIniciar.BackColor = Tema.Laranja;
            btnIniciar.ForeColor = Color.FromArgb(24, 16, 6);
        }

        // Grava as escolhas no estado — e delas que a retomada depois de cada
        // reinicializacao vai saber o que ainda falta fazer.
        public void Aplicar(Estado e)
        {
            e.FazerWindowsUpdate = cartaoUpdate.Marcado;
            e.FazerInstalacao = cartaoApps.Marcado;
            e.FazerAtualizacaoGeral = cartaoUpgrade.Marcado;
            e.ModoOusado = cartaoOusado.Marcado;
            e.Escolhidos = new List<string>();
            if (cartaoApps.Marcado)
                foreach (ItemPrograma i in itens)
                    if (i.Marcado) e.Escolhidos.Add(i.App.Chave);
            e.Configurado = true;
        }

        // Reabre a tela com o que ja tinha sido escolhido (o caso do "Refazer").
        public void Carregar(Estado e)
        {
            if (!e.Configurado) return;
            cartaoUpdate.Marcado = e.FazerWindowsUpdate;
            cartaoUpgrade.Marcado = e.FazerAtualizacaoGeral;
            cartaoOusado.Marcado = e.ModoOusado;
            foreach (ItemPrograma i in itens)
                i.DefinirSemAvisar(e.Escolhidos.Contains(i.App.Chave));
            cartaoApps.Marcado = e.FazerInstalacao;
            AlternarLista();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) dicas.Dispose();
            base.Dispose(disposing);
        }
    }
}
