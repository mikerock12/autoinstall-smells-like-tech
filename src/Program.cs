using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoInstall
{
    static class Program
    {
        // Argumentos:
        //   --resume   aberto pela tarefa agendada apos reiniciar (abertura
        //              curta, continua da fase salva em ProgramData)
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

                // A abertura e uma janela a parte, sem moldura: so o Guaxinim
                // recortado e o texto, sobre a area de trabalho. So depois dela
                // e que a janela do programa aparece e o processo comeca.
                if (retomada) Application.Run(new SplashGuaxinim(800, 400, 700));
                else Application.Run(new SplashGuaxinim(5000, 1200, 5000));

                Application.Run(new MainForm(retomada, preview));
                GC.KeepAlive(mutex);
            }
        }
    }
}
