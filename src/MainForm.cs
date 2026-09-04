using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoInstall
{
    // Janela do programa. O rodape (credito + site) fica fixo do inicio ao fim;
    // a area central troca entre escolha, progresso, reinicio e tela final. A
    // abertura com o Guaxinim recortado acontece antes, em SplashGuaxinim.
    public class MainForm : Form
    {
        // Ordem fixa das etapas. Quais delas entram na fila depende do que foi
        // marcado na primeira tela — ver Habilitada().
        static readonly string[] ORDEM = new string[]
            { "energia", "updates", "instaladores", "apps", "upgrade", "fim" };

        readonly bool retomada;
        readonly bool preview;

        Estado estado;
        ControleExecucao controle;
        // Uma instancia so para as tres fases que mexem com pacotes: ela
        // guarda onde o winget esta e o que ja foi preparado.
        InstaladorApps instalador;
        bool jaInterrompeu;
        Panel conteudo;
        TelaSelecao telaSelecao;
        TelaProgresso telaProg;
        TelaReiniciar telaReiniciar;
        TelaFinal telaFinal;

        public MainForm(bool retomada, bool preview)
        {
            this.retomada = retomada;
            this.preview = preview;

            Text = "AutoInstall · Smells Like Tech";
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

            telaSelecao = new TelaSelecao();
            telaSelecao.AoIniciar += delegate { Comecar(); };
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

        // Ponto de entrada: decide entre a tela de escolha, a continuacao de um
        // processo em andamento e o relatorio de um processo ja concluido.
        async void Fluxo()
        {
            estado = Estado.Carregar();

            if (estado.Fase == "concluido")
            {
                MostrarFinal();
                return;
            }

            // Ainda nao escolheu nada: a primeira tela e a de escolha, e nada
            // acontece no computador ate o botao INICIAR.
            if (!estado.Configurado)
            {
                telaSelecao.Carregar(estado);
                Mostrar(telaSelecao);
                return;
            }

            Mostrar(telaProg);
            await Rodar();
        }

        // Clique em INICIAR na tela de escolha.
        async void Comecar()
        {
            telaSelecao.Aplicar(estado);
            estado.InicioEm = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            estado.Fase = "energia";
            estado.Salvar();   // inofensivo em --preview (Estado.ModoPrevia)

            Estado.LogArquivo(string.Format(
                "Escolhas: Windows Update={0}, programas={1} ({2} marcados), atualização geral={3}",
                estado.FazerWindowsUpdate, estado.FazerInstalacao,
                estado.Escolhidos.Count, estado.FazerAtualizacaoGeral));

            Mostrar(telaProg);
            await Rodar();
        }

        async Task Rodar()
        {
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

        // Refazer tudo na mesma maquina: descarta o estado salvo e volta para a
        // tela de escolha, ja com as opcoes da rodada anterior marcadas.
        void Refazer()
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

            var anterior = estado;
            estado = Estado.Carregar();
            estado.FazerWindowsUpdate = anterior.FazerWindowsUpdate;
            estado.FazerInstalacao = anterior.FazerInstalacao;
            estado.FazerAtualizacaoGeral = anterior.FazerAtualizacaoGeral;
            estado.Escolhidos = new List<string>(anterior.Escolhidos);
            estado.Configurado = anterior.Configurado;

            telaProg.Limpar();
            telaSelecao.Carregar(estado);
            estado.Configurado = false;
            Mostrar(telaSelecao);
        }

        // ------------------------------------------------------------------
        // Sequencia de etapas
        // ------------------------------------------------------------------

        bool Habilitada(string fase)
        {
            if (fase == "updates") return estado.FazerWindowsUpdate;
            if (fase == "apps") return estado.FazerInstalacao && estado.Escolhidos.Count > 0;
            if (fase == "upgrade") return estado.FazerAtualizacaoGeral;
            // O preparo dos instaladores so faz sentido se alguma etapa depois
            // dele for usar winget, Loja ou script de fabricante.
            if (fase == "instaladores") return Habilitada("apps") || Habilitada("upgrade");
            return true;   // energia e fim sempre acontecem
        }

        InstaladorApps Instalador()
        {
            if (instalador == null) instalador = new InstaladorApps();
            return instalador;
        }

        // Proxima etapa marcada depois desta — as desmarcadas sao puladas.
        string Depois(string fase)
        {
            int i = Array.IndexOf(ORDEM, fase);
            for (int j = i + 1; j < ORDEM.Length; j++)
                if (Habilitada(ORDEM[j])) return ORDEM[j];
            return "fim";
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

            if (!await Ponto()) return;

            // 1) Energia no maximo (uma unica vez, valha o que valer o resto)
            if (estado.Fase == "energia")
            {
                if (!estado.EnergiaConfigurada)
                {
                    telaProg.Fase("Preparando o computador");
                    telaProg.Etapa("Ativando plano de energia de desempenho máximo (temporário)...");
                    telaProg.Progresso(15, "");
                    await Task.Run(delegate { Energia.AtivarMaximo(estado, log); });
                    estado.EnergiaConfigurada = true;
                    telaProg.Progresso(100, "");
                }
                estado.Fase = Depois("energia");
                estado.Salvar();
            }

            while (estado.Fase != "fim" && estado.Fase != "concluido")
            {
                if (!await Ponto()) return;

                if (estado.Fase == "updates")
                {
                    // Volta false quando o computador vai reiniciar: a sequencia
                    // continua sozinha depois do logon.
                    if (!await FaseUpdates(log)) return;
                }
                else if (estado.Fase == "instaladores")
                {
                    await FasePreparar(log);
                    if (controle.Parando) return;
                    estado.Fase = Depois("instaladores");
                    estado.Salvar();
                }
                else if (estado.Fase == "apps")
                {
                    await FaseApps(log);
                    if (controle.Parando) return;
                    estado.Fase = Depois("apps");
                    estado.Salvar();
                }
                else if (estado.Fase == "upgrade")
                {
                    await FaseUpgrade(log);
                    if (controle.Parando) return;
                    estado.Fase = Depois("upgrade");
                    estado.Salvar();
                }
                else
                {
                    // Fase desconhecida (estado de uma versao anterior): segue
                    // para o fim em vez de travar.
                    estado.Fase = "fim";
                    estado.Salvar();
                }
            }

            if (!await Ponto()) return;
            await Finalizar(log);
        }

        // Retorna true para seguir para a proxima etapa; false quando o
        // computador vai reiniciar ou o processo foi interrompido.
        async Task<bool> FaseUpdates(Action<string> log)
        {
            var atualizador = new AtualizadorWindows();
            int rodada = estado.Rodadas.Count + 1;
            telaProg.Fase(string.Format("Windows Update — verificação {0}", rodada));

            if (estado.Reinicios >= 8)
            {
                log("Limite de reinicializações atingido; seguindo para a próxima etapa.");
                estado.Fase = Depois("updates");
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
                log("Windows Update inacessível; seguindo para a próxima etapa.");
                estado.Fase = Depois("updates");
                estado.Salvar();
                return true;
            }

            telaProg.Contagens(string.Format(
                "Atualizações encontradas: {0}   ·   Opcionais/drivers: {1}",
                busca.Total, busca.Opcionais));
            if (busca.Interativas > 0)
                log(string.Format(
                    "{0} atualização(ões) declaram que podem pedir interação (normal em drivers) — " +
                    "todas entram na fila e instalam em modo silencioso forçado.", busca.Interativas));

            if (busca.Total == 0)
            {
                log("Nenhuma atualização pendente — Windows 100% atualizado!");
                estado.Fase = Depois("updates");
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

        // Etapa 0 das instalacoes: por o winget, o cliente da Loja e os
        // catalogos de manifestos em dia. Instalador velho e catalogo velho
        // sao a maior causa de falha na hora de instalar os programas.
        async Task FasePreparar(Action<string> log)
        {
            telaProg.Fase("Preparando os instaladores");
            telaProg.Contagens("winget · Microsoft Store · catálogos de pacotes");
            telaProg.Etapa("Deixando os próprios instaladores atualizados antes de instalar qualquer coisa...");
            telaProg.Progresso(0, "");

            InstaladorApps inst = Instalador();
            await Task.Run(delegate
            {
                inst.Preparar(estado, log, delegate(int pct, string detalhe)
                {
                    telaProg.Progresso(pct, detalhe);
                }, controle);
            });
            telaProg.Progresso(100, "Instaladores prontos.");
        }

        async Task FaseApps(Action<string> log)
        {
            List<AppCatalogo> lista = Catalogo.Resolver(estado.Escolhidos);
            telaProg.Fase("Instalação de programas");
            telaProg.Contagens(string.Format("{0} programa(s) escolhido(s) na primeira tela",
                lista.Count));
            telaProg.Progresso(0, "");

            InstaladorApps instalador = Instalador();
            if (!instalador.TemWinget)
            {
                telaProg.Etapa("Verificando o winget (Windows Package Manager)...");
                await Task.Run(delegate { return instalador.Garantir(log); });
            }

            // Um app ja registrado numa passagem anterior (antes de um reinicio,
            // por exemplo) nao e reinstalado.
            var feitos = new List<string>();
            foreach (AppInstalado a in estado.Apps) feitos.Add(a.Id);

            for (int i = 0; i < lista.Count; i++)
            {
                if (!await Ponto()) return;
                AppCatalogo alvo = lista[i];
                if (feitos.Contains(alvo.Chave)) continue;

                int indice = i;
                telaProg.Etapa(string.Format("Instalando {0} de {1}: {2}", i + 1, lista.Count, alvo.Nome));
                telaProg.Progresso((int)(i * 100.0 / lista.Count), alvo.Nome + " — " + alvo.Vias);

                AppInstalado app = await Task.Run(delegate
                {
                    return instalador.Instalar(alvo, log, delegate(int pctApp, string detalhe)
                    {
                        int geral = (int)((indice + pctApp / 100.0) * 100.0 / lista.Count);
                        telaProg.Progresso(geral, detalhe == null
                            ? string.Format("{0}: {1}%", alvo.Nome, pctApp) : detalhe);
                    });
                });
                estado.Apps.Add(app);
                estado.Salvar();
            }

            telaProg.Progresso(100, "Programas concluídos.");
        }

        async Task FaseUpgrade(Action<string> log)
        {
            telaProg.Fase("Atualizando tudo o que já está instalado");
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

            InstaladorApps instalador = Instalador();
            bool temWinget = instalador.TemWinget;
            if (!temWinget)
                temWinget = await Task.Run(delegate { return instalador.Garantir(log); });
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
            telaProg.Fase("Preparando os instaladores (PRÉVIA)");
            telaProg.Contagens("winget · Microsoft Store · catálogos de pacotes");
            telaProg.Etapa("Deixando os próprios instaladores atualizados... (simulação)");
            string[] passos = new string[]
            {
                "Procurando o winget nesta máquina...",
                "Atualizando o App Installer e a Microsoft Store...",
                "Reconferindo o winget...",
                "Limpando o cache de instaladores...",
                "Baixando os catálogos de pacotes mais recentes..."
            };
            for (int p = 0; p <= 100; p += 4)
            {
                telaProg.Progresso(p, passos[Math.Min(passos.Length - 1, p / 21)]);
                await Task.Delay(20);
            }
            telaProg.Log("Catálogos atualizados — os manifestos agora são os mais recentes. (simulação)");

            telaProg.Fase("Windows Update — verificação 1 (PRÉVIA)");
            telaProg.Contagens("Atualizações encontradas: 7   ·   Opcionais/drivers: 3");
            telaProg.Etapa("Baixando 7 atualização(ões)... (simulação — nada é executado)");
            for (int p = 0; p <= 100; p += 4)
            {
                telaProg.Progresso(p, string.Format("Baixando {0} de 7 ({1}% desta) — Atualização de exemplo",
                    1 + p * 6 / 100, p));
                await Task.Delay(20);
            }
            telaProg.Log("Download de todas as atualizações concluído. (simulação)");
            telaProg.Etapa("Instalando 7 atualização(ões)... (simulação)");
            for (int p = 0; p <= 100; p += 4)
            {
                telaProg.Progresso(p, string.Format("Instalando {0} de 7 ({1}% desta) — Atualização de exemplo",
                    1 + p * 6 / 100, p));
                await Task.Delay(20);
            }

            var fake = new Estado();
            fake.InicioEm = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            fake.Reinicios = 2;
            fake.FazerWindowsUpdate = estado.FazerWindowsUpdate;
            fake.FazerInstalacao = estado.FazerInstalacao;
            fake.FazerAtualizacaoGeral = estado.FazerAtualizacaoGeral;
            fake.Escolhidos = new List<string>(estado.Escolhidos);

            var rodada = new RodadaUpdates();
            rodada.Numero = 1;
            rodada.Atualizacoes.Add("Atualização Cumulativa para Windows 11 (KB5044033)");
            rodada.Atualizacoes.Add("Intel Corporation - Display - 31.0.101.4502");
            rodada.Atualizacoes.Add("Atualização de Definições do Microsoft Defender (KB2267602)");
            fake.Rodadas.Add(rodada);

            string[] versoes = new string[] { "1.4.2", "26.1.0", "7.12", "3.2.1", "18.9.5", "2026.3" };
            int n = 0;
            foreach (AppCatalogo a in Catalogo.Resolver(estado.Escolhidos))
            {
                var app = new AppInstalado();
                app.Nome = a.Nome;
                app.Id = a.Chave;
                app.Versao = versoes[n % versoes.Length];
                app.Status = n % 7 == 3 ? "já estava instalado" : "instalado";
                fake.Apps.Add(app);
                n++;
            }
            fake.Preparo.Add("winget encontrado na versão v1.29.290.");
            fake.Preparo.Add("1 instalador(es) atualizado(s) pela Loja.");
            fake.Preparo.Add("Cache de instaladores limpo (718 MB).");
            fake.Preparo.Add("Catálogos de pacotes atualizados.");
            fake.Upgrades.Add("Microsoft Store: 10 de 10 app(s) atualizado(s).");
            fake.Upgrades.Add("Passada 1 concluída (código 0).");
            fake.Upgrades.Add("Verificação final: nenhum aplicativo pendente.");

            estado = fake;
            MostrarFinal();
        }
    }
}
