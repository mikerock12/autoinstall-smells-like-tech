using System;
using System.Diagnostics;
using System.Threading;

namespace AutoInstall
{
    // Impede uma nova operacao ou reboot quando o agente nao confirmou o fim.
    public class WindowsUpdateEmAndamentoException : Exception
    {
        public WindowsUpdateEmAndamentoException(string mensagem) : base(mensagem) { }
    }

    public static class EsperaWindowsUpdate
    {
        public static void Aguardar(Func<bool> concluido, Func<string> progresso,
            Action abortar, ControleExecucao controle, TimeSpan limiteSemProgresso,
            Action<string> status)
        {
            var relogio = Stopwatch.StartNew();
            Aguardar(concluido, progresso, abortar, controle, limiteSemProgresso,
                TimeSpan.FromSeconds(60), status, delegate { return relogio.Elapsed; },
                delegate { Thread.Sleep(1000); });
        }

        // Relogio e espera injetados para testar timeouts sem esperar minutos.
        internal static void Aguardar(Func<bool> concluido, Func<string> progresso,
            Action abortar, ControleExecucao controle, TimeSpan limiteSemProgresso,
            TimeSpan prazoAborto, Action<string> status, Func<TimeSpan> agora, Action esperar)
        {
            TimeSpan inicio = agora(), ultimaMudanca = inicio, ultimoAviso = inicio;
            TimeSpan pediuAbortoEm = inicio;
            string anterior = null;
            bool cancelando = false, porParada = false;
            while (true)
            {
                // A propriedade e a fonte de verdade, mesmo sem callback COM.
                if (concluido())
                {
                    if (!cancelando) return;
                    if (porParada) throw new OperationCanceledException("Windows Update interrompido pelo técnico.");
                    throw new TimeoutException("Windows Update excedeu o tempo sem progresso; operação cancelada com segurança.");
                }

                TimeSpan tempo = agora();
                if (cancelando)
                {
                    if (tempo - pediuAbortoEm >= prazoAborto)
                        throw new WindowsUpdateEmAndamentoException(
                            "O Windows Update não confirmou o cancelamento em 60 segundos. " +
                            "As próximas etapas foram suspensas para evitar operações simultâneas.");
                }
                else
                {
                    string atual = progresso == null ? "" : progresso();
                    if (atual != anterior) { anterior = atual; ultimaMudanca = tempo; }
                    porParada = controle != null && controle.Parando;
                    bool expirou = limiteSemProgresso > TimeSpan.Zero &&
                        tempo - ultimaMudanca >= limiteSemProgresso;
                    if (abortar != null && (porParada || expirou))
                    {
                        cancelando = true;
                        pediuAbortoEm = tempo;
                        if (status != null) status(porParada
                            ? "Cancelando a operação a pedido do técnico..."
                            : "Sem progresso dentro do limite; cancelando antes de tentar novamente...");
                        abortar();
                    }
                }

                if (tempo - ultimoAviso >= TimeSpan.FromSeconds(30))
                {
                    ultimoAviso = tempo;
                    if (status != null) status(string.Format(
                        "Windows Update: {0:hh\\:mm\\:ss} decorridos · {1:hh\\:mm\\:ss} sem mudança de progresso.",
                        tempo - inicio, tempo - ultimaMudanca));
                }
                esperar();
            }
        }
    }
}
