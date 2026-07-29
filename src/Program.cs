using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoInstall
{
    static class Program
    {
        // Argumentos:
        //   --resume   aberto pela tarefa agendada apos reiniciar (splash curto,
        //              continua da fase salva em ProgramData)
        //   --preview  so mostra as telas com dados de exemplo, nao executa NADA
        //              (para conferir o visual sem mexer no sistema)
        [STAThread]
        static void Main(string[] args)
        {
            bool retomada = false;
            bool preview = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "--resume", StringComparison.OrdinalIgnoreCase)) retomada = true;
                if (string.Equals(a, "--preview", StringComparison.OrdinalIgnoreCase)) preview = true;
            }

            bool novaInstancia;
            using (var mutex = new Mutex(true, "SmellsLikeTechAutoInstall", out novaInstancia))
            {
                if (!novaInstancia) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(retomada, preview));
                GC.KeepAlive(mutex);
            }
        }
    }
}
