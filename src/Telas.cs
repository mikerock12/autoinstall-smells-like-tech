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
        readonly Button btnPausar;
        readonly Button btnParar;
        readonly Label lblAviso;
        ControleExecucao controle;

        public TelaProgresso()
        {
            BackColor = Tema.Fundo;

            btnPausar = BotaoControle("Pausar", 596);
            btnPausar.Click += delegate { AlternarPausa(); };
            Controls.Add(btnPausar);

            btnParar = BotaoControle("Parar", 736);
            btnParar.Click += delegate { PedirParada(); };
            Controls.Add(btnParar);

            lblAviso = new Label();
            lblAviso.Font = new Font("Segoe UI", 8.5f);
            lblAviso.ForeColor = Tema.TextoSuave;
            lblAviso.TextAlign = ContentAlignment.MiddleLeft;
            lblAviso.SetBounds(60, 552, 520, 32);
            lblAviso.Text = "A pausa e a parada valem ao fim do item atual: uma instalação\n" +
                            "em andamento nunca é cortada no meio.";
            Controls.Add(lblAviso);

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
            caixaLog.SetBounds(60, 222, 800, 318);
            Controls.Add(caixaLog);
        }

        static Button BotaoControle(string texto, int x)
        {
            var b = new Button();
            b.Text = texto;
            b.Font = new Font("Segoe UI Semibold", 10f);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Tema.Borda;
            b.BackColor = Color.FromArgb(28, 31, 38);
            b.ForeColor = Tema.Texto;
            b.SetBounds(x, 552, 124, 32);
            b.Cursor = Cursors.Hand;
            return b;
        }

        public void Ligar(ControleExecucao c)
        {
            controle = c;
            controle.AoMudar += delegate { NoUi(AtualizarBotoes); };
        }

        void AlternarPausa()
        {
            if (controle == null) return;
            if (controle.Pausado) { controle.Continuar(); Log("Processo retomado."); }
            else { controle.Pausar(); Log("Pausa solicitada — vou parar ao fim do item atual."); }
            AtualizarBotoes();
        }

        void PedirParada()
        {
            if (controle == null || controle.Parando) return;
            var r = MessageBox.Show(
                "Parar o processo agora?\n\n" +
                "O item que estiver instalando será concluído com segurança e o " +
                "programa vai para a tela final, restaurando o plano de energia.\n\n" +
                "Você pode refazer tudo depois pelo botão da tela final.",
                "AutoInstall — parar", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            controle.Parar();
            Log("Parada solicitada — encerrando assim que o item atual terminar.");
            AtualizarBotoes();
        }

        void AtualizarBotoes()
        {
            if (controle == null) return;
            if (controle.Parando)
            {
                btnPausar.Enabled = false;
                btnParar.Enabled = false;
                btnParar.Text = "Parando...";
                lblAviso.ForeColor = Tema.LaranjaClaro;
                lblAviso.Text = "PARANDO — encerro assim que o item atual terminar\ncom segurança.";
                return;
            }
            btnPausar.Text = controle.Pausado ? "Continuar" : "Pausar";
            btnPausar.BackColor = controle.Pausado ? Tema.Laranja : Color.FromArgb(28, 31, 38);
            btnPausar.ForeColor = controle.Pausado ? Color.FromArgb(24, 16, 6) : Tema.Texto;
            lblAviso.ForeColor = controle.Pausado ? Tema.LaranjaClaro : Tema.TextoSuave;
            lblAviso.Text = controle.Pausado
                ? "PAUSADO no fim do item atual. Clique em Continuar para\nretomar de onde parou."
                : "A pausa e a parada valem ao fim do item atual: uma instalação\nem andamento nunca é cortada no meio.";
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

        public void Limpar()
        {
            NoUi(delegate
            {
                caixaLog.Clear();
                barra.Valor = 0;
                lblDetalhe.Text = "";
                lblContagens.Text = "";
                AtualizarBotoes();
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

    // Tela final: Guaxinim de novo, o agradecimento, o relatorio completo do
    // que foi feito e o convite para o site e o Instagram.
    public class TelaFinal : Panel
    {
        public event Action AoFechar;
        public event Action AoRefazer;

        readonly FadeImagem imagem;
        readonly TextBox caixaRelatorio;
        readonly Label lblTitulo;
        readonly Label lblSub;

        public TelaFinal(Image guaxinim)
        {
            BackColor = Tema.Fundo;

            imagem = new FadeImagem();
            imagem.Imagem = guaxinim;
            imagem.SetBounds(0, 6, 920, 166);
            Controls.Add(imagem);

            lblTitulo = new Label();
            lblTitulo.Text = "Obrigado por usar o AutoInstall Smells Like Tech!";
            lblTitulo.Font = new Font("Segoe UI Semibold", 16f);
            lblTitulo.ForeColor = Tema.Laranja;
            lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
            lblTitulo.SetBounds(0, 174, 920, 36);
            Controls.Add(lblTitulo);

            lblSub = new Label();
            lblSub.Font = new Font("Segoe UI", 10f);
            lblSub.ForeColor = Tema.TextoSuave;
            lblSub.TextAlign = ContentAlignment.MiddleCenter;
            lblSub.SetBounds(0, 210, 920, 22);
            Controls.Add(lblSub);

            caixaRelatorio = new TextBox();
            caixaRelatorio.Multiline = true;
            caixaRelatorio.ReadOnly = true;
            caixaRelatorio.ScrollBars = ScrollBars.Both;
            caixaRelatorio.WordWrap = false;
            caixaRelatorio.BorderStyle = BorderStyle.FixedSingle;
            caixaRelatorio.BackColor = Tema.FundoEscuro;
            caixaRelatorio.ForeColor = Color.FromArgb(200, 204, 210);
            caixaRelatorio.Font = new Font("Consolas", 9f);
            caixaRelatorio.SetBounds(60, 238, 800, 260);
            Controls.Add(caixaRelatorio);

            var lblConvite = new Label();
            lblConvite.Text = "Precisa de ajuda com o seu computador? Fale com a gente:";
            lblConvite.Font = new Font("Segoe UI", 10.5f);
            lblConvite.ForeColor = Tema.Texto;
            lblConvite.TextAlign = ContentAlignment.MiddleCenter;
            lblConvite.SetBounds(0, 504, 920, 22);
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
            btnFechar.SetBounds(380, 556, 160, 38);
            btnFechar.Cursor = Cursors.Hand;
            btnFechar.Click += delegate
            {
                var h = AoFechar;
                if (h != null) h();
            };
            Controls.Add(btnFechar);

            // Discreto de proposito: serve para o tecnico voltar a tela de
            // escolha e rodar outra combinacao na mesma maquina, sem competir
            // com o botao Fechar.
            var btnRefazer = new LinkLabel();
            btnRefazer.Text = "Escolher etapas e rodar de novo";
            btnRefazer.Font = new Font("Segoe UI", 8.75f);
            btnRefazer.LinkColor = Tema.TextoSuave;
            btnRefazer.ActiveLinkColor = Tema.LaranjaClaro;
            btnRefazer.VisitedLinkColor = Tema.TextoSuave;
            btnRefazer.TextAlign = ContentAlignment.MiddleLeft;
            btnRefazer.SetBounds(60, 566, 260, 20);
            btnRefazer.LinkClicked += delegate { ConfirmarRefazer(); };
            Controls.Add(btnRefazer);
        }

        void ConfirmarRefazer()
        {
            var r = MessageBox.Show(
                "Voltar para a tela de escolha?\n\n" +
                "O relatório atual é descartado e você escolhe de novo o que fazer " +
                "nesta máquina — útil para reconferir uma etapa ou instalar mais programas.",
                "AutoInstall — rodar de novo", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            var h = AoRefazer;
            if (h != null) h();
        }

        public void Preencher(Estado e)
        {
            if (e.Interrompido)
            {
                lblTitulo.Text = "Processo interrompido";
                lblSub.Text = "Abaixo está tudo o que deu tempo de concluir nesta máquina.";
            }
            else
            {
                lblTitulo.Text = "Obrigado por usar o AutoInstall Smells Like Tech!";
                lblSub.Text = "Tudo pronto. Abaixo, o relatório completo do que foi feito nesta máquina.";
            }
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

        // So entram no relatorio as etapas que foram escolhidas: um relatorio
        // com secoes vazias so faz o tecnico procurar erro onde nao houve.
        static string MontarRelatorio(Estado e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RELATÓRIO DO AUTOINSTALL — SMELLS LIKE TECH INFORMÁTICA");
            if (!string.IsNullOrEmpty(e.InicioEm))
                sb.AppendLine(string.Format("Início: {0}   ·   Reinicializações: {1}", e.InicioEm, e.Reinicios));
            if (e.Interrompido)
                sb.AppendLine("ATENÇÃO: o processo foi PARADO pelo técnico antes do fim — " +
                              "o que está abaixo é só o que deu tempo de concluir.");
            sb.AppendLine();

            if (e.FazerWindowsUpdate)
            {
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
            }

            if (e.Preparo.Count > 0)
            {
                sb.AppendLine("== PREPARO DOS INSTALADORES ==");
                foreach (string x in e.Preparo)
                    sb.AppendLine("  • " + x);
                sb.AppendLine();
            }

            if (e.FazerInstalacao && e.Escolhidos.Count > 0)
            {
                sb.AppendLine("== PROGRAMAS ==");
                if (e.Apps.Count == 0)
                    sb.AppendLine("  (nenhum programa registrado)");
                foreach (var app in e.Apps)
                {
                    string versao = string.IsNullOrEmpty(app.Versao) ? "" : " — versão " + app.Versao;
                    sb.AppendLine(string.Format("  • {0}{1} — {2}", app.Nome, versao, app.Status));
                }
                int faltando = e.Escolhidos.Count - e.Apps.Count;
                if (faltando > 0)
                    sb.AppendLine(string.Format(
                        "  ({0} programa(s) escolhido(s) não chegaram a ser processados)", faltando));
                sb.AppendLine();
            }

            if (e.FazerAtualizacaoGeral)
            {
                sb.AppendLine("== ATUALIZAÇÃO GERAL (winget + Microsoft Store) ==");
                if (e.Upgrades.Count == 0)
                    sb.AppendLine("  (nada registrado)");
                foreach (string u in e.Upgrades)
                    sb.AppendLine("  • " + u);
                sb.AppendLine();
            }

            sb.AppendLine("== ENERGIA ==");
            sb.AppendLine("  • Durante o processo: desempenho máximo, tela/discos sempre ligados,");
            sb.AppendLine("    sem suspender e sem hibernar.");
            sb.AppendLine("  • Ao final: plano Equilibrado (recomendado) restaurado e o plano");
            sb.AppendLine("    temporário removido.");
            sb.AppendLine();
            sb.AppendLine("Obrigado por usar o AutoInstall Smells Like Tech!");
            sb.AppendLine("www.smellsliketech.com.br  ·  instagram.com/smellsliketechinfo");
            return sb.ToString();
        }
    }
}
