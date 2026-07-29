using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoInstall
{
    // Retomada automatica apos reiniciar: tarefa agendada ONLOGON com privilegios
    // elevados (nao dispara prompt do UAC). Se o schtasks falhar, cai para a
    // chave RunOnce (que funciona, mas mostra o prompt do UAC no logon).
    public static class TarefaInicio
    {
        const string NOME_TAREFA = "SmellsLikeTech AutoInstall";
        const string NOME_RUNONCE = "SmellsLikeTechAutoInstall";
        const string CHAVE_RUNONCE = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

        public static void Criar(Action<string> log)
        {
            string exe = Application.ExecutablePath;
            var r = Executor.Rodar("schtasks.exe",
                "/Create /F /TN \"" + NOME_TAREFA + "\" /SC ONLOGON /RL HIGHEST /DELAY 0000:20" +
                " /TR \"\\\"" + exe + "\\\" --resume\"");
            if (r.Ok)
            {
                log("Retomada automática configurada: o programa reabre sozinho após reiniciar.");
                return;
            }

            try
            {
                using (var k = Registry.LocalMachine.CreateSubKey(CHAVE_RUNONCE))
                    k.SetValue(NOME_RUNONCE, "\"" + exe + "\" --resume");
                log("Retomada automática configurada (RunOnce).");
            }
            catch (Exception ex)
            {
                log("AVISO: não consegui configurar a retomada automática: " + ex.Message);
                log("Depois de reiniciar, abra o programa manualmente para continuar.");
            }
        }

        public static void Remover()
        {
            Executor.Rodar("schtasks.exe", "/Delete /F /TN \"" + NOME_TAREFA + "\"");
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(CHAVE_RUNONCE, true))
                    if (k != null) k.DeleteValue(NOME_RUNONCE, false);
            }
            catch { }
        }
    }
}
