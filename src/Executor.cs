using System;
using System.Diagnostics;
using System.Text;

namespace AutoInstall
{
    public class ResultadoExec
    {
        public int Codigo;
        public string Saida;
        public string Erro;
        public bool Ok { get { return Codigo == 0; } }
    }

    // Executa programas direto (powercfg, winget, schtasks...), sem passar por
    // cmd.exe e sem abrir janela, capturando toda a saida.
    public static class Executor
    {
        public static ResultadoExec Rodar(string exe, string args)
        {
            return Rodar(exe, args, null, null);
        }

        public static ResultadoExec Rodar(string exe, string args, Encoding enc, Action<string> aoReceberLinha)
        {
            return Rodar(exe, args, enc, aoReceberLinha, null);
        }

        // "controle" so deve ser passado para processos que podem ser
        // encerrados a qualquer momento sem estragar nada (consultas, vigias).
        // Instaladores rodam sem ele: sao sempre concluidos.
        public static ResultadoExec Rodar(string exe, string args, Encoding enc,
            Action<string> aoReceberLinha, ControleExecucao controle)
        {
            var r = new ResultadoExec();
            var sbSaida = new StringBuilder();
            var sbErro = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                if (enc != null)
                {
                    psi.StandardOutputEncoding = enc;
                    psi.StandardErrorEncoding = enc;
                }
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        sbSaida.AppendLine(e.Data);
                        if (aoReceberLinha != null)
                        {
                            try { aoReceberLinha(e.Data); }
                            catch { }
                        }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) sbErro.AppendLine(e.Data);
                    };
                    p.Start();
                    if (controle != null) controle.Registrar(p);
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    if (controle != null) controle.Remover(p);
                    r.Codigo = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                r.Codigo = -1;
                sbErro.AppendLine(ex.Message);
            }
            r.Saida = sbSaida.ToString();
            r.Erro = sbErro.ToString();
            return r;
        }
    }
}
