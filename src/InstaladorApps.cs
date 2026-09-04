using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AutoInstall
{
    // Instalacao dos programas escolhidos na primeira tela e atualizacao geral
    // dos que ja estao na maquina.
    //
    // Cada app do catalogo traz uma ou mais vias de instalacao em ordem de
    // preferencia (winget, Microsoft Store, script PowerShell, instalador
    // oficial do fabricante). Elas sao tentadas uma a uma ate a maquina provar
    // que o programa esta la — quem decide se instalou e o sistema, nunca o
    // codigo de saida do instalador.
    public class InstaladorApps
    {
        const string ARGS_COMUNS =
            " --accept-package-agreements --accept-source-agreements --disable-interactivity";

        // 0x8A15002B: "nenhuma atualizacao aplicavel" - ja esta na ultima versao.
        const int COD_SEM_ATUALIZACAO = unchecked((int)0x8A15002B);
        // 0x8A150011: o hash do instalador nao confere com o do manifesto.
        const int COD_HASH = unchecked((int)0x8A150011);

        string winget;
        bool hashLiberado;

        // ------------------------------------------------------------------
        // winget: localizar e, se preciso, instalar (comum logo apos formatar)
        // ------------------------------------------------------------------

        string Localizar()
        {
            try
            {
                string alias = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(alias)) return alias;
            }
            catch { }
            var r = Executor.Rodar("where.exe", "winget.exe");
            if (r.Ok)
            {
                foreach (string l in r.Saida.Replace("\r", "\n").Split('\n'))
                {
                    string cand = l.Trim();
                    if (cand.Length > 0 && File.Exists(cand)) return cand;
                }
            }
            return null;
        }

        public bool TemWinget { get { return winget != null; } }

        public bool Garantir(Action<string> log)
        {
            winget = Localizar();
            if (winget != null)
            {
                var t = Executor.Rodar(winget, "--version");
                if (t.Ok) { log("winget disponível (" + t.Saida.Trim() + ")."); return true; }
            }

            log("winget não encontrado; instalando o App Installer da Microsoft...");
            try { BootstrapWinget(log); }
            catch (Exception ex) { log("Falha ao baixar/instalar o winget: " + ex.Message); }

            winget = Localizar();
            if (winget != null && Executor.Rodar(winget, "--version").Ok)
            {
                log("winget instalado com sucesso.");
                return true;
            }
            winget = null;
            log("AVISO: winget indisponível — só as vias alternativas de cada programa serão usadas.");
            return false;
        }

        void BootstrapWinget(Action<string> log)
        {
            Tls12();
            string tmp = Path.Combine(Path.GetTempPath(), "slt-winget");
            Directory.CreateDirectory(tmp);

            string[][] arquivos = new string[][]
            {
                new string[] { "https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx", "vclibs.appx" },
                new string[] { "https://github.com/microsoft/microsoft-ui-xaml/releases/download/v2.8.6/Microsoft.UI.Xaml.2.8.x64.appx", "uixaml.appx" },
                new string[] { "https://aka.ms/getwinget", "appinstaller.msixbundle" }
            };
            using (var wc = new WebClient())
            {
                foreach (string[] a in arquivos)
                {
                    log("Baixando " + a[1] + "...");
                    wc.DownloadFile(a[0], Path.Combine(tmp, a[1]));
                }
            }

            log("Registrando os pacotes do App Installer...");
            RodarPowerShell(
                "Add-AppxPackage -Path '" + Path.Combine(tmp, "vclibs.appx") + "' -ErrorAction SilentlyContinue\r\n" +
                "Add-AppxPackage -Path '" + Path.Combine(tmp, "uixaml.appx") + "' -ErrorAction SilentlyContinue\r\n" +
                "Add-AppxPackage -Path '" + Path.Combine(tmp, "appinstaller.msixbundle") + "'\r\n",
                null);
        }

        static void Tls12()
        {
            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
        }

        // ------------------------------------------------------------------
        // Etapa 0: por os PROPRIOS instaladores em dia
        // ------------------------------------------------------------------

        // Instalador desatualizado e a maior causa de falha na instalacao dos
        // programas. Sao tres coisas velhas diferentes, e cada uma quebra de um
        // jeito:
        //
        //   1. O winget em si. Versao antiga nao entende manifestos novos e
        //      falha em pacotes que funcionam em qualquer maquina em dia.
        //   2. O cliente da Loja. Desatualizado, derruba a fonte msstore
        //      inteira - e com ela todo app que so existe como pacote da Loja.
        //   3. O CATALOGO local de manifestos. Este e o mais traicoeiro: o
        //      winget guarda um indice em cache e, quando o fabricante
        //      republica o instalador, o hash do manifesto em cache fica
        //      velho e a instalacao morre em 0x8A150011. O app ja sabia se
        //      recuperar disso repetindo com --ignore-security-hash; agora
        //      ataca a causa, baixando o indice novo antes de comecar.
        //
        // Roda antes da instalacao dos programas E antes da atualizacao geral,
        // uma unica vez por execucao.
        public void Preparar(Estado estado, Action<string> log, Action<int, string> progresso,
            ControleExecucao controle)
        {
            Prog(progresso, 5, "Procurando o winget nesta máquina...");
            Garantir(log);
            string antes = VersaoWinget();
            if (antes != null) estado.Preparo.Add("winget encontrado na versão " + antes + ".");

            // 1) winget e cliente da Loja, pela API da Loja - ela nao depende
            //    de nenhum dos dois estar funcionando.
            Prog(progresso, 12, "Atualizando o App Installer e a Microsoft Store...");
            var loja = new LojaAppInstaller();
            loja.AoLogar = log;
            loja.AoProgredir = delegate(int pct, string detalhe)
            {
                Prog(progresso, 12 + pct * 33 / 100, detalhe);
            };
            bool lojaOk = loja.Atualizar(controle);
            if (lojaOk)
                estado.Preparo.Add(loja.Atualizados == 0
                    ? "App Installer e Microsoft Store já estavam em dia."
                    : string.Format("{0} instalador(es) atualizado(s) pela Loja{1}.",
                        loja.Atualizados, loja.Erros > 0 ? ", " + loja.Erros + " com erro" : ""));
            else
                estado.Preparo.Add("Não consegui atualizar os instaladores pela Loja.");

            if (controle != null && !controle.Prosseguir()) return;

            // 2) Segunda via para o proprio winget, caso a Loja nao tenha dado
            //    conta. 0x8A15002B aqui e boa noticia: ja esta na ultima.
            if (winget != null)
            {
                Prog(progresso, 48, "Conferindo a versão do winget...");
                var r = Executor.Rodar(winget,
                    "upgrade --id Microsoft.AppInstaller -e --silent" + ARGS_COMUNS,
                    Encoding.UTF8, null);
                if (r.Codigo == 0) log("winget: pacote do App Installer atualizado.");
            }

            // 3) O PATH do processo e de quando o programa abriu. Um App
            //    Installer recem-instalado publica o winget.exe num caminho
            //    novo, que so aparece relendo o ambiente do registro.
            Prog(progresso, 58, "Recarregando o ambiente...");
            RecarregarAmbiente(log);
            winget = Localizar();
            string depois = VersaoWinget();
            if (depois != null && antes != null && depois != antes)
            {
                log(string.Format("winget atualizado: {0} → {1}.", antes, depois));
                estado.Preparo.Add(string.Format("winget atualizado: {0} → {1}.", antes, depois));
            }
            else if (depois != null && antes == null)
            {
                log("winget instalado nesta máquina: versão " + depois + ".");
                estado.Preparo.Add("winget instalado: versão " + depois + ".");
            }

            if (winget == null)
            {
                log("AVISO: winget continua indisponível — as instalações vão usar só as vias alternativas.");
                estado.Preparo.Add("winget indisponível: só as vias alternativas foram usadas.");
                estado.Salvar();
                Prog(progresso, 100, "");
                return;
            }

            if (controle != null && !controle.Prosseguir()) return;

            // 4) Cache de instaladores baixados. Cresce sem limite (centenas de
            //    MB em maquina usada) e guarda versoes velhas dos pacotes.
            Prog(progresso, 70, "Limpando o cache de instaladores...");
            LimparCache(estado, log);

            // 5) O indice de manifestos - o item 3 da explicacao la em cima.
            Prog(progresso, 82, "Baixando os catálogos de pacotes mais recentes...");
            AtualizarFontes(estado, log);

            estado.Salvar();
            Prog(progresso, 100, "Instaladores prontos.");
        }

        static void Prog(Action<int, string> progresso, int pct, string detalhe)
        {
            if (progresso != null) progresso(pct, detalhe);
        }

        string VersaoWinget()
        {
            if (winget == null) return null;
            var r = Executor.Rodar(winget, "--version");
            if (!r.Ok || r.Saida == null) return null;
            string v = r.Saida.Trim();
            return v.Length == 0 ? null : v;
        }

        // Rele o PATH do registro (maquina + usuario) e aplica no processo.
        // Sem isto, um instalador que acabou de acrescentar sua pasta ao PATH
        // so seria encontrado na proxima vez que o programa abrisse.
        static void RecarregarAmbiente(Action<string> log)
        {
            try
            {
                string maquina = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
                    "Path", "") as string;
                string usuario = Registry.GetValue(@"HKEY_CURRENT_USER\Environment", "Path", "") as string;
                string novo = ((maquina ?? "") + ";" + (usuario ?? "")).Trim(';');
                if (novo.Length > 0)
                    Environment.SetEnvironmentVariable("PATH", Environment.ExpandEnvironmentVariables(novo));
            }
            catch (Exception ex)
            {
                log("Aviso: não consegui recarregar o PATH do processo — " + ex.Message);
            }
        }

        void LimparCache(Estado estado, Action<string> log)
        {
            try
            {
                string pasta = Path.Combine(Path.GetTempPath(), "WinGet");
                if (!Directory.Exists(pasta)) return;
                long bytes = 0;
                try
                {
                    foreach (string f in Directory.GetFiles(pasta, "*", SearchOption.AllDirectories))
                        bytes += new FileInfo(f).Length;
                }
                catch { }
                // Arquivo travado por outro processo e ignorado de proposito:
                // limpar o cache e higiene, nao pre-requisito.
                ApagarOQuePuder(pasta);
                if (bytes > 0)
                {
                    string m = string.Format("Cache de instaladores limpo ({0:N0} MB).", bytes / 1048576);
                    log(m);
                    estado.Preparo.Add(m);
                }
            }
            catch (Exception ex)
            {
                log("Aviso: não consegui limpar o cache do winget — " + ex.Message);
            }
        }

        static void ApagarOQuePuder(string pasta)
        {
            foreach (string f in Directory.GetFiles(pasta))
            {
                try { File.Delete(f); }
                catch { }
            }
            foreach (string d in Directory.GetDirectories(pasta))
            {
                ApagarOQuePuder(d);
                try { Directory.Delete(d); }
                catch { }
            }
        }

        void AtualizarFontes(Estado estado, Action<string> log)
        {
            log("Atualizando os catálogos de pacotes (winget e Microsoft Store)...");
            var r = Executor.Rodar(winget, "source update --disable-interactivity",
                Encoding.UTF8, delegate(string l)
                {
                    string t = l.Trim();
                    if (t.Length > 0) log("    " + t);
                });
            if (r.Ok)
            {
                log("Catálogos atualizados — os manifestos agora são os mais recentes.");
                estado.Preparo.Add("Catálogos de pacotes atualizados.");
                return;
            }

            // Indice corrompido: reconstroi as fontes padrao do zero. So neste
            // caso, porque o reset apaga tambem qualquer fonte que o tecnico
            // tenha acrescentado a mao na maquina.
            log(string.Format("Falha ao atualizar os catálogos (0x{0:X8}); reconstruindo as fontes...", r.Codigo));
            Executor.Rodar(winget, "source reset --force --disable-interactivity", Encoding.UTF8, null);
            var r2 = Executor.Rodar(winget, "source update --disable-interactivity", Encoding.UTF8, null);
            if (r2.Ok)
            {
                log("Fontes reconstruídas e catálogos atualizados.");
                estado.Preparo.Add("Fontes do winget reconstruídas e catálogos atualizados.");
            }
            else
            {
                log("Não consegui atualizar os catálogos; sigo com o que está em cache.");
                estado.Preparo.Add("Catálogos não puderam ser atualizados (segui com o cache local).");
            }
        }

        // ------------------------------------------------------------------
        // Instalacao de um app do catalogo
        // ------------------------------------------------------------------

        // "progresso" recebe (percentual, detalhe) e pode ser nulo.
        public AppInstalado Instalar(AppCatalogo alvo, Action<string> log, Action<int, string> progresso)
        {
            var app = new AppInstalado();
            app.Nome = alvo.Nome;
            app.Id = alvo.Chave;

            // Ja esta na maquina? Nao mexe.
            string jaTem = Conferir(alvo);
            if (jaTem != null)
            {
                app.Versao = jaTem == "?" ? null : jaTem;
                app.Status = "já estava instalado";
                log(alvo.Nome + ": já estava instalado — mantido.");
                return app;
            }

            string ultimoMotivo = null;
            for (int i = 0; i < alvo.Metodos.Length; i++)
            {
                MetodoInstalacao m = alvo.Metodos[i];
                if (m.Via == Via.Winget && winget == null)
                {
                    ultimoMotivo = "winget indisponível";
                    continue;
                }
                if (m.Via == Via.MSStore && winget == null)
                {
                    ultimoMotivo = "winget indisponível (a Loja é acionada por ele)";
                    continue;
                }

                if (i > 0)
                    log(string.Format("{0}: tentando pela via alternativa ({1})...", alvo.Nome, m.Rotulo));

                string motivo;
                bool jaEstava;
                Executar(alvo, m, log, progresso, out motivo, out jaEstava);
                if (motivo != null) ultimoMotivo = motivo;

                string versao = Conferir(alvo);
                if (versao != null)
                {
                    app.Versao = versao == "?" ? null : versao;
                    app.Status = jaEstava
                        ? "já estava instalado/atualizado"
                        : (i == 0 ? "instalado" : "instalado (" + m.Rotulo + ")");
                    log(string.Format("{0} — {1}{2}", app.Nome, app.Status,
                        app.Versao == null ? "" : " (versão " + app.Versao + ")"));
                    return app;
                }
            }

            app.Status = "falhou" + (ultimoMotivo == null ? "" : ": " + ultimoMotivo);
            log(alvo.Nome + " — " + app.Status);
            return app;
        }

        // Roda UM metodo. Nao decide nada sobre sucesso: quem confere e o
        // chamador, olhando a maquina.
        void Executar(AppCatalogo alvo, MetodoInstalacao m, Action<string> log,
            Action<int, string> progresso, out string motivo, out bool jaEstava)
        {
            motivo = null;
            jaEstava = false;
            try
            {
                switch (m.Via)
                {
                    case Via.Winget:
                        PorWinget(alvo, m, "", log, progresso, out motivo, out jaEstava);
                        return;
                    case Via.MSStore:
                        PorLoja(alvo, m, log, progresso, out motivo, out jaEstava);
                        return;
                    case Via.PowerShell:
                        PorPowerShell(alvo, m, log, out motivo);
                        return;
                    case Via.Direto:
                        PorInstaladorOficial(alvo, m, log, progresso, out motivo);
                        return;
                    case Via.ODT:
                        PorOfficeDeploymentTool(log, progresso, out motivo);
                        return;
                }
            }
            catch (Exception ex)
            {
                motivo = ex.Message;
                log(alvo.Nome + ": erro na via " + m.Rotulo + " — " + ex.Message);
            }
        }

        void PorWinget(AppCatalogo alvo, MetodoInstalacao m, string extras, Action<string> log,
            Action<int, string> progresso, out string motivo, out bool jaEstava)
        {
            var parse = new ParseWinget(log, progresso, alvo.Nome);
            var r = Executor.Rodar(winget,
                "install --id " + m.Alvo + " -e --silent" + ARGS_COMUNS + extras,
                Encoding.UTF8, parse.Linha);
            motivo = parse.UltimaMensagem;
            jaEstava = SemAtualizacao(r);
            if (r.Codigo == 0 || jaEstava) return;

            log(string.Format("{0}: winget retornou 0x{1:X8}{2}", alvo.Nome, r.Codigo,
                motivo == null ? "" : " — " + motivo));

            // Repeticao SO quando o problema e o hash. O caso classico e o
            // fabricante republicar o instalador no mesmo endereco e o hash do
            // manifesto do winget ficar velho: o download continua vindo do
            // site oficial por HTTPS, so a conferencia contra o manifesto e que
            // nao fecha. Repetir por qualquer outro motivo so gera ruido.
            if (extras.Length == 0 && FalhaDeHash(r, motivo) && LiberarHash(log))
            {
                log(alvo.Nome + ": hash do manifesto desatualizado — repetindo sem essa conferência...");
                string ignorado;
                bool ignorado2;
                PorWinget(alvo, m, " --ignore-security-hash --force", log, progresso,
                    out ignorado, out ignorado2);
                if (ignorado != null) motivo = ignorado;
            }
        }

        void PorLoja(AppCatalogo alvo, MetodoInstalacao m, Action<string> log,
            Action<int, string> progresso, out string motivo, out bool jaEstava)
        {
            var parse = new ParseWinget(log, progresso, alvo.Nome);
            var r = Executor.Rodar(winget,
                "install --id " + m.Alvo + " -e --source msstore" + ARGS_COMUNS,
                Encoding.UTF8, parse.Linha);
            motivo = parse.UltimaMensagem;
            jaEstava = SemAtualizacao(r);
            if (r.Codigo != 0 && !jaEstava)
                log(string.Format("{0}: a Microsoft Store retornou 0x{1:X8}{2}", alvo.Nome, r.Codigo,
                    motivo == null ? "" : " — " + motivo));
        }

        // O comando vai para um .ps1 temporario em vez da linha de comando:
        // os instaladores oficiais por script ("irm ... | iex") sao cheios de
        // aspas e chaves, que nao sobrevivem inteiros a um -Command.
        void PorPowerShell(AppCatalogo alvo, MetodoInstalacao m, Action<string> log, out string motivo)
        {
            log(alvo.Nome + ": rodando o script oficial de instalação no PowerShell...");
            var r = RodarPowerShell(m.Alvo, log);
            motivo = r.Codigo == 0 ? null : "o script terminou com código " + r.Codigo;
            if (r.Codigo != 0)
                log(string.Format("{0}: o script terminou com código {1}.", alvo.Nome, r.Codigo));
        }

        static ResultadoExec RodarPowerShell(string comando, Action<string> log)
        {
            string arquivo = Path.Combine(Path.GetTempPath(),
                "slt-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ps1");
            try
            {
                File.WriteAllText(arquivo,
                    "$ProgressPreference = 'SilentlyContinue'\r\n" +
                    "[Net.ServicePointManager]::SecurityProtocol = " +
                    "[Net.SecurityProtocolType]::Tls12 -bor [Net.ServicePointManager]::SecurityProtocol\r\n" +
                    comando + "\r\n",
                    new UTF8Encoding(true));
                string ps = Path.Combine(Environment.SystemDirectory,
                    @"WindowsPowerShell\v1.0\powershell.exe");
                return Executor.Rodar(ps,
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + arquivo + "\"",
                    Encoding.UTF8,
                    log == null ? null : (Action<string>)delegate(string l)
                    {
                        string t = l.Trim();
                        if (t.Length > 0) log("    " + t);
                    });
            }
            finally
            {
                try { File.Delete(arquivo); } catch { }
            }
        }

        // Ultimo recurso: baixa o instalador do site do proprio fabricante,
        // sempre de um endereco que aponta para a versao mais recente.
        void PorInstaladorOficial(AppCatalogo alvo, MetodoInstalacao m, Action<string> log,
            Action<int, string> progresso, out string motivo)
        {
            motivo = null;
            string arquivo = null;
            try
            {
                log(alvo.Nome + ": baixando o instalador oficial do fabricante...");
                Tls12();
                arquivo = Path.Combine(Path.GetTempPath(),
                    "slt-" + alvo.Chave + Extensao(m.Alvo));

                Exception falhaDownload = null;
                using (var wc = new WebClient())
                using (var pronto = new ManualResetEvent(false))
                {
                    wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        if (progresso != null)
                            progresso(e.ProgressPercentage,
                                string.Format("{0}: baixando do site oficial — {1}%",
                                    alvo.Nome, e.ProgressPercentage));
                    };
                    wc.DownloadFileCompleted += delegate(object s, AsyncCompletedEventArgs e)
                    {
                        falhaDownload = e.Error;
                        pronto.Set();
                    };
                    wc.DownloadFileAsync(new Uri(m.Alvo), arquivo);
                    pronto.WaitOne();
                }

                // Sem esta conferencia, um download que falhou (404, queda de
                // rede) seguiria para o msiexec com um arquivo invalido.
                if (falhaDownload != null)
                {
                    motivo = "falha no download do instalador oficial (" + falhaDownload.Message + ")";
                    log(alvo.Nome + ": " + motivo);
                    return;
                }
                long tamanho = File.Exists(arquivo) ? new FileInfo(arquivo).Length : 0;
                if (tamanho < 100000)
                {
                    motivo = string.Format("o instalador baixado tem só {0} bytes — descartado", tamanho);
                    log(alvo.Nome + ": " + motivo);
                    return;
                }
                log(string.Format("{0}: baixado ({1:N0} MB).", alvo.Nome, tamanho / 1048576));
                if (progresso != null)
                    progresso(100, alvo.Nome + ": instalando em silêncio...");

                ResultadoExec r;
                if (Extensao(m.Alvo).Equals(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    log(alvo.Nome + ": instalando pelo MSI oficial, em silêncio...");
                    r = Executor.Rodar("msiexec.exe", "/i \"" + arquivo + "\" /qn /norestart");
                }
                else
                {
                    log(alvo.Nome + ": executando o instalador oficial em silêncio...");
                    r = Executor.Rodar(arquivo, m.Args == null ? "" : m.Args);
                }
                // 3010 = instalou, pede reinicializacao depois
                if (r.Codigo == 0 || r.Codigo == 3010)
                    log(alvo.Nome + ": instalador oficial concluído.");
                else
                {
                    motivo = "o instalador oficial retornou " + r.Codigo;
                    log(alvo.Nome + ": " + motivo + ".");
                }
            }
            catch (Exception ex)
            {
                motivo = "falha no instalador oficial (" + ex.Message + ")";
                log(alvo.Nome + ": " + motivo);
            }
            finally
            {
                try { if (arquivo != null && File.Exists(arquivo)) File.Delete(arquivo); }
                catch { }
            }
        }

        void PorOfficeDeploymentTool(Action<string> log, Action<int, string> progresso, out string motivo)
        {
            var office = new InstaladorOffice();
            AppInstalado r = office.Instalar(log, progresso == null
                ? delegate(int p, string d) { }
                : progresso);
            motivo = r.Versao == null ? r.Status : null;
        }

        // Alguns enderecos "latest" nao trazem a extensao no caminho; nesses
        // casos o instalador e .exe.
        static string Extensao(string url)
        {
            try
            {
                string ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (!string.IsNullOrEmpty(ext) &&
                    (ext.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                    return ext;
            }
            catch { }
            return ".exe";
        }

        static bool FalhaDeHash(ResultadoExec r, string motivo)
        {
            if (r.Codigo == COD_HASH) return true;
            string s = ((r.Saida ?? "") + (r.Erro ?? "") + (motivo ?? "")).ToLowerInvariant();
            return s.Contains("hash");
        }

        // O winget recusa --ignore-security-hash enquanto um administrador nao
        // habilitar o recurso; sem isto a segunda tentativa morre com erro de
        // argumento (e despeja a ajuda inteira no log). O app roda elevado.
        bool LiberarHash(Action<string> log)
        {
            if (hashLiberado) return true;
            var r = Executor.Rodar(winget, "settings --enable InstallerHashOverride");
            hashLiberado = (r.Codigo == 0);
            if (hashLiberado)
                log("winget: liberada a permissão para ignorar hash desatualizado de manifesto.");
            else
                log(string.Format("winget: não consegui liberar essa permissão (0x{0:X8}) — " +
                    "seguindo para a via seguinte.", r.Codigo));
            return hashLiberado;
        }

        static bool SemAtualizacao(ResultadoExec r)
        {
            if (r.Codigo == COD_SEM_ATUALIZACAO) return true;
            string s = ((r.Saida ?? "") + "\n" + (r.Erro ?? "")).ToLowerInvariant();
            return s.Contains("already installed") || s.Contains("já está instalado");
        }

        // ------------------------------------------------------------------
        // Conferencia: a maquina e que diz se o programa esta instalado
        // ------------------------------------------------------------------

        // Devolve a versao encontrada, "?" quando esta instalado mas sem versao
        // legivel, ou null quando nao esta instalado.
        public string Conferir(AppCatalogo alvo)
        {
            foreach (MetodoInstalacao m in alvo.Metodos)
            {
                if (m.Via == Via.ODT)
                {
                    string v = InstaladorOffice.VersaoInstalada();
                    if (v != null) return v;
                    continue;
                }
                // O "winget list" enxerga tanto os pacotes do winget quanto os
                // da Microsoft Store, pelo mesmo id usado na instalacao.
                if (m.Via == Via.Winget || m.Via == Via.MSStore)
                {
                    string v = ObterVersao(m.Alvo);
                    if (v != null) return v;
                }
            }
            // Vias sem id de pacote (script e instalador oficial) so podem ser
            // conferidas pelo id do winget, se o app tiver um.
            return null;
        }

        // Le a versao na saida de "winget list --id X -e". Quando o nome do
        // pacote e comprido, o winget quebra a linha e a versao cai na linha
        // de baixo — por isso a continuacao.
        public string ObterVersao(string id)
        {
            if (winget == null || string.IsNullOrEmpty(id)) return null;
            var r = Executor.Rodar(winget,
                "list --id " + id + " -e --accept-source-agreements --disable-interactivity",
                Encoding.UTF8, null);
            if (r.Saida == null) return null;

            string[] linhas = r.Saida.Replace("\r", "\n").Split('\n');
            for (int i = 0; i < linhas.Length; i++)
            {
                if (linhas[i].IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string[] tok = linhas[i].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int t = 0; t < tok.Length; t++)
                    if (string.Equals(tok[t], id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (t + 1 < tok.Length) return tok[t + 1];
                        // Linha quebrada: a versao abre a proxima linha util.
                        for (int j = i + 1; j < linhas.Length; j++)
                        {
                            string[] seg = linhas[j].Split(
                                new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (seg.Length > 0) return seg[0];
                        }
                        return "?";
                    }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Atualizacao geral dos programas de desktop
        // ------------------------------------------------------------------

        // Roda "winget upgrade --all" em passadas repetidas ate nao sobrar
        // atualizacao nenhuma (max. 5). Os apps UWP da Loja sao cobertos pela
        // etapa da LojaMicrosoft (AppInstallManager); aqui ficam os programas
        // de desktop.
        public void AtualizarTudo(Estado estado, Action<string> log, Action<int> progresso,
            ControleExecucao controle)
        {
            if (winget == null) return;

            for (int passada = 1; passada <= 5; passada++)
            {
                if (controle != null && !controle.Prosseguir()) return;
                log(string.Format("Passada {0}: winget upgrade --all...", passada));
                var parse = new ParseWinget(log, progresso == null
                    ? null
                    : (Action<int, string>)delegate(int p, string d) { progresso(p); }, null);
                var r = Executor.Rodar(winget,
                    "upgrade --all --include-unknown --silent" + ARGS_COMUNS,
                    Encoding.UTF8, parse.Linha);
                estado.Upgrades.Add(string.Format("Passada {0} concluída (código {1}).", passada, r.Codigo));
                estado.Salvar();

                if (controle != null && !controle.Prosseguir()) return;
                var pendentes = Executor.Rodar(winget,
                    "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
                    Encoding.UTF8, null, controle);
                if (!TemTabela(pendentes.Saida))
                {
                    log("Nenhuma atualização de aplicativo pendente — tudo em dia.");
                    estado.Upgrades.Add("Verificação final: nenhum aplicativo pendente.");
                    estado.Salvar();
                    return;
                }
            }
            log("Limite de passadas atingido; alguns apps podem atualizar sozinhos depois.");
            estado.Upgrades.Add("Limite de 5 passadas atingido.");
            estado.Salvar();
        }

        // Uma "tabela" na saida do winget (lista de pendencias) tem uma linha
        // separadora de cabecalho composta so de tracos.
        static bool TemTabela(string saida)
        {
            if (saida == null) return false;
            foreach (string l in saida.Replace("\r", "\n").Split('\n'))
                if (Regex.IsMatch(l.Trim(), "^-{5,}$")) return true;
            return false;
        }

        // Interpreta a saida do winget: extrai percentuais (ou "X MB / Y MB")
        // para a barra de progresso e repassa as linhas informativas ao log.
        class ParseWinget
        {
            static readonly Regex RxPct = new Regex(@"(\d{1,3})(?:[.,]\d+)?\s*%");
            static readonly Regex RxBytes = new Regex(
                @"([\d.,]+)\s*(KB|MB|GB)\s*/\s*([\d.,]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);

            readonly Action<string> log;
            readonly Action<int, string> progresso;
            readonly string nome;
            int ultimoPct = -1;
            string ultimaLinha;

            // Ultima linha informativa que o winget escreveu. Em caso de falha
            // e ela que explica o motivo, na lingua do sistema e melhor do que
            // qualquer tabela de codigos que eu mantivesse aqui.
            public string UltimaMensagem;

            public ParseWinget(Action<string> log, Action<int, string> progresso, string nome)
            {
                this.log = log;
                this.progresso = progresso;
                this.nome = nome;
            }

            public void Linha(string bruta)
            {
                if (bruta == null) return;
                string l = bruta.Trim();
                if (l.Length == 0) return;

                int pct = -1;
                MatchCollection ms = RxPct.Matches(l);
                if (ms.Count > 0)
                {
                    int v;
                    if (int.TryParse(ms[ms.Count - 1].Groups[1].Value, out v)) pct = v;
                }
                else
                {
                    Match mb = RxBytes.Match(l);
                    if (mb.Success)
                    {
                        double feito = ParseTamanho(mb.Groups[1].Value, mb.Groups[2].Value);
                        double total = ParseTamanho(mb.Groups[3].Value, mb.Groups[4].Value);
                        if (total > 0) pct = (int)(feito * 100 / total);
                    }
                }
                if (pct >= 0 && pct <= 100 && pct != ultimoPct && progresso != null)
                {
                    ultimoPct = pct;
                    progresso(pct, nome == null ? null : string.Format("{0}: {1}%", nome, pct));
                }

                if (SoBarraDeProgresso(l)) return;
                if (l == ultimaLinha) return;
                ultimaLinha = l;
                if (l.Length >= 15) UltimaMensagem = Encurtar(l);
                if (log != null) log("    " + l);
            }

            static string Encurtar(string l)
            {
                return l.Length <= 140 ? l : l.Substring(0, 139) + "…";
            }

            static bool SoBarraDeProgresso(string l)
            {
                foreach (char c in l)
                    if ("█▓▒░ -\\|/.,%0123456789KMGBibs".IndexOf(c) < 0) return false;
                return true;
            }

            static double ParseTamanho(string num, string unidade)
            {
                double v;
                double.TryParse(num.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out v);
                string u = unidade.ToUpperInvariant();
                if (u == "KB") return v * 1024;
                if (u == "MB") return v * 1024 * 1024;
                if (u == "GB") return v * 1024 * 1024 * 1024;
                return v;
            }
        }
    }
}
