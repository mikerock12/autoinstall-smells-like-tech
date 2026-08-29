using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace AutoInstall
{
    public class RodadaUpdates
    {
        public int Numero { get; set; }
        public List<string> Atualizacoes { get; set; }
        public RodadaUpdates() { Atualizacoes = new List<string>(); }
    }

    public class AppInstalado
    {
        public string Nome { get; set; }
        public string Id { get; set; }
        public string Versao { get; set; }
        public string Status { get; set; }
    }

    // Estado persistido em ProgramData: e ele que permite continuar do ponto
    // certo depois de cada reinicializacao do Windows Update.
    // Fases: selecao -> energia -> updates -> apps -> upgrade -> fim -> concluido
    public class Estado
    {
        // Muda quando o formato do arquivo muda de forma incompativel. Um
        // estado de versao diferente e descartado em vez de interpretado
        // torto (o da versao 1 nao tinha escolha nenhuma salva).
        public const int VERSAO_ATUAL = 2;

        public int Versao { get; set; }
        public string Fase { get; set; }
        public int Reinicios { get; set; }
        public bool EnergiaConfigurada { get; set; }
        public bool Interrompido { get; set; }
        public string PlanoUltra { get; set; }      // GUID do plano temporario (para remover no final)
        public string PlanoOriginal { get; set; }   // GUID do plano ativo antes de tudo
        public List<RodadaUpdates> Rodadas { get; set; }
        public List<AppInstalado> Apps { get; set; }
        public List<string> Upgrades { get; set; }
        public string InicioEm { get; set; }

        // --- Escolhas da primeira tela ---
        // Ficam salvas junto com o resto porque o processo atravessa varias
        // reinicializacoes: e daqui que a retomada sabe o que ainda falta.
        public bool Configurado { get; set; }        // ja passou pela tela de escolha
        public bool FazerWindowsUpdate { get; set; }
        public bool FazerInstalacao { get; set; }
        public bool FazerAtualizacaoGeral { get; set; }
        public List<string> Escolhidos { get; set; } // chaves do Catalogo

        // Versao NAO e preenchida aqui de proposito. O JavaScriptSerializer
        // instancia pelo construtor e so sobrescreve os campos presentes no
        // JSON: se o construtor ja carimbasse a versao atual, um arquivo
        // antigo (que nao tem o campo) passaria pela conferencia como se
        // fosse do formato novo. Quem carimba e o Salvar().
        public Estado()
        {
            Rodadas = new List<RodadaUpdates>();
            Apps = new List<AppInstalado>();
            Upgrades = new List<string>();
            Escolhidos = new List<string>();
        }

        public static string Pasta
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SmellsLikeTech", "AutoInstall");
            }
        }

        static string Arquivo { get { return Path.Combine(Pasta, "estado.json"); } }

        public static Estado Carregar()
        {
            try
            {
                if (File.Exists(Arquivo))
                {
                    var js = new JavaScriptSerializer();
                    var e = js.Deserialize<Estado>(File.ReadAllText(Arquivo));
                    if (e != null && e.Versao == VERSAO_ATUAL)
                    {
                        if (e.Rodadas == null) e.Rodadas = new List<RodadaUpdates>();
                        if (e.Apps == null) e.Apps = new List<AppInstalado>();
                        if (e.Upgrades == null) e.Upgrades = new List<string>();
                        if (e.Escolhidos == null) e.Escolhidos = new List<string>();
                        if (string.IsNullOrEmpty(e.Fase)) e.Fase = "selecao";
                        return e;
                    }
                }
            }
            catch { }
            var novo = new Estado();
            novo.Versao = VERSAO_ATUAL;
            novo.Fase = "selecao";
            novo.InicioEm = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            return novo;
        }

        public void Salvar()
        {
            try
            {
                Versao = VERSAO_ATUAL;
                Directory.CreateDirectory(Pasta);
                File.WriteAllText(Arquivo, new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }

        // "Refazer todas as etapas": apaga o estado salvo para o processo
        // recomecar do zero na proxima execucao.
        public static void Apagar()
        {
            try { if (File.Exists(Arquivo)) File.Delete(Arquivo); }
            catch { }
        }

        public static void LogArquivo(string linha)
        {
            try
            {
                Directory.CreateDirectory(Pasta);
                File.AppendAllText(Path.Combine(Pasta, "log.txt"),
                    DateTime.Now.ToString("dd/MM HH:mm:ss  ") + linha + Environment.NewLine);
            }
            catch { }
        }
    }
}
