using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WUApiLib;

namespace AutoInstall
{
    public class ItemUpdate
    {
        public string Titulo;
        public bool Opcional;
        public bool Driver;
        public bool Interativa;
        public bool Baixado;
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
        public int Baixados { get { return Itens.FindAll(i => i.Baixado).Count; } }
        public int Instalados { get { return Itens.FindAll(i => i.Resultado == "ok" || i.Resultado == "ok com avisos").Count; } }
    }

    public class AtualizadorWindows
    {
        public Action<int, int, int, string> AoProgredirDownload;
        public Action<int, int, int, string> AoProgredirInstalacao;
        public Action<string> AoLogar;
        public Action<string> AoAguardar;
        public bool RebootNecessario;
        IUpdateSession3 sessao;

        void Log(string mensagem) { if (AoLogar != null) AoLogar(mensagem); }


        IUpdateSession3 ObterSessao()
        {
            if (sessao != null) return sessao;
            Log("Inicializando o agente do Windows Update...");
            sessao = (IUpdateSession3)Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
            sessao.ClientApplicationID = "SmellsLikeTech AutoInstall";
            try
            {
                var usm = (IUpdateServiceManager2)Activator.CreateInstance(
                    Type.GetTypeFromProgID("Microsoft.Update.ServiceManager"));
                usm.ClientApplicationID = "SmellsLikeTech AutoInstall";
                // 1 (pending) + 4 (AU): nao faz o registro ONLINE sincrono (2),
                // que podia bloquear antes mesmo de iniciar a busca monitorada.
                usm.AddService2("7971f918-a847-4430-9279-4a52d1efe18d", 5, "");
            }
            catch (Exception ex) { Log("Microsoft Update: " + Erro(ex)); }
            return sessao;
        }

        public static string Erro(Exception ex)
        {
            return ex.Message + string.Format(" (0x{0:X8})", ex.HResult);
        }

        public ResultadoBusca Buscar(ControleExecucao controle)
        {
            if (controle != null && !controle.Prosseguir()) throw new OperationCanceledException();
            IUpdateSearcher buscador = ObterSessao().CreateUpdateSearcher();
            buscador.Online = true;
            Log("Busca assíncrona iniciada (limite: 10 minutos por tentativa).");
            ISearchJob job = buscador.BeginSearch("IsInstalled=0 and IsHidden=0", new CallbackFim(), null);
            ISearchResult resultado;
            try
            {
                Esperar(delegate { return job.IsCompleted; }, null, job.RequestAbort, controle,
                    TimeSpan.FromMinutes(10), "Busca");
                resultado = buscador.EndSearch(job);
            }
            finally { LimparSeConcluido(delegate { return job.IsCompleted; }, job.CleanUp); }

            if (resultado.ResultCode != OperationResultCode.orcSucceeded)
                throw new InvalidOperationException("Busca incompleta: " + resultado.ResultCode +
                    ". O resultado não será tratado como Windows atualizado.");

            var rb = new ResultadoBusca();
            rb.Colecao = NovaColecao();
            var encontrados = resultado.Updates;
            for (int i = 0; i < encontrados.Count; i++)
            {
                if (controle != null && controle.Parando) throw new OperationCanceledException();
                IUpdate u = encontrados[i];
                bool driver = u.Type == UpdateType.utDriver;
                bool opcional = driver;
                var u3 = u as IUpdate3;
                if (u3 != null) { try { if (u3.BrowseOnly) opcional = true; } catch { } }
                bool interativa = false;
                try { interativa = u.InstallationBehavior.CanRequestUserInput; } catch { }
                try { if (!u.EulaAccepted) u.AcceptEula(); }
                catch (Exception ex) { Log("Não foi possível aceitar a licença de " + u.Title + ": " + Erro(ex)); }
                rb.Colecao.Add(u);
                rb.Itens.Add(new ItemUpdate { Titulo = u.Title, Opcional = opcional,
                    Driver = driver, Interativa = interativa });
                if (opcional) rb.Opcionais++;
                if (driver) rb.Drivers++;
                if (interativa) rb.Interativas++;
            }
            return rb;
        }

