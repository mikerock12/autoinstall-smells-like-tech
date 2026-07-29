using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AutoInstall
{
    // Pausar / parar o processo. Como quase tudo aqui e instalacao (Windows
    // Update, winget, Office), NAO da para congelar no meio de um item sem
    // arriscar deixar o sistema num estado quebrado: a pausa e a parada valem
    // nos pontos de checagem entre etapas e entre itens. A tela avisa isso.
    public class ControleExecucao
    {
        readonly ManualResetEvent liberado = new ManualResetEvent(true);
        readonly List<Process> processos = new List<Process>();
        volatile bool pausado;
        volatile bool parando;

        public event Action AoMudar;

        public bool Pausado { get { return pausado; } }
        public bool Parando { get { return parando; } }

        public void Pausar()
        {
            if (parando || pausado) return;
            pausado = true;
            liberado.Reset();
            Notificar();
        }

        public void Continuar()
        {
            if (!pausado) return;
            pausado = false;
            liberado.Set();
            Notificar();
        }

        public void Parar()
        {
            if (parando) return;
            parando = true;
            pausado = false;
            liberado.Set();
            MatarProcessosDescartaveis();
            Notificar();
        }

        // Volta ao estado inicial (usado pelo "Refazer todas as etapas").
        public void Reiniciar()
        {
            parando = false;
            pausado = false;
            liberado.Set();
            lock (processos) processos.Clear();
            Notificar();
        }

        void Notificar()
        {
            var h = AoMudar;
            if (h != null) h();
        }

        // Ponto de checagem das threads de trabalho: segura enquanto pausado e
        // devolve false quando o tecnico mandou parar.
        public bool Prosseguir()
        {
            liberado.WaitOne();
            return !parando;
        }

        // Processos que podem ser encerrados a qualquer momento sem estragar
        // nada (consultas e o vigia da Loja, que so acompanha o andamento).
        // Instaladores em si nunca entram nesta lista.
        public void Registrar(Process p)
        {
            lock (processos)
            {
                processos.Add(p);
                if (parando) EncerrarProcesso(p);
            }
        }

        public void Remover(Process p)
        {
            lock (processos) processos.Remove(p);
        }

        void MatarProcessosDescartaveis()
        {
            lock (processos)
            {
                foreach (Process p in processos) EncerrarProcesso(p);
                processos.Clear();
            }
        }

        static void EncerrarProcesso(Process p)
        {
            try { if (!p.HasExited) p.Kill(); }
            catch { }
        }
    }
}
