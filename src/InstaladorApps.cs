using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoInstall
{
    public class ProgramaAlvo
    {
        public string Nome;
        public string Id;
        public ProgramaAlvo(string nome, string id) { Nome = nome; Id = id; }
    }

    // Instalacao de programas e atualizacao geral via winget (Windows Package
    // Manager), com bootstrap do App Installer caso o winget ainda nao exista
    // (comum logo apos formatar).
    public class InstaladorApps
    {
        public static readonly ProgramaAlvo[] Programas = new ProgramaAlvo[]
        {
            new ProgramaAlvo("Google Chrome", "Google.Chrome"),
            new ProgramaAlvo("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit"),
            new ProgramaAlvo("WinRAR", "RARLab.WinRAR"),
            // K-Lite Standard = codecs de audio/video atualizados + MPC-HC,
            // player leve e recomendado (melhor que os que vem com o Windows)
            new ProgramaAlvo("K-Lite Codec Pack (codecs + player MPC-HC)", "CodecGuide.K-LiteCodecPack.Standard"),
            new ProgramaAlvo("Microsoft 365 (Office)", "Microsoft.Office"),
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

            var parse = new ParseWinget(log, progresso);
            var r = Executor.Rodar(winget,
                "install --id " + alvo.Id + " -e --silent" + ARGS_COMUNS,
                Encoding.UTF8, parse.Linha);

            string saida = ((r.Saida ?? "") + "\n" + (r.Erro ?? "")).ToLowerInvariant();
            if (r.Codigo == 0)
                app.Status = "instalado";
            else if (r.Codigo == -1978335189 ||                        // sem atualizacao aplicavel
                     saida.Contains("already installed") ||
                     saida.Contains("já está instalado"))
                app.Status = "já estava instalado/atualizado";
            else
                app.Status = string.Format("falhou (código 0x{0:X8})", r.Codigo);

            if (app.Status == "instalado" || app.Status.StartsWith("já"))
            {
                try { app.Versao = ObterVersao(alvo.Id); } catch { }
            }
            return app;
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
        public void AtualizarTudo(Estado estado, Action<string> log, Action<int> progresso)
        {
            if (winget == null) return;

            for (int passada = 1; passada <= 5; passada++)
            {
                log(string.Format("Passada {0}: winget upgrade --all...", passada));
                var parse = new ParseWinget(log, progresso);
                var r = Executor.Rodar(winget,
                    "upgrade --all --include-unknown --silent" + ARGS_COMUNS,
                    Encoding.UTF8, parse.Linha);
                estado.Upgrades.Add(string.Format("Passada {0} concluída (código {1}).", passada, r.Codigo));
                estado.Salvar();

                var pendentes = Executor.Rodar(winget,
                    "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
                    Encoding.UTF8, null);
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
                if (log != null) log("    " + l);
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
