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
    // Fases: energia -> updates -> apps -> upgrade -> fim -> concluido
    public class Estado
    {
        public string Fase { get; set; }
        public int Reinicios { get; set; }
        public bool EnergiaConfigurada { get; set; }
        public string PlanoUltra { get; set; }      // GUID do plano temporario (para remover no final)
        public string PlanoOriginal { get; set; }   // GUID do plano ativo antes de tudo
        public List<RodadaUpdates> Rodadas { get; set; }
        public List<AppInstalado> Apps { get; set; }
        public List<string> Upgrades { get; set; }
        public string InicioEm { get; set; }

        public Estado()
        {
            Rodadas = new List<RodadaUpdates>();
            Apps = new List<AppInstalado>();
            Upgrades = new List<string>();
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
                    if (e != null)
                    {
                        if (e.Rodadas == null) e.Rodadas = new List<RodadaUpdates>();
                        if (e.Apps == null) e.Apps = new List<AppInstalado>();
                        if (e.Upgrades == null) e.Upgrades = new List<string>();
                        if (string.IsNullOrEmpty(e.Fase)) e.Fase = "energia";
                        return e;
                    }
                }
            }
            catch { }
            var novo = new Estado();
            novo.Fase = "energia";
            novo.InicioEm = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            return novo;
        }

        public void Salvar()
        {
            try
            {
                Directory.CreateDirectory(Pasta);
                File.WriteAllText(Arquivo, new JavaScriptSerializer().Serialize(this));
            }
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
