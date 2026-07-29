using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.Win32;

namespace AutoInstall
{
    // Instalacao do Office 365.
    //
    // POR QUE NAO PELO WINGET: o pacote "Microsoft.Office" do winget baixa o
    // https://officecdn.microsoft.com/pr/wsus/setup.exe, que e o motor do
    // Office Deployment Tool. Esse setup.exe nao instala nada sozinho - ele
    // precisa de um XML dizendo qual produto, qual idioma e qual arquitetura.
    // Sem isso ele sai sem instalar e o winget ainda assim devolve sucesso.
    // Era essa a falha em campo: o app dizia "instalado" e nao havia Word nem
    // Excel na maquina. Aqui o mesmo setup.exe oficial e usado, porem com o
    // XML explicito, e no fim a instalacao e CONFERIDA no registro.
    public class InstaladorOffice
    {
        // Sempre o Microsoft 365 Personal/Família (a edicao de consumidor), em
        // portugues. E a que combina com a assinatura da clientela da loja.
        public const string PRODUTO = "O365HomePremRetail";
        public const string NOME = "Microsoft 365 Personal/Família";

        const string URL_SETUP = "https://officecdn.microsoft.com/pr/wsus/setup.exe";
        const string CHAVE_C2R = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";
        const int TIMEOUT_MIN = 75;

        // Le a versao direto do registro do Click-to-Run: e a unica prova de
        // que o Office esta mesmo instalado.
        public static string VersaoInstalada()
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(CHAVE_C2R))
                {
                    if (k == null) return null;
                    string produtos = k.GetValue("ProductReleaseIds") as string;
                    string versao = k.GetValue("VersionToReport") as string;
                    if (string.IsNullOrEmpty(produtos) || string.IsNullOrEmpty(versao)) return null;
                    return versao;
                }
            }
            catch { return null; }
        }

        public static string ProdutosInstalados()
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(CHAVE_C2R))
                    return k == null ? null : k.GetValue("ProductReleaseIds") as string;
            }
            catch { return null; }
        }

        // "progresso" recebe (percentual, detalhe). O Office Deployment Tool
        // nao publica percentual nenhum, entao aqui ele e uma estimativa pelo
        // tempo decorrido - e o texto ao lado diz exatamente isso.
        public AppInstalado Instalar(Action<string> log, Action<int, string> progresso)
        {
            var app = new AppInstalado();
            app.Nome = "Microsoft Office 365";
            app.Id = PRODUTO;

            string jaTem = VersaoInstalada();
            if (jaTem != null)
            {
                app.Nome = "Microsoft Office (" + (ProdutosInstalados() ?? "Click-to-Run") + ")";
                app.Versao = jaTem;
                app.Status = "já estava instalado";
                log("Office: já instalado nesta máquina (versão " + jaTem + ") — mantido.");
                return app;
            }

            app.Nome = "Microsoft Office 365 — " + NOME;
            string pasta = Path.Combine(Path.GetTempPath(), "slt-office");
            string setup = Path.Combine(pasta, "setup.exe");
            string xml = Path.Combine(pasta, "config.xml");
            try
            {
                Directory.CreateDirectory(pasta);
                log("Office: baixando o instalador oficial da Microsoft...");
                progresso(2, "Baixando o instalador do Office...");
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                using (var wc = new WebClient()) wc.DownloadFile(URL_SETUP, setup);
                File.WriteAllText(xml, MontarXml(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                app.Status = "falhou ao baixar o instalador (" + ex.Message + ")";
                log("Office: " + app.Status);
                return app;
            }

            log("Office: instalando " + NOME +
                " em português (pt-BR). Isso costuma levar de 10 a 30 minutos.");
            var relogio = Stopwatch.StartNew();
            int codigo = -1;
            try
            {
                var psi = new ProcessStartInfo(setup, "/configure \"" + xml + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = pasta;
                using (var p = Process.Start(psi))
                {
                    while (!p.WaitForExit(5000))
                    {
                        int min = (int)relogio.Elapsed.TotalMinutes;
                        int pct = Math.Min(95, 3 + min * 92 / 25);   // estimativa: ~25 min
                        progresso(pct, string.Format(
                            "Instalando o Office — {0} min decorridos (estimativa de tempo, sem interação)", min));
                        if (relogio.Elapsed.TotalMinutes > TIMEOUT_MIN)
                        {
                            try { p.Kill(); }
                            catch { }
                            break;
                        }
                    }
                    try { codigo = p.ExitCode; }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                app.Status = "falhou ao executar o instalador (" + ex.Message + ")";
                log("Office: " + app.Status);
                return app;
            }

            // Conferencia real: o codigo de saida do setup.exe nao e confiavel.
            string versao = VersaoInstalada();
            if (versao != null)
            {
                app.Versao = versao;
                app.Status = "instalado";
                log(string.Format("Office: instalado com sucesso (versão {0}, {1} min).",
                    versao, (int)relogio.Elapsed.TotalMinutes));
            }
            else
            {
                app.Status = "falhou (código " + codigo + ", nenhum Office encontrado no registro)";
                log("Office: " + app.Status);
            }
            return app;
        }

        static string MontarXml()
        {
            string bits = Environment.Is64BitOperatingSystem ? "64" : "32";
            var sb = new StringBuilder();
            sb.AppendLine("<Configuration>");
            sb.AppendLine("  <Add OfficeClientEdition=\"" + bits + "\" Channel=\"Current\">");
            sb.AppendLine("    <Product ID=\"" + PRODUTO + "\">");
            sb.AppendLine("      <Language ID=\"pt-br\" />");
            sb.AppendLine("      <ExcludeApp ID=\"Groove\" />");
            sb.AppendLine("      <ExcludeApp ID=\"Lync\" />");
            sb.AppendLine("    </Product>");
            sb.AppendLine("  </Add>");
            sb.AppendLine("  <Display Level=\"None\" AcceptEULA=\"TRUE\" />");
            sb.AppendLine("  <Property Name=\"FORCEAPPSHUTDOWN\" Value=\"TRUE\" />");
            sb.AppendLine("  <RemoveMSI />");
            sb.AppendLine("</Configuration>");
            return sb.ToString();
        }
    }
}
