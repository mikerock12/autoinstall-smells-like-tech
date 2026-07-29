using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoInstall
{
    // Janela do programa. O rodape (credito + site) fica fixo do inicio ao fim;
    // a area central troca entre progresso, reinicio e tela final. A abertura
    // com o Guaxinim recortado acontece antes, em SplashGuaxinim.
    public class MainForm : Form
    {
        readonly bool retomada;
        readonly bool preview;

        Estado estado;
        ControleExecucao controle;
        bool jaInterrompeu;
        Panel conteudo;
        TelaProgresso telaProg;
        TelaReiniciar telaReiniciar;
        TelaFinal telaFinal;

        public MainForm(bool retomada, bool preview)
        {
            this.retomada = retomada;
            this.preview = preview;

            Text = "AutoInstall Pós-Formatação · Smells Like Tech";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(920, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Tema.Fundo;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            // Rodape permanente: credito + site clicavel
            var rodape = new Panel();
            rodape.Dock = DockStyle.Bottom;
            rodape.Height = 58;
            rodape.BackColor = Tema.FundoEscuro;
            rodape.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(70, 52, 26)))
                    e.Graphics.DrawLine(pen, 0, 0, rodape.Width, 0);
            };

            var lblCredito = new Label();
            lblCredito.Text = "Criado por Maicon Nunes, da Smells Like Tech Informática";
            lblCredito.Font = new Font("Segoe UI", 9.75f);
            lblCredito.ForeColor = Tema.Texto;
            lblCredito.TextAlign = ContentAlignment.MiddleCenter;
            lblCredito.SetBounds(0, 7, 920, 22);
            rodape.Controls.Add(lblCredito);

            var linkSite = new LinkLabel();
            linkSite.Text = Tema.SITE_TEXTO;
            linkSite.Font = new Font("Segoe UI", 9.75f);
            linkSite.LinkColor = Tema.Laranja;
            linkSite.ActiveLinkColor = Tema.LaranjaClaro;
            linkSite.VisitedLinkColor = Tema.Laranja;
            linkSite.TextAlign = ContentAlignment.MiddleCenter;
            linkSite.SetBounds(0, 29, 920, 22);
            linkSite.LinkClicked += delegate { Tema.AbrirSite(); };
            rodape.Controls.Add(linkSite);

            conteudo = new Panel();
            conteudo.Dock = DockStyle.Fill;
            conteudo.BackColor = Tema.Fundo;

            Controls.Add(rodape);
            Controls.Add(conteudo);
            conteudo.BringToFront();

            controle = new ControleExecucao();

            telaProg = new TelaProgresso();
            telaProg.Ligar(controle);
            telaReiniciar = new TelaReiniciar();
            telaReiniciar.AoReiniciar += delegate { ReiniciarAgora(); };
            telaFinal = new TelaFinal(Recursos.CarregarGuaxinim());
            telaFinal.AoFechar += delegate { Close(); };
            telaFinal.AoRefazer += delegate { Refazer(); };

            Shown += delegate { Fluxo(); };
        }

        void Mostrar(Control tela)
        {
            conteudo.Controls.Clear();
            tela.Dock = DockStyle.Fill;
            conteudo.Controls.Add(tela);
        }

        void MostrarFinal()
        {
            Mostrar(telaFinal);
            telaFinal.Preencher(estado);
        }

        async void Fluxo()
        {
            estado = Estado.Carregar();
            Mostrar(telaProg);

            if (preview)
            {
                await FluxoPreview();
                return;
            }

            try
            {
                await FluxoReal();
            }
            catch (Exception ex)
            {
                telaProg.Log("ERRO INESPERADO: " + ex.Message);
                telaProg.Log("Feche e abra o programa novamente para retomar do ponto salvo.");
            }
        }

        // Refazer tudo na mesma maquina: descarta o estado salvo e recomeca.
        async void Refazer()
        {
            if (preview)
            {
                MessageBox.Show(this,
                    "Modo prévia: nada é executado de verdade. Abra o programa sem --preview " +
                    "para refazer o processo nesta máquina.",
                    "AutoInstall — prévia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            jaInterrompeu = false;
            Estado.Apagar();
            TarefaInicio.Remover();
            controle.Reiniciar();
            estado = Estado.Carregar();
            telaProg.Limpar();
            Mostrar(telaProg);
            telaProg.Log("Refazendo todas as etapas do zero.");
            try
            {
                await FluxoReal();
            }
            catch (Exception ex)
            {
                telaProg.Log("ERRO INESPERADO: " + ex.Message);
            }
        }

        // Ponto de checagem: segura enquanto pausado e encerra se o tecnico
        // mandou parar. Roda fora da thread da interface (Prosseguir bloqueia).
        async Task<bool> Ponto()
        {
            bool seguir = await Task.Run(delegate { return controle.Prosseguir(); });
            if (seguir) return true;
            await Interromper();
            return false;
        }

        async Task FluxoReal()
        {
            Action<string> log = telaProg.Log;

            // Ja terminou tudo em execucao anterior? So mostra o relatorio -
            // e de la que se refaz o processo, pelo botao da tela final.
            if (estado.Fase == "concluido")
            {
                MostrarFinal();
                return;
            }

            // Escolha do Office: uma unica vez, antes do processo longo.
            if (string.IsNullOrEmpty(estado.EdicaoOffice))
            {
                using (var dlg = new EscolhaOffice())
                {
                    dlg.ShowDialog(this);
                    estado.EdicaoOffice = dlg.Edicao;
                }
                estado.Salvar();
                log("Office escolhido: " + InstaladorOffice.NomeEdicao(estado.EdicaoOffice) + ".");
            }

            if (!await Ponto()) return;

            // 1) Energia no maximo (uma unica vez)
            if (!estado.EnergiaConfigurada)
            {
                telaProg.Fase("Preparando o computador");
                telaProg.Etapa("Ativando plano de energia de desempenho máximo (temporário)...");
                telaProg.Progresso(15, "");
                await Task.Run(delegate { Energia.AtivarMaximo(estado, log); });
                estado.EnergiaConfigurada = true;
                if (estado.Fase == "energia") estado.Fase = "updates";
                estado.Salvar();
                telaProg.Progresso(100, "");
            }

            // 2) Windows Update em rodadas (reinicia entre elas)
            if (estado.Fase == "updates")
            {
                bool continuar = await FaseUpdates(log);
                if (!continuar) return;   // vai reiniciar, ou foi interrompido
            }

            // 3) Programas essenciais
            if (estado.Fase == "apps")
            {
                if (!await Ponto()) return;
                await FaseApps(log);
                if (controle.Parando) return;
            }

            // 4) Office
            if (estado.Fase == "office")
            {
                if (!await Ponto()) return;
                await FaseOffice(log);
                if (controle.Parando) return;
            }

            // 5) Loja + atualizacao geral de aplicativos
            if (estado.Fase == "upgrade")
            {
                if (!await Ponto()) return;
                await FaseUpgrade(log);
                if (controle.Parando) return;
            }

            // 6) Energia recomendada + relatorio final
            if (!await Ponto()) return;
            await Finalizar(log);
        }

        // Retorna true para seguir para a proxima fase; false quando o
        // computador vai reiniciar ou o processo foi interrompido.
        async Task<bool> FaseUpdates(Action<string> log)
        {
            if (!await Ponto()) return false;

            var atualizador = new AtualizadorWindows();
            int rodada = estado.Rodadas.Count + 1;
            telaProg.Fase(string.Format("Windows Update — verificação {0}", rodada));

            if (estado.Reinicios >= 8)
            {
                log("Limite de reinicializações atingido; seguindo para a instalação de programas.");
                estado.Fase = "apps";
                estado.Salvar();
                return true;
            }

            telaProg.Contagens("Consultando o Windows Update, isso pode levar alguns minutos...");
            telaProg.Etapa("Procurando atualizações (incluindo opcionais e drivers)...");
            telaProg.Progresso(0, "");

            ResultadoBusca busca = null;
            for (int tentativa = 1; tentativa <= 3; tentativa++)
            {
                Exception falha = null;
                try { busca = await Task.Run(delegate { return atualizador.Buscar(); }); }
                catch (Exception ex) { falha = ex; }
                if (busca != null) break;
                log(string.Format("Falha ao consultar o Windows Update (tentativa {0}/3): {1}",
                    tentativa, falha.Message));
                if (tentativa < 3) await Task.Delay(20000);
            }
            if (busca == null)
            {
                log("Windows Update inacessível; seguindo para a instalação de programas.");
                estado.Fase = "apps";
                estado.Salvar();
                return true;
            }

            telaProg.Contagens(string.Format(
                "Atualizações encontradas: {0}   ·   Opcionais/drivers: {1}",
                busca.Total, busca.Opcionais));
            if (busca.Ignorados > 0)
                log(string.Format("{0} atualização(ões) que exigem interação foram puladas.", busca.Ignorados));

            if (busca.Total == 0)
            {
                log("Nenhuma atualização pendente — Windows 100% atualizado!");
                estado.Fase = "apps";
                estado.Salvar();
                return true;
            }

            foreach (var item in busca.Itens)
                log((item.Driver ? "[driver] " : item.Opcional ? "[opcional] " : "") + item.Titulo);

            if (!await Ponto()) return false;

            // Download com percentual real
            telaProg.Etapa(string.Format("Baixando {0} atualização(ões)...", busca.Total));
            atualizador.AoProgredirDownload = delegate(int geral, int idx, int pctItem, string titulo)
            {
                telaProg.Progresso(geral, string.Format("Baixando {0} de {1} ({2}% desta) — {3}",
                    idx, busca.Total, pctItem, Curto(titulo)));
            };
            await Task.Run(delegate { atualizador.Baixar(busca, controle); });
            if (controle.Parando) { await Interromper(); return false; }
            telaProg.Progresso(100, "Download concluído.");
            log("Download de todas as atualizações concluído.");

            if (!await Ponto()) return false;

            // Instalacao com percentual real (nunca abortada no meio)
            telaProg.Etapa(string.Format("Instalando {0} atualização(ões)...", busca.Total));
            telaProg.Progresso(0, "");
            atualizador.AoProgredirInstalacao = delegate(int geral, int idx, int pctItem, string titulo)
            {
                telaProg.Progresso(geral, string.Format("Instalando {0} de {1} ({2}% desta) — {3}",
                    idx, busca.Total, pctItem, Curto(titulo)));
            };
            await Task.Run(delegate { atualizador.Instalar(busca); });
            telaProg.Progresso(100, "Instalação concluída.");
            log("Instalação desta rodada concluída.");

            var registro = new RodadaUpdates();
            registro.Numero = rodada;
            foreach (var item in busca.Itens)
                registro.Atualizacoes.Add(item.Titulo +
                    (item.Resultado == "ok" ? "" : " [" + item.Resultado + "]"));
            estado.Rodadas.Add(registro);
            estado.Reinicios++;
            estado.Salvar();

            if (controle.Parando) { await Interromper(); return false; }

            TarefaInicio.Criar(log);
            Mostrar(telaReiniciar);
            telaReiniciar.Iniciar();
            return false;
        }

        async Task FaseApps(Action<string> log)
        {
            var lista = InstaladorApps.Programas;
            telaProg.Fase("Instalação de programas");
            telaProg.Contagens(string.Format(
                "{0} programas: Chrome · Acrobat Reader · WinRAR · K-Lite (codecs + player)",
                lista.Length));
            telaProg.Progresso(0, "");

            var instalador = new InstaladorApps();
            telaProg.Etapa("Verificando o winget (Windows Package Manager)...");
            bool temWinget = await Task.Run(delegate { return instalador.Garantir(log); });

            for (int i = 0; i < lista.Length; i++)
            {
                if (!await Ponto()) return;
                var alvo = lista[i];
                int indice = i;
                telaProg.Etapa(string.Format("Instalando {0} de {1}: {2}", i + 1, lista.Length, alvo.Nome));
                telaProg.Progresso((int)(i * 100.0 / lista.Length), alvo.Nome);

                AppInstalado app;
                if (!temWinget)
                {
                    app = new AppInstalado();
                    app.Nome = alvo.Nome;
                    app.Id = alvo.Id;
                    app.Status = "não instalado (winget indisponível)";
                }
                else
                {
                    app = await Task.Run(delegate
                    {
                        return instalador.Instalar(alvo, log, delegate(int pctApp)
                        {
                            int geral = (int)((indice + pctApp / 100.0) * 100.0 / lista.Length);
                            telaProg.Progresso(geral, string.Format("{0}: {1}%", alvo.Nome, pctApp));
                        });
                    });
                }
                estado.Apps.Add(app);
                estado.Salvar();
                log(string.Format("{0} — {1}{2}", app.Nome, app.Status,
                    string.IsNullOrEmpty(app.Versao) ? "" : " (versão " + app.Versao + ")"));
            }

            telaProg.Progresso(100, "Programas concluídos.");
            estado.Fase = "office";
            estado.Salvar();
        }

        async Task FaseOffice(Action<string> log)
        {
            string edicao = string.IsNullOrEmpty(estado.EdicaoOffice)
                ? InstaladorOffice.EDICAO_CONSUMIDOR : estado.EdicaoOffice;

            telaProg.Fase("Instalação do Office");
            telaProg.Contagens(InstaladorOffice.NomeEdicao(edicao) + "   ·   português (pt-BR)");
            telaProg.Etapa("Instalando o Office direto da Microsoft, sem interação...");
            telaProg.Progresso(0, "");

            var instalador = new InstaladorOffice();
            AppInstalado app = await Task.Run(delegate
            {
                return instalador.Instalar(edicao, log, delegate(int pct, string detalhe)
                {
                    telaProg.Progresso(pct, detalhe);
                });
            });
            estado.Apps.Add(app);
            estado.Fase = "upgrade";
            estado.Salvar();
            telaProg.Progresso(100, app.Status);
        }

        async Task FaseUpgrade(Action<string> log)
        {
            telaProg.Fase("Atualizando todos os aplicativos");
            telaProg.Contagens("Apps da Microsoft Store + programas comuns (winget)");

            // 1) Apps da Microsoft Store — mesma ação do botão "Atualizar todos"
            //    da Loja (o winget não cobre bem os apps UWP).
            telaProg.Etapa("Atualizando os apps da Microsoft Store...");
            telaProg.Progresso(0, "");
            var loja = new LojaMicrosoft();
            loja.AoLogar = log;
            loja.AoProgredir = delegate(int pct, string detalhe)
            {
                telaProg.Progresso(pct, detalhe);
            };
            bool lojaOk = await Task.Run(delegate { return loja.Atualizar(estado, controle); });
            if (!lojaOk)
            {
                log("Não consegui acionar a atualização automática da Loja; abrindo a página " +
                    "de atualizações da Microsoft Store para acompanhar manualmente.");
                try { Process.Start("ms-windows-store://downloadsandupdates"); }
                catch { }
            }

            if (!await Ponto()) return;

            // 2) Programas comuns via winget, em passadas até zerar
            telaProg.Etapa("Rodando a atualização geral do winget (repete até não sobrar nada)...");
            telaProg.Progresso(0, "");

            var instalador = new InstaladorApps();
            bool temWinget = await Task.Run(delegate { return instalador.Garantir(log); });
            if (temWinget)
            {
                await Task.Run(delegate
                {
                    instalador.AtualizarTudo(estado, log, delegate(int pct)
                    {
                        telaProg.Progresso(pct, null);
                    }, controle);
                });
            }
            else
            {
                log("winget indisponível — atualização geral de aplicativos pulada.");
            }

            telaProg.Progresso(100, "");
            estado.Fase = "fim";
            estado.Salvar();
        }

        async Task Finalizar(Action<string> log)
        {
            telaProg.Fase("Finalizando");
            telaProg.Etapa("Restaurando o plano de energia recomendado...");
            await Task.Run(delegate { Energia.Restaurar(estado, log); });
            TarefaInicio.Remover();
            estado.Fase = "concluido";
            estado.Salvar();
            MostrarFinal();
        }

        // Parada pedida pelo tecnico: devolve a maquina a um estado saudavel
        // (energia recomendada, sem tarefa de retomada) e mostra o que deu
        // tempo de fazer.
        async Task Interromper()
        {
            if (jaInterrompeu) return;
            jaInterrompeu = true;
            telaProg.Fase("Interrompendo");
            telaProg.Etapa("Restaurando o plano de energia e encerrando com segurança...");
            estado.Interrompido = true;
            estado.Salvar();
            await Task.Run(delegate { Energia.Restaurar(estado, telaProg.Log); });
            TarefaInicio.Remover();
            estado.Fase = "concluido";
            estado.Salvar();
            MostrarFinal();
        }

        void ReiniciarAgora()
        {
            Estado.LogArquivo("Reiniciando o computador...");
            Executor.Rodar("shutdown.exe", "/r /t 3");
            Application.Exit();
        }

        static string Curto(string titulo)
        {
            if (string.IsNullOrEmpty(titulo)) return "";
            if (titulo.Length <= 58) return titulo;
            return titulo.Substring(0, 57) + "…";
        }

        // Modo --preview: so demonstra as telas com dados ficticios.
        async Task FluxoPreview()
        {
            telaProg.Fase("Windows Update — verificação 1 (PRÉVIA)");
            telaProg.Contagens("Atualizações encontradas: 7   ·   Opcionais/drivers: 3");
            telaProg.Etapa("Baixando 7 atualização(ões)... (simulação — nada é executado)");
            for (int p = 0; p <= 100; p += 2)
            {
                telaProg.Progresso(p, string.Format("Baixando {0} de 7 ({1}% desta) — Atualização de exemplo",
                    1 + p * 6 / 100, p));
                await Task.Delay(35);
            }
            telaProg.Log("Download de todas as atualizações concluído. (simulação)");
            telaProg.Etapa("Instalando 7 atualização(ões)... (simulação)");
            for (int p = 0; p <= 100; p += 2)
            {
                telaProg.Progresso(p, string.Format("Instalando {0} de 7 ({1}% desta) — Atualização de exemplo",
                    1 + p * 6 / 100, p));
                await Task.Delay(35);
            }

            var fake = new Estado();
            fake.InicioEm = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            fake.Reinicios = 2;
            var rodada = new RodadaUpdates();
            rodada.Numero = 1;
            rodada.Atualizacoes.Add("Atualização Cumulativa para Windows 11 (KB5044033)");
            rodada.Atualizacoes.Add("Intel Corporation - Display - 31.0.101.4502");
            rodada.Atualizacoes.Add("Atualização de Definições do Microsoft Defender (KB2267602)");
            fake.Rodadas.Add(rodada);
            fake.Apps.Add(AppFake("Google Chrome", "Google.Chrome", "138.0.7204.97"));
            fake.Apps.Add(AppFake("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", "25.001.20521"));
            fake.Apps.Add(AppFake("WinRAR", "RARLab.WinRAR", "7.12"));
            fake.Apps.Add(AppFake("K-Lite Codec Pack (codecs + player MPC-HC)", "CodecGuide.K-LiteCodecPack.Standard", "18.9.5"));
            fake.Apps.Add(AppFake("Microsoft Office 365 — Microsoft 365 (Personal/Família)", "O365HomePremRetail", "16.0.18827.20202"));
            fake.Upgrades.Add("Microsoft Store: 10 de 10 app(s) atualizado(s).");
            fake.Upgrades.Add("Passada 1 concluída (código 0).");
            fake.Upgrades.Add("Verificação final: nenhum aplicativo pendente.");

            estado = fake;
            MostrarFinal();
        }

        static AppInstalado AppFake(string nome, string id, string versao)
        {
            var a = new AppInstalado();
            a.Nome = nome;
            a.Id = id;
            a.Versao = versao;
            a.Status = "instalado";
            return a;
        }
    }
}