        public void Baixar(ResultadoBusca busca, ControleExecucao controle)
        {
            if (controle != null && !controle.Prosseguir()) throw new OperationCanceledException();
            IUpdateDownloader dl = ObterSessao().CreateUpdateDownloader();
            dl.Updates = busca.Colecao;
            try { dl.Priority = DownloadPriority.dpHigh; } catch { }
            IDownloadJob job = dl.BeginDownload(new CallbackDownload(), new CallbackFim(), null);
            IDownloadResult resultado;
            try
            {
                Esperar(delegate { return job.IsCompleted; }, delegate
                {
                    IDownloadProgress p = job.GetProgress();
                    int idx = p.CurrentUpdateIndex;
                    if (AoProgredirDownload != null)
                        AoProgredirDownload(p.PercentComplete, idx + 1, p.CurrentUpdatePercentComplete,
                            idx >= 0 && idx < busca.Total ? busca.Itens[idx].Titulo : "");
                    // Bytes contam como atividade mesmo quando a porcentagem nao muda.
                    return p.TotalBytesDownloaded + ":" + idx + ":" + p.CurrentUpdateDownloadPhase;
                }, job.RequestAbort, controle, TimeSpan.FromMinutes(15), "Download");
                resultado = dl.EndDownload(job);
            }
            finally { LimparSeConcluido(delegate { return job.IsCompleted; }, job.CleanUp); }

            Log("Resultado do download: " + resultado.ResultCode + string.Format(" (0x{0:X8}).", resultado.HResult));
            for (int i = 0; i < busca.Total; i++)
            {
                var r = resultado.GetUpdateResult(i);
                var item = busca.Itens[i];
                item.Baixado = busca.Colecao[i].IsDownloaded;
                item.Resultado = item.Baixado ? null : string.Format(
                    "download falhou: {0} (0x{1:X8})", r.ResultCode, r.HResult);
                if (!item.Baixado) Log(item.Titulo + " — " + item.Resultado);
            }
        }

        public void Instalar(ResultadoBusca busca, ControleExecucao controle)
        {
            IUpdateSession3 s = ObterSessao();
            int total = busca.Baixados;
            RebootNecessario = false;
            var lote = new List<int>();
            var exclusivas = new List<int>();
            for (int i = 0; i < busca.Total; i++)
            {
                if (!busca.Itens[i].Baixado) continue;
                if ((int)busca.Colecao[i].InstallationBehavior.Impact == 2) exclusivas.Add(i);
                else lote.Add(i);
            }
            int concluidas = 0;
            if (lote.Count > 0)
            {
                InstalarParte(s, busca, lote, concluidas, total, controle);
                concluidas += lote.Count;
            }
            foreach (int i in exclusivas)
            {
                if (RebootNecessario || (controle != null && !controle.Prosseguir())) break;
                InstalarParte(s, busca, new List<int> { i }, concluidas, total, controle);
                concluidas++;
            }
            foreach (var item in busca.Itens)
                if (item.Resultado == null) item.Resultado = "pendente (instalação não iniciada)";
        }

