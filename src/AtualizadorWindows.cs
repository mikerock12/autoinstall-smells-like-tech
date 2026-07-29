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
        public string Resultado;
    }

    public class ResultadoBusca
    {
        public UpdateCollection Colecao;
        public List<ItemUpdate> Itens = new List<ItemUpdate>();
        public int Opcionais;
        public int Drivers;
        public int Ignorados;   // exigem interacao do usuario -> pulados
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

                bool interativo = false;
                try { interativo = u.InstallationBehavior.CanRequestUserInput; } catch { }
                if (interativo) { rb.Ignorados++; continue; }

                try { if (!u.EulaAccepted) u.AcceptEula(); } catch { }

                rb.Colecao.Add(u);
                var item = new ItemUpdate();
                item.Titulo = u.Title;
                item.Opcional = opcional;
                item.Driver = driver;
                rb.Itens.Add(item);
                if (opcional) rb.Opcionais++;
                if (driver) rb.Drivers++;
            }
            return rb;
        }

        public void Baixar(ResultadoBusca busca)
        {
            IUpdateSession3 s = ObterSessao();
            IUpdateDownloader dl = s.CreateUpdateDownloader();
            dl.Updates = busca.Colecao;
            try { dl.Priority = (DownloadPriority)3; } catch { }   // dpHigh
            using (var fim = new ManualResetEvent(false))
            {
                IDownloadJob job = dl.BeginDownload(
                    new CbDownloadProgresso(this, busca), new CbDownloadFim(fim), null);
                fim.WaitOne();
                dl.EndDownload(job);
            }
        }

        public void Instalar(ResultadoBusca busca)
        {
            IUpdateSession3 s = ObterSessao();
            IUpdateInstaller inst = s.CreateUpdateInstaller();
            inst.Updates = busca.Colecao;
            IInstallationResult resultado;
            using (var fim = new ManualResetEvent(false))
            {
                IInstallationJob job = inst.BeginInstall(
                    new CbInstalacaoProgresso(this, busca), new CbInstalacaoFim(fim), null);
                fim.WaitOne();
                resultado = inst.EndInstall(job);
            }

            for (int i = 0; i < busca.Itens.Count; i++)
            {
                try
                {
                    var r = resultado.GetUpdateResult(i);
                    int cod = (int)r.ResultCode;                   // 2 = ok, 3 = ok com avisos
                    if (cod == 2) busca.Itens[i].Resultado = "ok";
                    else if (cod == 3) busca.Itens[i].Resultado = "ok com avisos";
                    else busca.Itens[i].Resultado = "código " + cod;
                }
                catch { busca.Itens[i].Resultado = "?"; }
            }

            RebootNecessario = false;
            try { RebootNecessario = resultado.RebootRequired; } catch { }
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

        class CbInstalacaoProgresso : IInstallationProgressChangedCallback
        {
            readonly AtualizadorWindows dono;
            readonly ResultadoBusca busca;
            public CbInstalacaoProgresso(AtualizadorWindows dono, ResultadoBusca busca)
            {
                this.dono = dono; this.busca = busca;
            }
            public void Invoke(IInstallationJob job, IInstallationProgressChangedCallbackArgs e)
            {
                try
                {
                    IInstallationProgress p = e.Progress;
                    int idx = p.CurrentUpdateIndex;
                    string titulo = (idx >= 0 && idx < busca.Itens.Count) ? busca.Itens[idx].Titulo : "";
                    var h = dono.AoProgredirInstalacao;
                    if (h != null) h(p.PercentComplete, idx + 1, p.CurrentUpdatePercentComplete, titulo);
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
