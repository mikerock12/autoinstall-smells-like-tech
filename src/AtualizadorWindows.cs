using System;
using System.Collections.Generic;
using System.Threading;
using WUApiLib;

namespace AutoInstall
{
    public class ItemUpdate
    {
        public string Titulo;
        public bool Opcional;
        public bool Driver;
        public bool Interativa;   // declara que PODE pedir interacao
        public string Resultado;
    }

    public class ResultadoBusca
    {
        public UpdateCollection Colecao;
        public List<ItemUpdate> Itens = new List<ItemUpdate>();
        public int Opcionais;
        public int Drivers;
        public int Interativas;
        public int Total { get { return Itens.Count; } }
    }

    // Windows Update via COM (wuapi.dll), com progresso real de download e
    // instalacao atraves dos callbacks BeginDownload/BeginInstall.
    public class AtualizadorWindows
    {
        // (percentualGeral, indiceAtual 1-based, percentualDoItem, tituloDoItem)
        public Action<int, int, int, string> AoProgredirDownload;
        public Action<int, int, int, string> AoProgredirInstalacao;

        public bool RebootNecessario;

        IUpdateSession3 sessao;

        IUpdateSession3 ObterSessao()
        {
            if (sessao == null)
            {
                sessao = (IUpdateSession3)Activator.CreateInstance(
                    Type.GetTypeFromProgID("Microsoft.Update.Session"));
                sessao.ClientApplicationID = "SmellsLikeTech AutoInstall";

                // Opt-in no Microsoft Update (alem do Windows Update, traz
                // atualizacoes do Office e de outros produtos Microsoft).
                try
                {
                    var usm = (IUpdateServiceManager2)Activator.CreateInstance(
                        Type.GetTypeFromProgID("Microsoft.Update.ServiceManager"));
                    usm.ClientApplicationID = "SmellsLikeTech AutoInstall";
                    usm.AddService2("7971f918-a847-4430-9279-4a52d1efe18d", 7, "");
                }
                catch { }
            }
            return sessao;
        }

        // Procura tudo que esta pendente, incluindo opcionais e drivers.
        public ResultadoBusca Buscar()
        {
            IUpdateSession3 s = ObterSessao();
            IUpdateSearcher buscador = s.CreateUpdateSearcher();
            buscador.Online = true;
            ISearchResult resultado = buscador.Search("IsInstalled=0 and IsHidden=0");

            var rb = new ResultadoBusca();
            rb.Colecao = (UpdateCollection)Activator.CreateInstance(
                Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));

            var encontrados = resultado.Updates;
            for (int i = 0; i < encontrados.Count; i++)
            {
                IUpdate u = encontrados[i];

                bool driver = false;
                try { driver = ((int)u.Type == 2); } catch { }    // 2 = utDriver

                bool opcional = driver;
                var u3 = u as IUpdate3;
                if (u3 != null)
                {
                    try { if (u3.BrowseOnly) opcional = true; }
                    catch { }
                }

                // CanRequestUserInput NAO e motivo para pular: drivers quase
                // sempre se declaram assim e instalam silenciosamente numa
                // boa. A instalacao roda com ForceQuiet, que suprime qualquer
                // pedido de interacao; se alguma realmente nao puder instalar
                // sem usuario, falha sozinha e fica registrada no relatorio.
                bool interativa = false;
                try { interativa = u.InstallationBehavior.CanRequestUserInput; } catch { }

                try { if (!u.EulaAccepted) u.AcceptEula(); } catch { }

                rb.Colecao.Add(u);
                var item = new ItemUpdate();
                item.Titulo = u.Title;
                item.Opcional = opcional;
                item.Driver = driver;
                item.Interativa = interativa;
                rb.Itens.Add(item);
                if (opcional) rb.Opcionais++;
                if (driver) rb.Drivers++;
                if (interativa) rb.Interativas++;
            }
            return rb;
        }

        // Download pode ser abortado com seguranca (o que ja veio fica em cache
        // e o proximo ciclo aproveita). A INSTALACAO nunca e abortada: parar no
        // meio de uma atualizacao e a receita para um Windows quebrado.
        public void Baixar(ResultadoBusca busca, ControleExecucao controle)
        {
            IUpdateSession3 s = ObterSessao();
            IUpdateDownloader dl = s.CreateUpdateDownloader();
            dl.Updates = busca.Colecao;
            try { dl.Priority = (DownloadPriority)3; } catch { }   // dpHigh
            using (var fim = new ManualResetEvent(false))
            {
                IDownloadJob job = dl.BeginDownload(
                    new CbDownloadProgresso(this, busca), new CbDownloadFim(fim), null);
                while (!fim.WaitOne(500))
                {
                    if (controle == null || !controle.Parando) continue;
                    try { job.RequestAbort(); }
                    catch { }
                    fim.WaitOne(20000);
                    break;
                }
                try { dl.EndDownload(job); }
                catch { }
            }
        }

        public void Instalar(ResultadoBusca busca)
        {
            IUpdateSession3 s = ObterSessao();
            int total = busca.Itens.Count;
            RebootNecessario = false;

            // Atualizacoes com Impact "exclusivo" (ex.: upgrades de versao) so
            // podem ser instaladas sozinhas: misturadas ao lote, derrubam a
            // operacao inteira (0x8024000B). Separa: lote normal primeiro,
            // depois cada exclusiva por vez.
            var lote = new List<int>();
            var exclusivas = new List<int>();
            for (int i = 0; i < total; i++)
            {
                bool exclusiva = false;
                try { exclusiva = ((int)busca.Colecao[i].InstallationBehavior.Impact == 2); }
                catch { }
                if (exclusiva) exclusivas.Add(i);
                else lote.Add(i);
            }

            int concluidas = 0;
            if (lote.Count > 0)
            {
                InstalarParte(s, busca, lote, concluidas, total);
                concluidas += lote.Count;
            }
            foreach (int i in exclusivas)
            {
                var sozinha = new List<int>();
                sozinha.Add(i);
                InstalarParte(s, busca, sozinha, concluidas, total);
                concluidas++;
            }
        }