        void InstalarParte(IUpdateSession3 s, ResultadoBusca busca, List<int> indices,
            int jaConcluidas, int total, ControleExecucao controle)
        {
            if (controle != null && !controle.Prosseguir()) return;
            try
            {
                var col = NovaColecao();
                foreach (int i in indices) col.Add(busca.Colecao[i]);
                IUpdateInstaller inst = s.CreateUpdateInstaller();
                if (inst.IsBusy) throw new WindowsUpdateEmAndamentoException(
                    "Outra instalação do Windows Update está em andamento. Aguarde sua conclusão antes de retomar.");
                if (inst.RebootRequiredBeforeInstallation) { RebootNecessario = true; return; }
                inst.Updates = col;
                inst.AllowSourcePrompts = false;
                var inst2 = inst as IUpdateInstaller2;
                if (inst2 != null) inst2.ForceQuiet = true;
                IInstallationJob job = inst.BeginInstall(new CallbackInstalacao(), new CallbackFim(), null);
                IInstallationResult resultado;
                try
                {
                    Esperar(delegate { return job.IsCompleted; }, delegate
                    {
                        IInstallationProgress p = job.GetProgress();
                        int k = p.CurrentUpdateIndex;
                        NotificarProgressoInstalacao(busca, indices, jaConcluidas, total,
                            k, p.PercentComplete, p.CurrentUpdatePercentComplete, AoProgredirInstalacao);
                        return k + ":" + p.PercentComplete + ":" + p.CurrentUpdatePercentComplete;
                    }, null, null, TimeSpan.Zero, "Instalação"); // Nunca aborta uma instalacao ativa.
                    resultado = inst.EndInstall(job);
                }
                finally { LimparSeConcluido(delegate { return job.IsCompleted; }, job.CleanUp); }

                for (int k = 0; k < indices.Count; k++)
                {
                    var r = resultado.GetUpdateResult(k);
                    busca.Itens[indices[k]].Resultado = r.ResultCode == OperationResultCode.orcSucceeded ? "ok" :
                        r.ResultCode == OperationResultCode.orcSucceededWithErrors ? "ok com avisos" :
                        string.Format("instalação falhou: {0} (0x{1:X8})", r.ResultCode, r.HResult);
                    Log(busca.Itens[indices[k]].Titulo + " — " + busca.Itens[indices[k]].Resultado);
                }
                if (resultado.RebootRequired) RebootNecessario = true;
            }
            catch (WindowsUpdateEmAndamentoException) { throw; }
            catch (Exception ex)
            {
                foreach (int i in indices)
                    if (busca.Itens[i].Resultado == null) busca.Itens[i].Resultado = "falhou: " + Erro(ex);
                Log("Falha na instalação: " + Erro(ex));
            }
        }

        internal static void NotificarProgressoInstalacao(ResultadoBusca busca, List<int> indices,
            int jaConcluidas, int total, int indiceParte, int percentualParte, int percentualItem,
            Action<int, int, int, string> progresso)
        {
            if (progresso == null || indices.Count == 0 || total <= 0) return;
            int k = Math.Max(0, Math.Min(indiceParte, indices.Count - 1));
            int original = indices[k];
            int geral = (jaConcluidas * 100 + percentualParte * indices.Count) / total;
            // A posicao na fila baixada e diferente do indice na busca original:
            // itens sem download e exclusivas podem deixar lacunas na busca.
            progresso(Math.Max(0, Math.Min(geral, 100)), jaConcluidas + k + 1, percentualItem,
                busca.Itens[original].Titulo);
        }

        void Esperar(Func<bool> concluido, Func<string> progresso, Action abortar,
            ControleExecucao controle, TimeSpan limite, string operacao)
        {
            try { EsperaWindowsUpdate.Aguardar(concluido, progresso, abortar, controle, limite,
                AoAguardar, Log, operacao); }
            catch (TimeoutException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (WindowsUpdateEmAndamentoException) { throw; }
            catch (Exception ex)
            {
                // Uma falha de consulta nao prova que o instalador terminou.
                throw new WindowsUpdateEmAndamentoException("Perdi a comunicação com o Windows Update: " + Erro(ex) +
                    ". As próximas etapas foram suspensas; o estado foi preservado.");
            }
        }

        static UpdateCollection NovaColecao()
        {
            return (UpdateCollection)Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));
        }

        static void LimparSeConcluido(Func<bool> concluido, Action limpar)
        {
            // CleanUp tambem espera: chama-lo em job ativo recriaria o travamento.
            try { if (concluido()) limpar(); } catch { }
        }

        // Callbacks publicos e visiveis ao COM apesar do ComVisible(false) do
        // assembly. Nao acessam a UI nem seguram referencias ao job. O polling
        // de IsCompleted/GetProgress funciona mesmo se uma notificacao se perder.
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public class CallbackFim : ISearchCompletedCallback, IDownloadCompletedCallback, IInstallationCompletedCallback
        {
            public void Invoke(ISearchJob job, ISearchCompletedCallbackArgs e) { }
            public void Invoke(IDownloadJob job, IDownloadCompletedCallbackArgs e) { }
            public void Invoke(IInstallationJob job, IInstallationCompletedCallbackArgs e) { }
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public class CallbackDownload : IDownloadProgressChangedCallback
        {
            public void Invoke(IDownloadJob job, IDownloadProgressChangedCallbackArgs e) { }
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public class CallbackInstalacao : IInstallationProgressChangedCallback
        {
            public void Invoke(IInstallationJob job, IInstallationProgressChangedCallbackArgs e) { }
        }
    }
}
