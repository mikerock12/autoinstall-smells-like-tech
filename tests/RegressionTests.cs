using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AutoInstall;
using WUApiLib;

class RegressionTests
{
    // Host fora da area visivel para renderizar os controles nativos de WinForms
    // com handles reais, sem ativar uma janela sobre o trabalho do usuario.
    class JanelaTeste : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
    }
    static int verificacoes;
    static void Conferir(bool condicao, string descricao)
    {
        if (!condicao) throw new Exception(descricao);
        verificacoes++;
    }
    static void EsperarExcecao<T>(Action acao) where T : Exception
    {
        try { acao(); } catch (T) { verificacoes++; return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
    class Cenario
    {
        public int Segundos, Abortos, Avisos;
        public readonly List<string> MensagensStatus = new List<string>();
        public readonly List<string> MensagensLog = new List<string>();
        public string Operacao = "Operação";
        public int Fim = 3;
        public int Limite = 10;
        public int Graca = 4;
        public bool ConfirmaAborto = true;
        public bool PodeAbortar = true;
        public ControleExecucao Controle = new ControleExecucao();
        public Func<string> Progresso;
        public Action AoEsperar;
        public void Rodar()
        {
            EsperaWindowsUpdate.Aguardar(() => Segundos >= Fim, Progresso,
                PodeAbortar ? (Action)delegate { Abortos++; if (ConfirmaAborto) Fim = Segundos + 2; } : null,
                Controle, TimeSpan.FromSeconds(Limite), TimeSpan.FromSeconds(Graca),
                delegate(string mensagem) { Avisos++; MensagensStatus.Add(mensagem); }, () => TimeSpan.FromSeconds(Segundos),
                delegate { Segundos++; if (AoEsperar != null) AoEsperar();
                    if (Segundos > 10000) throw new Exception("Espera sem limite no teste"); }, MensagensLog.Add, Operacao);
        }
    }
    static void TestarEspera()
    {
        var semCallback = new Cenario();
        semCallback.Rodar();
        Conferir(semCallback.Segundos == 3 && semCallback.Abortos == 0,
            "Conclusao deve funcionar sem qualquer callback");

        var buscaTravada = new Cenario { Fim = int.MaxValue };
        EsperarExcecao<TimeoutException>(buscaTravada.Rodar);
        Conferir(buscaTravada.Abortos == 1 && buscaTravada.Segundos == 12,
            "Timeout so libera nova tentativa depois de confirmar o aborto");

        var semConfirmacao = new Cenario { Fim = int.MaxValue, ConfirmaAborto = false };
        EsperarExcecao<WindowsUpdateEmAndamentoException>(semConfirmacao.Rodar);
        Conferir(semConfirmacao.Abortos == 1 && semConfirmacao.Segundos == 14,
            "Cancelamento sem resposta deve bloquear sobreposicao de operacoes");

        var parada = new Cenario { Fim = 100 };
        parada.AoEsperar = delegate { if (parada.Segundos == 2) parada.Controle.Parar(); };
        EsperarExcecao<OperationCanceledException>(parada.Rodar);
        Conferir(parada.Segundos == 4 && parada.Abortos == 1, "Parar deve cancelar a busca em andamento");

        var baixando = new Cenario { Fim = 40 };
        baixando.Progresso = () => (baixando.Segundos / 3).ToString();
        baixando.Rodar();
        Conferir(baixando.Abortos == 0 && baixando.Segundos > baixando.Limite,
            "Bytes avancando devem renovar o prazo mesmo sem mudar a porcentagem");

        var parouDownload = new Cenario { Fim = 100 };
        parouDownload.Progresso = () => Math.Min(parouDownload.Segundos, 20).ToString();
        EsperarExcecao<TimeoutException>(parouDownload.Rodar);
        Conferir(parouDownload.Segundos == 32, "Prazo deve contar da ultima mudanca, nao do inicio");

        var instalando = new Cenario { Fim = 3700, PodeAbortar = false, Limite = 0 };
        instalando.Controle.Parar();
        instalando.Rodar();
        Conferir(instalando.Abortos == 0 && instalando.Segundos == 3700 && instalando.Avisos > 0,
            "Instalacao lenta deve aguardar com heartbeat e nunca ser abortada");

        // Reproduz a imagem: instalacao longa, percentual parado por minutos,
        // depois avancando. A espera nao deve ser apresentada como erro.
        var imagem = new Cenario { Fim = 670, PodeAbortar = false, Limite = 0, Operacao = "Instalação" };
        imagem.Progresso = () => imagem.Segundos < 570 ? "item:62:100" : "item:80:100";
        imagem.Rodar();
        Conferir(imagem.MensagensStatus.Count > 10 && imagem.MensagensStatus.All(m =>
            m.StartsWith("Instalação em andamento") && !m.Contains("sem mudança") && !m.Contains("falhou")),
            "Espera normal deve informar instalacao em andamento mesmo com percentual parado");
        Conferir(imagem.MensagensLog.Count == 0, "Heartbeats de instalacao nao devem inundar o log");

        var cancelamentoLento = new Cenario { Fim = int.MaxValue, Limite = 30, Graca = 61, ConfirmaAborto = false };
        EsperarExcecao<WindowsUpdateEmAndamentoException>(cancelamentoLento.Rodar);
        Conferir(cancelamentoLento.MensagensLog.Count == 1 && cancelamentoLento.MensagensLog[0].Contains("cancelando"),
            "Timeout verdadeiro deve continuar registrado no log");
        Conferir(cancelamentoLento.MensagensStatus.Last().Contains("cancelando") &&
            cancelamentoLento.MensagensStatus.All(m => !m.Contains("em andamento")),
            "Heartbeat nao deve sobrescrever o aviso de cancelamento pendente");

        foreach (Type tipo in new[] { typeof(AtualizadorWindows.CallbackFim),
            typeof(AtualizadorWindows.CallbackDownload), typeof(AtualizadorWindows.CallbackInstalacao) })
            Conferir(Marshal.IsTypeVisibleFromCom(tipo), "Callback deve estar visivel para COM: " + tipo.Name);
    }
    static void TestarProgressoInstalacao()
    {
        var busca = new ResultadoBusca();
        for (int i = 0; i < 35; i++) busca.Itens.Add(new ItemUpdate { Titulo = "Atualização " + i });
        var indices = Enumerable.Range(0, 20).Concat(Enumerable.Range(30, 5)).ToList();
        int geral = -1, posicao = -1, pctItem = -1;
        string titulo = null;
        Action<int, int, int, string> receber = delegate(int pct, int idx, int item, string nome)
        { geral = pct; posicao = idx; pctItem = item; titulo = nome; };
        AtualizadorWindows.NotificarProgressoInstalacao(busca, indices, 0, 25, 21, 62, 100, receber);
        Conferir(geral == 62 && posicao == 22 && titulo == "Atualização 31" && pctItem == 100,
            "35 encontrados e 25 baixados: posicao na fila baixada deve corresponder ao titulo original");
        AtualizadorWindows.NotificarProgressoInstalacao(busca, new List<int> { 34 }, 24, 25, 0, 100, 100, receber);
        Conferir(geral == 100 && posicao == 25 && titulo == "Atualização 34",
            "Ultima exclusiva deve fechar em 25 de 25 e 100 por cento");
        AtualizadorWindows.NotificarProgressoInstalacao(busca, indices, 0, 25, 25, 100, 100, receber);
        Conferir(posicao == 25 && geral == 100, "Indice final do agente nao pode exceder o total da fila");
    }
    static T Campo<T>(object objeto, string nome)
    {
        return (T)objeto.GetType().GetField(nome, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(objeto);
    }
    static void TestarCatalogoETela()
    {
        Conferir(Catalogo.Apps.Select(a => a.Chave).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Catalogo.Apps.Length,
            "Chaves de aplicativos devem ser unicas");
        Conferir(Catalogo.Categorias.Distinct().Count() == Catalogo.Categorias.Length, "Categorias duplicadas");
        Conferir(Catalogo.Apps.All(a => Catalogo.Categorias.Contains(a.Categoria) && a.Metodos.Length > 0),
            "Todos os aplicativos precisam de categoria e metodo");
        Conferir(Catalogo.Categorias.All(c => Catalogo.Da(c).Count > 0), "Categorias vazias");
        Conferir(Catalogo.Corresponde(Catalogo.Achar("nvda"), "deficiencia", null), "Busca deve ignorar acentos");
        Conferir(!Catalogo.Corresponde(Catalogo.Achar("steam"), "steam", Catalogo.ESTUDOS), "Filtro de categoria");
        var antigos = "chrome firefox brave 7zip winrar notepadpp office365 libreoffice acrobat vscode git node python innosetup terminal pwsh paintnet gimp inkscape irfanview vlc klite obs shotcut handbrake malwarebytes adwcleaner bitdefender bitwarden chatgpt claude ollama lmstudio powertoys everything rufus".Split(' ');
        Conferir(antigos.All(c => Catalogo.Achar(c) != null), "Preservar todas as chaves da versao anterior");
        Conferir(Catalogo.Apps.Where(a => a.Recomendado).Select(a => a.Chave).OrderBy(x => x).SequenceEqual(new[] { "chrome", "7zip", "office365", "acrobat", "whatsapp", "telegram", "vlc", "klite" }.OrderBy(x => x)), "Recomendados devem ser exatamente os oito apps definidos");
        Conferir(Catalogo.Achar("klite").Metodos[0].Alvo == "CodecGuide.K-LiteCodecPack.Mega", "K-Lite deve instalar a edicao Mega");
        var js = new JavaScriptSerializer();
        var legado = js.Deserialize<Estado>("{\"Versao\":2,\"Fase\":\"updates\",\"Escolhidos\":[\"chrome\",\"acrobat\"]}");
        Conferir(legado.AvisosWindowsUpdate != null && Catalogo.Resolver(legado.Escolhidos).Count == 2,
            "Estado antigo deve permanecer compativel");

        using (var janela = new JanelaTeste())
        using (var tela = new TelaSelecao())
        {
            janela.ShowInTaskbar = false;
            janela.StartPosition = FormStartPosition.Manual;
            janela.Location = new Point(-20000, -20000);
            janela.ClientSize = new Size(920, 602);
            janela.Controls.Add(tela);
            tela.Size = new Size(920, 602);
            janela.Show();
            Application.DoEvents();
            var estado = new Estado { Configurado = true, FazerInstalacao = true, FazerWindowsUpdate = true,
                Escolhidos = new List<string> { "chrome", "vlc" } };
            tela.Carregar(estado);
            var pesquisa = Campo<TextBox>(tela, "buscaApps");
            var categoria = Campo<ComboBox>(tela, "filtroCategoria");
            var painel = Campo<Panel>(tela, "painelApps");
            var itens = Campo<List<ItemPrograma>>(tela, "itens");
            pesquisa.Text = "zotero";
            Conferir(itens.Count(i => i.Visible) == 1, "Busca deve mostrar somente Zotero");
            painel.Controls.OfType<Button>().Single(b => b.Text == "Todos visíveis").PerformClick();
            var selecionados = new Estado();
            tela.Aplicar(selecionados);
            Conferir(selecionados.Escolhidos.Count == 3 && selecionados.Escolhidos.Contains("chrome") &&
                selecionados.Escolhidos.Contains("zotero"), "Marcar filtrados deve preservar escolhas ocultas");
            painel.Controls.OfType<Button>().Single(b => b.Text == "Limpar visíveis").PerformClick();
            tela.Aplicar(selecionados);
            Conferir(selecionados.Escolhidos.Count == 2 && !selecionados.Escolhidos.Contains("zotero"),
                "Limpar filtrados deve preservar escolhas ocultas");
            pesquisa.Text = "sem-resultado-xyz";
            Conferir(itens.All(i => !i.Visible) && Campo<Label>(tela, "resultadoBusca").Text.Contains("Nenhum resultado"),
                "Busca vazia deve informar nenhum resultado");
            pesquisa.Text = "";
            categoria.SelectedItem = Catalogo.ESTUDOS;
            Conferir(categoria.Text == Catalogo.ESTUDOS, "Categoria selecionada deve aparecer no campo");
            Application.DoEvents();
            Conferir(itens.Where(i => i.Visible).All(i => i.App.Categoria == Catalogo.ESTUDOS), "Filtro por categoria na tela");
            using (var bitmap = new Bitmap(920, 602))
            {
                tela.DrawToBitmap(bitmap, new Rectangle(0, 0, 920, 602));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "teste-catalogo-estudos.png"));
            }
            categoria.SelectedIndex = 0;
            Application.DoEvents();
            Conferir(itens.Count(i => i.Visible) == Catalogo.Apps.Length, "Limpar filtro deve restaurar catalogo completo");
            using (var bitmap = new Bitmap(920, 602))
            {
                tela.DrawToBitmap(bitmap, new Rectangle(0, 0, 920, 602));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "teste-catalogo.png"));
            }
        }
        var comFalha = new Estado { FazerWindowsUpdate = true };
        comFalha.AvisosWindowsUpdate.Add("Busca falhou");
        var relatorio = (string)typeof(TelaFinal).GetMethod("MontarRelatorio", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { comFalha });
        Conferir(relatorio.Contains("Busca falhou") && !relatorio.Contains("nenhuma atualização pendente foi encontrada"),
            "Erro de busca nao deve virar relatorio de Windows atualizado");
    }
    static void SmokeCom()
    {
        // Somente leitura do cache local: nao registra servicos, nao aceita EULAs,
        // nao baixa nem instala atualizacoes e nao altera energia/estado da maquina.
        Task.Run(delegate
        {
            var sessao = (IUpdateSession3)Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
            var buscador = sessao.CreateUpdateSearcher();
            buscador.Online = false;
            var job = buscador.BeginSearch("IsInstalled=0 and IsHidden=0", new AtualizadorWindows.CallbackFim(), null);
            try
            {
                EsperaWindowsUpdate.Aguardar(() => job.IsCompleted, null, job.RequestAbort, null,
                    TimeSpan.FromMinutes(2), Console.WriteLine);
                var resultado = buscador.EndSearch(job);
                Console.WriteLine("COM local: " + resultado.ResultCode + "; itens: " + resultado.Updates.Count);
                Conferir(resultado.ResultCode == OperationResultCode.orcSucceeded, "Busca COM local deve concluir");
            }
            finally { if (job.IsCompleted) job.CleanUp(); }
        }).GetAwaiter().GetResult();
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Estado.ModoPrevia = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TestarEspera();
            TestarProgressoInstalacao();
            TestarCatalogoETela();
            if (args.Contains("--wu-smoke")) SmokeCom();
            Console.WriteLine("OK: " + verificacoes + " verificacoes; " + Catalogo.Apps.Length + " apps / " + Catalogo.Categorias.Length + " categorias.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