        void InstalarParte(IUpdateSession3 s, ResultadoBusca busca, List<int> indices,
            int jaConcluidas, int total)
        {
            try
            {
                var col = (UpdateCollection)Activator.CreateInstance(
                    Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));
                foreach (int i in indices) col.Add(busca.Colecao[i]);

                IUpdateInstaller inst = s.CreateUpdateInstaller();
                inst.Updates = col;
                try { inst.AllowSourcePrompts = false; } catch { }
                // ForceQuiet: instala sem nenhuma interacao, inclusive as
                // atualizacoes que declaram CanRequestUserInput (drivers).
                var inst2 = inst as IUpdateInstaller2;
                if (inst2 != null)
                {
                    try { inst2.ForceQuiet = true; } catch { }
                }

                IInstallationResult resultado;
                using (var fim = new ManualResetEvent(false))
                {
                    IInstallationJob job = inst.BeginInstall(
                        new CbInstalacaoProgresso(this, busca, indices, jaConcluidas, total),
                        new CbInstalacaoFim(fim), null);
                    fim.WaitOne();
                    resultado = inst.EndInstall(job);
                }

                for (int k = 0; k < indices.Count; k++)
                {
                    try
                    {
                        var r = resultado.GetUpdateResult(k);
                        int cod = (int)r.ResultCode;               // 2 = ok, 3 = ok com avisos
                        if (cod == 2) busca.Itens[indices[k]].Resultado = "ok";
                        else if (cod == 3) busca.Itens[indices[k]].Resultado = "ok com avisos";
                        else busca.Itens[indices[k]].Resultado = "código " + cod;
                    }
                    catch { busca.Itens[indices[k]].Resultado = "?"; }
                }

                try { if (resultado.RebootRequired) RebootNecessario = true; } catch { }
            }
            catch (Exception ex)
            {
                // Falha desta parte nao derruba as demais; fica no relatorio
                // e a proxima rodada (pos-reinicio) tenta de novo.
                foreach (int i in indices)
                    if (busca.Itens[i].Resultado == null)
                        busca.Itens[i].Resultado = "falhou (" + ex.Message + ")";
            }
        }

        // ---- Callbacks COM (chamados pelo agente do Windows Update) ----

        class CbDownloadProgresso : IDownloadProgressChangedCallback
        {
            readonly AtualizadorWindows dono;
            readonly ResultadoBusca busca;
            public CbDownloadProgresso(AtualizadorWindows dono, ResultadoBusca busca)
            {
                this.dono = dono; this.busca = busca;
            }
            public void Invoke(IDownloadJob job, IDownloadProgressChangedCallbackArgs e)
            {
                try
                {
                    IDownloadProgress p = e.Progress;
                    int idx = p.CurrentUpdateIndex;
                    string titulo = (idx >= 0 && idx < busca.Itens.Count) ? busca.Itens[idx].Titulo : "";
                    var h = dono.AoProgredirDownload;
                    if (h != null) h(p.PercentComplete, idx + 1, p.CurrentUpdatePercentComplete, titulo);
                }
                catch { }
            }
        }

        class CbDownloadFim : IDownloadCompletedCallback
        {
            readonly ManualResetEvent ev;
            public CbDownloadFim(ManualResetEvent ev) { this.ev = ev; }
            public void Invoke(IDownloadJob job, IDownloadCompletedCallbackArgs e)
            {
                try { ev.Set(); } catch { }
            }
        }

        // A instalacao pode rodar em partes (lote normal + exclusivas): o
        // callback recebe o mapa de indices da parte atual e o quanto ja foi
        // concluido antes dela, para o percentual geral cobrir o conjunto todo.
        class CbInstalacaoProgresso : IInstallationProgressChangedCallback
        {
            readonly AtualizadorWindows dono;
            readonly ResultadoBusca busca;
            readonly List<int> indices;
            readonly int jaConcluidas;
            readonly int total;
            public CbInstalacaoProgresso(AtualizadorWindows dono, ResultadoBusca busca,
                List<int> indices, int jaConcluidas, int total)
            {
                this.dono = dono;
                this.busca = busca;
                this.indices = indices;
                this.jaConcluidas = jaConcluidas;
                this.total = total;
            }
            public void Invoke(IInstallationJob job, IInstallationProgressChangedCallbackArgs e)
            {
                try
                {
                    IInstallationProgress p = e.Progress;
                    int k = p.CurrentUpdateIndex;
                    int original = (k >= 0 && k < indices.Count) ? indices[k] : 0;
                    string titulo = (original < busca.Itens.Count) ? busca.Itens[original].Titulo : "";
                    int geral = total > 0
                        ? (jaConcluidas * 100 + p.PercentComplete * indices.Count) / total
                        : p.PercentComplete;
                    var h = dono.AoProgredirInstalacao;
                    if (h != null) h(geral, original + 1, p.CurrentUpdatePercentComplete, titulo);
                }
                catch { }
            }
        }

        class CbInstalacaoFim : IInstallationCompletedCallback
        {
            readonly ManualResetEvent ev;
            public CbInstalacaoFim(ManualResetEvent ev) { this.ev = ev; }
            public void Invoke(IInstallationJob job, IInstallationCompletedCallbackArgs e)
            {
                try { ev.Set(); } catch { }
            }
        }
    }
}
