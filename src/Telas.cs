using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AutoInstall
{
    // Tela de progresso: fase, contagens, etapa atual, barra com percentual
    // claro, detalhe do item atual e log corrido.
    public class TelaProgresso : Panel
    {
        readonly Label lblFase;
        readonly Label lblContagens;
        readonly Label lblEtapa;
        readonly Label lblDetalhe;
        readonly BarraProgresso barra;
        readonly TextBox caixaLog;

        public TelaProgresso()
        {
            BackColor = Tema.Fundo;

            lblFase = new Label();
            lblFase.Font = new Font("Segoe UI Semibold", 17f);
            lblFase.ForeColor = Tema.Laranja;
            lblFase.TextAlign = ContentAlignment.MiddleCenter;
            lblFase.SetBounds(0, 22, 920, 42);
            Controls.Add(lblFase);

            lblContagens = new Label();
            lblContagens.Font = new Font("Segoe UI", 11f);
            lblContagens.ForeColor = Tema.TextoSuave;
            lblContagens.TextAlign = ContentAlignment.MiddleCenter;
            lblContagens.SetBounds(0, 68, 920, 26);
            Controls.Add(lblContagens);

            lblEtapa = new Label();
            lblEtapa.Font = new Font("Segoe UI", 10.5f);
            lblEtapa.ForeColor = Tema.Texto;
            lblEtapa.SetBounds(60, 116, 800, 24);
            Controls.Add(lblEtapa);

            barra = new BarraProgresso();
            barra.SetBounds(60, 148, 800, 30);
            Controls.Add(barra);

            lblDetalhe = new Label();
            lblDetalhe.Font = new Font("Segoe UI", 9.5f);
            lblDetalhe.ForeColor = Tema.TextoSuave;
            lblDetalhe.SetBounds(60, 186, 800, 22);
            Controls.Add(lblDetalhe);

            caixaLog = new TextBox();
            caixaLog.Multiline = true;
            caixaLog.ReadOnly = true;
            caixaLog.ScrollBars = ScrollBars.Vertical;
            caixaLog.BorderStyle = BorderStyle.FixedSingle;
            caixaLog.BackColor = Tema.FundoEscuro;
            caixaLog.ForeColor = Color.FromArgb(185, 190, 196);
            caixaLog.Font = new Font("Consolas", 9f);
            caixaLog.SetBounds(60, 222, 800, 358);
            Controls.Add(caixaLog);
        }

        void NoUi(Action acao)
        {
            if (IsHandleCreated && InvokeRequired) BeginInvoke(acao);
            else acao();
        }

        public void Fase(string texto)
        {
            NoUi(delegate { lblFase.Text = texto; });
        }

        public void Contagens(string texto)
        {
            NoUi(delegate { lblContagens.Text = texto; });
        }

        public void Etapa(string texto)
        {
            Estado.LogArquivo("[etapa] " + texto);
            NoUi(delegate { lblEtapa.Text = texto; });
        }

        public void Progresso(int pct, string detalhe)
        {
            NoUi(delegate
            {
                barra.Valor = pct;
                if (detalhe != null) lblDetalhe.Text = detalhe;
            });
        }

        public void Log(string linha)
        {
            Estado.LogArquivo(linha);
            NoUi(delegate
            {
                if (caixaLog.TextLength > 400000) caixaLog.Clear();
                caixaLog.AppendText(linha + Environment.NewLine);
            });
        }
    }

    // Pede a reinicializacao (com contagem regressiva) e avisa que o programa
    // reabre sozinho e continua de onde parou.
    public class TelaReiniciar : Panel
    {
        public event Action AoReiniciar;

        readonly Label lblContagem;
        readonly Timer timer;
        int restantes = 60;
        bool disparado;

        public TelaReiniciar()
        {
            BackColor = Tema.Fundo;

            var lblTitulo = new Label();
            lblTitulo.Text = "Atualizações instaladas!";
            lblTitulo.Font = new Font("Segoe UI Semibold", 17f);
            lblTitulo.ForeColor = Tema.Laranja;
            lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
            lblTitulo.SetBounds(0, 130, 920, 42);
            Controls.Add(lblTitulo);

            var lblMsg = new Label();
            lblMsg.Text = "O computador precisa reiniciar para concluir esta rodada.\n" +
                          "Depois de reiniciar, o programa abre sozinho e procura mais atualizações,\n" +
                          "repetindo até o Windows ficar 100% em dia. Deixe o computador ligado.";
            lblMsg.Font = new Font("Segoe UI", 11f);
            lblMsg.ForeColor = Tema.Texto;
            lblMsg.TextAlign = ContentAlignment.MiddleCenter;
            lblMsg.SetBounds(60, 186, 800, 80);
            Controls.Add(lblMsg);

            var btn = new Button();
            btn.Text = "Reiniciar agora";
            btn.Font = new Font("Segoe UI Semibold", 13f);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = Tema.Laranja;
            btn.ForeColor = Color.FromArgb(24, 16, 6);
            btn.SetBounds(330, 296, 260, 52);
            btn.Cursor = Cursors.Hand;
            btn.Click += delegate { Disparar(); };
            Controls.Add(btn);

            lblContagem = new Label();
            lblContagem.Font = new Font("Segoe UI", 10f);
            lblContagem.ForeColor = Tema.TextoSuave;
            lblContagem.TextAlign = ContentAlignment.MiddleCenter;
            lblContagem.SetBounds(0, 368, 920, 26);
            Controls.Add(lblContagem);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                restantes--;
                if (restantes <= 0) { Disparar(); return; }
                AtualizarContagem();
            };
        }

        public void Iniciar()
        {
            restantes = 60;
            disparado = false;
            AtualizarContagem();
            timer.Start();
        }

        void AtualizarContagem()
        {
            lblContagem.Text = string.Format("Reiniciando automaticamente em {0} segundo{1}...",
                restantes, restantes == 1 ? "" : "s");
        }

        void Disparar()
        {
            if (disparado) return;
            disparado = true;
            timer.Stop();
            var h = AoReiniciar;
            if (h != null) h();
        }
    }

    // Tela final: Guaxinim de novo, relatorio completo do que foi instalado
    // e convite para o site e o Instagram.
    public class TelaFinal : Panel
    {
        public event Action AoFechar;

        readonly FadeImagem imagem;
        readonly TextBox caixaRelatorio;

        public TelaFinal(Image guaxinim)
        {
            BackColor = Tema.Fundo;

            imagem = new FadeImagem();
            imagem.Imagem = guaxinim;
            imagem.SetBounds(0, 8, 920, 200);
            Controls.Add(imagem);

            var lblTitulo = new Label();
            lblTitulo.Text = "Tudo pronto!";
            lblTitulo.Font = new Font("Segoe UI Semibold", 17f);
            lblTitulo.ForeColor = Tema.Laranja;
            lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
            lblTitulo.SetBounds(0, 212, 920, 38);
            Controls.Add(lblTitulo);

            caixaRelatorio = new TextBox();
            caixaRelatorio.Multiline = true;
            caixaRelatorio.ReadOnly = true;
            caixaRelatorio.ScrollBars = ScrollBars.Both;
            caixaRelatorio.WordWrap = false;
            caixaRelatorio.BorderStyle = BorderStyle.FixedSingle;
            caixaRelatorio.BackColor = Tema.FundoEscuro;
            caixaRelatorio.ForeColor = Color.FromArgb(200, 204, 210);
            caixaRelatorio.Font = new Font("Consolas", 9f);
            caixaRelatorio.SetBounds(60, 256, 800, 238);
            Controls.Add(caixaRelatorio);

            var lblConvite = new Label();
            lblConvite.Text = "Gostou do serviço? Acesse o site e siga a Smells Like Tech no Instagram:";
            lblConvite.Font = new Font("Segoe UI", 10.5f);
            lblConvite.ForeColor = Tema.Texto;
            lblConvite.TextAlign = ContentAlignment.MiddleCenter;
            lblConvite.SetBounds(0, 502, 920, 24);
            Controls.Add(lblConvite);

            var linkSite = new LinkLabel();
            linkSite.Text = Tema.SITE_TEXTO;
            linkSite.Font = new Font("Segoe UI Semibold", 10.5f);
            linkSite.LinkColor = Tema.Laranja;
            linkSite.ActiveLinkColor = Tema.LaranjaClaro;
            linkSite.VisitedLinkColor = Tema.Laranja;
            linkSite.TextAlign = ContentAlignment.MiddleCenter;
            linkSite.SetBounds(160, 528, 300, 24);
            linkSite.LinkClicked += delegate { Tema.AbrirSite(); };
            Controls.Add(linkSite);

            var linkInsta = new LinkLabel();
            linkInsta.Text = Tema.INSTA_TEXTO;
            linkInsta.Font = new Font("Segoe UI Semibold", 10.5f);
            linkInsta.LinkColor = Tema.Laranja;
            linkInsta.ActiveLinkColor = Tema.LaranjaClaro;
            linkInsta.VisitedLinkColor = Tema.Laranja;
            linkInsta.TextAlign = ContentAlignment.MiddleCenter;
            linkInsta.SetBounds(460, 528, 300, 24);
            linkInsta.LinkClicked += delegate { Tema.AbrirInstagram(); };
            Controls.Add(linkInsta);

            var btnFechar = new Button();
            btnFechar.Text = "Fechar";
            btnFechar.Font = new Font("Segoe UI Semibold", 11f);
            btnFechar.FlatStyle = FlatStyle.Flat;
            btnFechar.FlatAppearance.BorderSize = 0;
            btnFechar.BackColor = Tema.Laranja;
            btnFechar.ForeColor = Color.FromArgb(24, 16, 6);
            btnFechar.SetBounds(380, 558, 160, 38);
            btnFechar.Cursor = Cursors.Hand;
            btnFechar.Click += delegate
            {
                var h = AoFechar;
                if (h != null) h();
            };
            Controls.Add(btnFechar);
        }

        public void Preencher(Estado e)
        {
            caixaRelatorio.Text = MontarRelatorio(e);
            caixaRelatorio.Select(0, 0);

            // Fade in rapido do Guaxinim na tela final
            var cronometro = Stopwatch.StartNew();
            var fade = new Timer();
            fade.Interval = 30;
            fade.Tick += delegate
            {
                float a = cronometro.ElapsedMilliseconds / 1500f;
                if (a >= 1f) { a = 1f; fade.Stop(); }
                imagem.Alfa = a;
            };
            fade.Start();
        }

        static string MontarRelatorio(Estado e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RESUMO DO PÓS-FORMATAÇÃO — SMELLS LIKE TECH INFORMÁTICA");
            if (!string.IsNullOrEmpty(e.InicioEm))
                sb.AppendLine(string.Format("Início: {0}   ·   Reinicializações: {1}", e.InicioEm, e.Reinicios));
            sb.AppendLine();

            sb.AppendLine("== ATUALIZAÇÕES DO WINDOWS ==");
            int total = 0;
            if (e.Rodadas.Count == 0)
                sb.AppendLine("  (nenhuma atualização pendente foi encontrada)");
            foreach (var r in e.Rodadas)
            {
                sb.AppendLine(string.Format("  Verificação {0} — {1} atualização(ões):",
                    r.Numero, r.Atualizacoes.Count));
                foreach (string a in r.Atualizacoes)
                {
                    sb.AppendLine("    • " + a);
                    total++;
                }
            }
            sb.AppendLine(string.Format("  Total: {0} atualização(ões) em {1} verificação(ões).",
                total, e.Rodadas.Count));
            sb.AppendLine();

            sb.AppendLine("== PROGRAMAS ==");
            if (e.Apps.Count == 0)
                sb.AppendLine("  (nenhum programa registrado)");
            foreach (var app in e.Apps)
            {
                string versao = string.IsNullOrEmpty(app.Versao) ? "" : " — versão " + app.Versao;
                sb.AppendLine(string.Format("  • {0}{1} — {2}", app.Nome, versao, app.Status));
            }
            sb.AppendLine();

            sb.AppendLine("== ATUALIZAÇÃO GERAL DE APLICATIVOS (winget + Microsoft Store) ==");
            if (e.Upgrades.Count == 0)
                sb.AppendLine("  (nada registrado)");
            foreach (string u in e.Upgrades)
                sb.AppendLine("  • " + u);
            sb.AppendLine();

            sb.AppendLine("== ENERGIA ==");
            sb.AppendLine("  • Durante o processo: desempenho máximo, tela/discos sempre ligados,");
            sb.AppendLine("    sem suspender e sem hibernar.");
            sb.AppendLine("  • Ao final: plano Equilibrado (recomendado) restaurado e o plano");
            sb.AppendLine("    temporário removido.");
            return sb.ToString();
        }
    }
}
