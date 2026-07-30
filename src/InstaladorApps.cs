using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AutoInstall
{
    public class ProgramaAlvo
    {
        public string Nome;
        public string Id;
        // Instalador oficial do fabricante, usado so se o winget falhar em
        // todas as tentativas. .msi roda pelo msiexec; .exe usa ArgsReserva.
        public string UrlReserva;
        public string ArgsReserva;

        public ProgramaAlvo(string nome, string id) { Nome = nome; Id = id; }

        public ProgramaAlvo(string nome, string id, string urlReserva, string argsReserva)
        {
            Nome = nome;
            Id = id;
            UrlReserva = urlReserva;
            ArgsReserva = argsReserva;
        }
    }

    // Instalacao de programas e atualizacao geral via winget (Windows Package
    // Manager), com bootstrap do App Installer caso o winget ainda nao exista
    // (comum logo apos formatar).
    public class InstaladorApps
    {
        public static readonly ProgramaAlvo[] Programas = new ProgramaAlvo[]
        {
            // Chrome tem reserva porque e o pacote mais sujeito a hash velho no
            // manifesto: o Google republica o instalador no mesmo endereco com
            // frequencia. O MSI corporativo abaixo e o endereco oficial.
            new ProgramaAlvo("Google Chrome", "Google.Chrome",
                "https://dl.google.com/tag/s/dl/chrome/install/googlechromestandaloneenterprise64.msi", null),
            new ProgramaAlvo("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit"),
            new ProgramaAlvo("WinRAR", "RARLab.WinRAR"),
            // K-Lite Standard = codecs de audio/video atualizados + MPC-HC,
            // player leve e recomendado (melhor que os que vem com o Windows)
            new ProgramaAlvo("K-Lite Codec Pack (codecs + player MPC-HC)", "CodecGuide.K-LiteCodecPack.Standard"),
            // O Office NAO entra aqui: o pacote Microsoft.Office do winget so
            // baixa o motor do Office Deployment Tool e sai sem instalar nada.
            // Ele tem instalador proprio em InstaladorOffice.cs.
        };

        const string ARGS_COMUNS = " --accept-package-agreements --accept-source-agreements --disable-interactivity";

        string winget;

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
            log("ERRO: winget indisponível — a instalação de programas não pôde ser automatizada.");
            return false;
        }

        void BootstrapWinget(Action<string> log)
        {
            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
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
            string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            string cmd = "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                "Add-AppxPackage -Path '" + Path.Combine(tmp, "vclibs.appx") + "' -ErrorAction SilentlyContinue; " +
                "Add-AppxPackage -Path '" + Path.Combine(tmp, "uixaml.appx") + "' -ErrorAction SilentlyContinue; " +
                "Add-AppxPackage -Path '" + Path.Combine(tmp, "appinstaller.msixbundle") + "\'\"";
            Executor.Rodar(ps, cmd);
        }

        // 0x8A15002B: "nenhuma atualizacao aplicavel" - ja esta na ultima versao.
        const int COD_SEM_ATUALIZACAO = unchecked((int)0x8A15002B);
        // 0x8A150011: o hash do instalador nao confere com o do manifesto.
        const int COD_HASH = unchecked((int)0x8A150011);

        bool hashLiberado;

        public AppInstalado Instalar(ProgramaAlvo alvo, Action<string> log, Action<int> progresso)
        {
            var app = new AppInstalado();
            app.Nome = alvo.Nome;
            app.Id = alvo.Id;
            if (winget == null)
            {
                app.Status = "não instalado (winget indisponível)";
                return app;
            }

            string motivo;
            var r = Tentar(alvo, "", log, progresso, out motivo);
            // A explicacao boa e a da PRIMEIRA tentativa: as seguintes usam
            // argumentos extras e, se algo der errado nelas, a mensagem final
            // seria sobre os argumentos, nao sobre o problema de verdade.
            string motivoReal = motivo;
            bool jaEstava = SemAtualizacao(r);

            if (r.Codigo != 0 && !jaEstava)
            {
                log(string.Format("{0}: winget falhou (0x{1:X8}){2}", alvo.Nome, r.Codigo,
                    motivo == null ? "" : " — " + motivo));

                // Segunda tentativa SO quando o problema e o hash - o caso
                // classico (e o do Chrome) e o fabricante republicar o
                // instalador no mesmo endereco e o hash do manifesto do winget
                // ficar velho: o download continua vindo do site oficial por
                // HTTPS, so a conferencia contra o manifesto e que nao fecha.
                // Repetir por qualquer outro motivo so gera ruido.
                if (FalhaDeHash(r, motivo) && LiberarHash(log))
                {
                    log(alvo.Nome + ": hash do manifesto desatualizado — repetindo sem essa conferência...");
                    string motivoRepeticao;
                    r = Tentar(alvo, " --ignore-security-hash --force", log, progresso, out motivoRepeticao);
                    if (r.Codigo != 0 && !SemAtualizacao(r))
                        log(string.Format("{0}: a repetição também falhou (0x{1:X8}){2}",
                            alvo.Nome, r.Codigo, motivoRepeticao == null ? "" : " — " + motivoRepeticao));
                }
            }

            if (r.Codigo != 0 && !SemAtualizacao(r) && !string.IsNullOrEmpty(alvo.UrlReserva))
                InstalarReserva(alvo, log, progresso);

            // Quem decide se instalou e o sistema, nao o codigo de saida: o
            // winget as vezes devolve erro com o programa instalado, e vice-versa.
            try { app.Versao = ObterVersao(alvo.Id); } catch { }

            if (app.Versao != null)
            {
                if (jaEstava) app.Status = "já estava instalado/atualizado";
                else if (r.Codigo == 0 || SemAtualizacao(r)) app.Status = "instalado";
                else app.Status = "instalado pelo instalador oficial do fabricante";
            }
            else
            {
                app.Status = string.Format("falhou (código 0x{0:X8}){1}", r.Codigo,
                    motivoReal == null ? "" : ": " + motivoReal);
            }
            return app;
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
                    "seguindo para o instalador do fabricante.", r.Codigo));
            return hashLiberado;
        }

        ResultadoExec Tentar(ProgramaAlvo alvo, string extras, Action<string> log,
            Action<int> progresso, out string motivo)
        {
            var parse = new ParseWinget(log, progresso);
            var r = Executor.Rodar(winget,
                "install --id " + alvo.Id + " -e --silent" + ARGS_COMUNS + extras,
                Encoding.UTF8, parse.Linha);
            motivo = parse.UltimaMensagem;
            return r;
        }

        static bool SemAtualizacao(ResultadoExec r)
        {
            if (r.Codigo == COD_SEM_ATUALIZACAO) return true;
            string s = ((r.Saida ?? "") + "\n" + (r.Erro ?? "")).ToLowerInvariant();
            return s.Contains("already installed") || s.Contains("já está instalado");
        }

        // Ultimo recurso: baixa o instalador do site do proprio fabricante.
        void InstalarReserva(ProgramaAlvo alvo, Action<string> log, Action<int> progresso)
        {
            string arquivo = null;
            try
            {
                log(alvo.Nome + ": baixando o instalador oficial do fabricante...");
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                string ext = Path.GetExtension(new Uri(alvo.UrlReserva).AbsolutePath);
                if (string.IsNullOrEmpty(ext)) ext = ".exe";
                arquivo = Path.Combine(Path.GetTempPath(), "slt-reserva" + ext);

                Exception falhaDownload = null;
                using (var wc = new WebClient())
                using (var pronto = new ManualResetEvent(false))
                {
                    wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        if (progresso != null) progresso(e.ProgressPercentage);
                    };
                    wc.DownloadFileCompleted += delegate(object s, AsyncCompletedEventArgs e)
                    {
                        falhaDownload = e.Error;
                        pronto.Set();
                    };
                    wc.DownloadFileAsync(new Uri(alvo.UrlReserva), arquivo);
                    pronto.WaitOne();
                }

                // Sem esta conferencia, um download que falhou (404, queda de
                // rede) seguiria para o msiexec com um arquivo invalido.
                if (falhaDownload != null)
                {
                    log(alvo.Nome + ": falha no download do instalador oficial — " + falhaDownload.Message);
                    return;
                }
                long tamanho = File.Exists(arquivo) ? new FileInfo(arquivo).Length : 0;
                if (tamanho < 100000)
                {
                    log(string.Format("{0}: o instalador baixado tem só {1} bytes — descartado.",
                        alvo.Nome, tamanho));
                    return;
                }
                log(string.Format("{0}: baixado ({1:N0} MB).", alvo.Nome, tamanho / 1048576));

                ResultadoExec r;
                if (ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    log(alvo.Nome + ": instalando pelo MSI oficial, em silêncio...");
                    r = Executor.Rodar("msiexec.exe", "/i \"" + arquivo + "\" /qn /norestart");
                }
                else
                {
                    log(alvo.Nome + ": executando o instalador oficial...");
                    r = Executor.Rodar(arquivo, alvo.ArgsReserva ?? "");
                }
                // 3010 = instalou, pede reinicializacao depois
                if (r.Codigo == 0 || r.Codigo == 3010)
                    log(alvo.Nome + ": instalador oficial concluído.");
                else
                    log(string.Format("{0}: instalador oficial retornou {1}.", alvo.Nome, r.Codigo));
            }
            catch (Exception ex)
            {
                log(alvo.Nome + ": falha no instalador de reserva — " + ex.Message);
            }
            finally
            {
                try { if (arquivo != null && File.Exists(arquivo)) File.Delete(arquivo); }
                catch { }
            }
        }

        public string ObterVersao(string id)
        {
            if (winget == null) return null;
            var r = Executor.Rodar(winget,
                "list --id " + id + " -e --accept-source-agreements --disable-interactivity",
                Encoding.UTF8, null);
            if (r.Saida == null) return null;
            foreach (string l in r.Saida.Replace("\r", "\n").Split('\n'))
            {
                if (l.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string[] tok = l.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tok.Length; i++)
                    if (string.Equals(tok[i], id, StringComparison.OrdinalIgnoreCase) && i + 1 < tok.Length)
                        return tok[i + 1];
            }
            return null;
        }

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
                var parse = new ParseWinget(log, progresso);
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
            readonly Action<int> progresso;
            int ultimoPct = -1;
            string ultimaLinha;

            // Ultima linha informativa que o winget escreveu. Em caso de falha
            // e ela que explica o motivo, na lingua do sistema e melhor do que
            // qualquer tabela de codigos que eu mantivesse aqui.
            public string UltimaMensagem;

            public ParseWinget(Action<string> log, Action<int> progresso)
            {
                this.log = log;
                this.progresso = progresso;
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
                    progresso(pct);
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
