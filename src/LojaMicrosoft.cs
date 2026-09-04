using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace AutoInstall
{
    // Os scripts PowerShell do projeto sao embutidos no exe como recurso e
    // extraidos para %TEMP% na hora de rodar. Dois deles usam a API WinRT da
    // Loja, que so existe no Windows PowerShell 5.1 - por isso script, e nao
    // codigo C#.
    public static class ScriptEmbutido
    {
        public static string Carregar(string nome)
        {
            try
            {
                var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(nome);
                if (st != null)
                    using (var sr = new StreamReader(st, Encoding.UTF8))
                        return sr.ReadToEnd();
            }
            catch { }
            // Fallback: arquivo ao lado do exe (caso compilado sem o recurso)
            try
            {
                string p = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "tools", nome);
                if (File.Exists(p)) return File.ReadAllText(p);
            }
            catch { }
            return null;
        }

        // Devolve o caminho do .ps1 pronto para rodar, ou null se o recurso
        // nao estiver la.
        public static string Extrair(string nome)
        {
            string script = Carregar(nome);
            if (script == null) return null;
            string caminho = Path.Combine(Path.GetTempPath(), "slt-" + nome);
            try
            {
                File.WriteAllText(caminho, script, new UTF8Encoding(true));
                return caminho;
            }
            catch { return null; }
        }

        public static string PowerShell
        {
            get
            {
                return Path.Combine(Environment.SystemDirectory,
                    @"WindowsPowerShell\v1.0\powershell.exe");
            }
        }
    }

    // Atualiza os PROPRIOS instaladores antes de qualquer instalacao: o
    // pacote do winget (Microsoft.DesktopAppInstaller) e o cliente da Loja
    // (Microsoft.WindowsStore), pela API da Loja - que nao depende de nenhum
    // dos dois estar funcionando.
    public class LojaAppInstaller
    {
        public Action<string> AoLogar;
        public Action<int, string> AoProgredir;

        public int Atualizados;
        public int Erros;
        public bool Terminou;
        public string Falha;

        string alvoAtual;

        public bool Atualizar(ControleExecucao controle)
        {
            string caminho = ScriptEmbutido.Extrair("preparar-instaladores.ps1");
            if (caminho == null)
            {
                Log("Preparo: script de atualização dos instaladores não encontrado.");
                return false;
            }

            var r = Executor.Rodar(ScriptEmbutido.PowerShell,
                "-NoProfile -ExecutionPolicy Bypass -File \"" + caminho + "\"",
                null, TratarLinha, controle);
            try { File.Delete(caminho); }
            catch { }

            if (Falha != null)
            {
                Log("Preparo: a API da Loja falhou (" + Falha + ") — sigo com o que a máquina tem.");
                return false;
            }
            if (!Terminou)
            {
                Log(string.Format(
                    "Preparo: a atualização dos instaladores terminou sem confirmação (código {0}).",
                    r.Codigo));
                return false;
            }
            return true;
        }

        void Log(string m)
        {
            var h = AoLogar;
            if (h != null) h(m);
        }

        void Prog(int pct, string detalhe)
        {
            var h = AoProgredir;
            if (h != null) h(pct, detalhe);
        }

        // "Microsoft.WindowsStore_8wekyb3d8bbwe" -> "Microsoft Store"
        static string Amigavel(string familia)
        {
            if (familia == null) return "(pacote)";
            if (familia.StartsWith("Microsoft.DesktopAppInstaller")) return "App Installer (winget)";
            if (familia.StartsWith("Microsoft.WindowsStore")) return "Microsoft Store";
            int i = familia.IndexOf('_');
            return i > 0 ? familia.Substring(0, i) : familia;
        }

        void TratarLinha(string bruta)
        {
            if (bruta == null) return;
            string l = bruta.Trim();
            if (!l.StartsWith("SLT-")) return;

            if (l == "SLT-INICIO")
            {
                Log("Consultando a Loja por atualizações do winget e da Microsoft Store...");
                return;
            }
            if (l.StartsWith("SLT-INFO:"))
            {
                Log("Preparo: " + l.Substring(9));
                return;
            }
            if (l.StartsWith("SLT-ALVO:"))
            {
                alvoAtual = Amigavel(l.Substring(9));
                Prog(0, "Verificando " + alvoAtual + "...");
                return;
            }
            if (l.StartsWith("SLT-PROG:"))
            {
                string[] p = l.Split(':');
                int pct;
                if (p.Length >= 2 && int.TryParse(p[1], out pct))
                    Prog(pct, string.Format("Atualizando {0} — {1}%", alvoAtual, pct));
                return;
            }
            if (l.StartsWith("SLT-SEM:"))
            {
                Log("  [em dia] " + Amigavel(l.Substring(8)));
                return;
            }
            if (l.StartsWith("SLT-OK:"))
            {
                Atualizados++;
                Log("  [atualizado] " + Amigavel(l.Substring(7)));
                return;
            }
            if (l.StartsWith("SLT-ERRO:"))
            {
                Erros++;
                string[] p = l.Split(':');
                Log(string.Format("  [erro] {0}{1}", Amigavel(p.Length > 1 ? p[1] : null),
                    p.Length > 2 ? " (" + p[2] + ")" : ""));
                return;
            }
            if (l.StartsWith("SLT-FIM:"))
            {
                Terminou = true;
                return;
            }
            if (l.StartsWith("SLT-FALHA:"))
            {
                Falha = l.Substring(10);
                return;
            }
        }
    }

    // Atualiza os apps da Microsoft Store pela MESMA via do botao "Atualizar
    // todos" da Loja: a API WinRT AppInstallManager (SearchForAllUpdatesAsync),
    // acionada pelo script embutido tools\loja-update.ps1, que reporta o
    // andamento em linhas "SLT-..." interpretadas aqui.
    // (O winget nao cobre bem os apps UWP da Loja — por isso esta etapa.)
    public class LojaMicrosoft
    {
        public Action<string> AoLogar;
        public Action<int, string> AoProgredir;

        public int Total = -1;
        public int Concluidos;
        public int Erros;
        public bool Terminou;
        public string Falha;

        public bool Atualizar(Estado estado, ControleExecucao controle)
        {
            string caminho = ScriptEmbutido.Extrair("loja-update.ps1");
            if (caminho == null)
            {
                Log("Loja: script de atualização não encontrado no executável.");
                return false;
            }

            // O script so ACOMPANHA a fila da Loja; encerra-lo ao parar nao
            // interrompe nada - a Loja segue instalando por conta propria.
            var r = Executor.Rodar(ScriptEmbutido.PowerShell,
                "-NoProfile -ExecutionPolicy Bypass -File \"" + caminho + "\"",
                null, TratarLinha, controle);
            try { File.Delete(caminho); } catch { }

            if (controle != null && controle.Parando)
            {
                Log("Loja: acompanhamento encerrado a pedido (a Loja continua em segundo plano).");
                estado.Upgrades.Add("Microsoft Store: acompanhamento interrompido pelo técnico.");
                estado.Salvar();
                return true;
            }

            if (Falha != null)
            {
                Log("Loja: falha na API de atualização: " + Falha);
                estado.Upgrades.Add("Microsoft Store: falhou (" + Falha + ").");
                estado.Salvar();
                return false;
            }
            if (!Terminou)
            {
                Log(string.Format("Loja: o processo terminou sem confirmação (código {0}).", r.Codigo));
                estado.Upgrades.Add(string.Format("Microsoft Store: sem confirmação (código {0}).", r.Codigo));
                estado.Salvar();
                return false;
            }

            if (Total <= 0)
                estado.Upgrades.Add("Microsoft Store: nenhum app com atualização pendente.");
            else
                estado.Upgrades.Add(string.Format(
                    "Microsoft Store: {0} de {1} app(s) atualizado(s){2}.",
                    Concluidos, Total, Erros > 0 ? ", " + Erros + " com erro" : ""));
            estado.Salvar();
            return true;
        }

        void Log(string m)
        {
            var h = AoLogar;
            if (h != null) h(m);
        }

        void Prog(int pct, string detalhe)
        {
            var h = AoProgredir;
            if (h != null) h(pct, detalhe);
        }

        // "Microsoft.WindowsCalculator_8wekyb3d8bbwe" -> "Microsoft.WindowsCalculator"
        static string NomeAmigavel(string familia)
        {
            if (string.IsNullOrEmpty(familia)) return "(app)";
            int i = familia.IndexOf('_');
            return i > 0 ? familia.Substring(0, i) : familia;
        }

        void TratarLinha(string bruta)
        {
            if (bruta == null) return;
            string l = bruta.Trim();
            if (!l.StartsWith("SLT-")) return;

            if (l == "SLT-INICIO")
            {
                Log("Consultando atualizações na Microsoft Store...");
                return;
            }
            if (l.StartsWith("SLT-INFO:"))
            {
                Log("Loja: " + l.Substring(9));
                return;
            }
            if (l.StartsWith("SLT-TOTAL:"))
            {
                // O total e cumulativo: a verificacao roda mais de uma vez e
                // pode achar apps novos depois de instalar a primeira leva.
                int t;
                if (!int.TryParse(l.Substring(10), out t)) return;
                int antes = Total;
                Total = t;
                if (Total == 0)
                {
                    Log("Loja: nenhum app com atualização pendente (verificado mais de uma vez).");
                    Prog(100, "Microsoft Store em dia.");
                }
                else if (antes > 0)
                {
                    Log(string.Format("Loja: mais {0} app(s) na nova verificação — total {1}.",
                        Total - antes, Total));
                }
                else
                {
                    Log(string.Format("Loja: {0} app(s) com atualização — baixando e instalando...", Total));
                    Prog(0, null);
                }
                return;
            }
            if (l.StartsWith("SLT-PROG:"))
            {
                string[] p = l.Split(':');
                int pct, feitos, total;
                if (p.Length >= 4 && int.TryParse(p[1], out pct) &&
                    int.TryParse(p[2], out feitos) && int.TryParse(p[3], out total))
                    Prog(pct, string.Format("Microsoft Store: {0} de {1} concluído(s)", feitos, total));
                return;
            }
            if (l.StartsWith("SLT-OK:"))
            {
                Concluidos++;
                Log("  [ok] " + NomeAmigavel(l.Substring(7)));
                return;
            }
            if (l.StartsWith("SLT-ERRO:"))
            {
                Erros++;
                string[] p = l.Split(':');
                Log("  [erro] " + NomeAmigavel(p.Length > 1 ? p[1] : ""));
                return;
            }
            if (l == "SLT-TEMPO")
            {
                Log("Loja: tempo limite atingido; o restante continua baixando em segundo plano.");
                return;
            }
            if (l.StartsWith("SLT-FIM:"))
            {
                Terminou = true;
                return;
            }
            if (l.StartsWith("SLT-FALHA:"))
            {
                Falha = l.Substring(10);
                return;
            }
        }
    }
}
