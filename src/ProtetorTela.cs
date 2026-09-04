using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoInstall
{
    // Protetor de tela "Faixas" (Ribbons.scr) em 15 minutos, aplicado no fim
    // quando o tecnico marca "Sou ousado".
    //
    // POR QUE ISTO EXISTE: no modo ousado a maquina fica com a tela ligada
    // para sempre, e imagem parada por muitas horas marca o monitor. O
    // protetor resolve isso sem devolver o computador para o modo de energia
    // que desliga e suspende tudo.
    public static class ProtetorTela
    {
        public const int MINUTOS = 15;
        public const string NOME = "Faixas";
        const string ARQUIVO = "Ribbons.scr";
        const string CHAVE = @"Control Panel\Desktop";

        const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
        const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
        const uint SPIF_UPDATEINIFILE = 0x01;
        const uint SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint acao, uint parametro, IntPtr valor, uint aviso);

        public static bool Configurar(Action<string> log)
        {
            string scr = Path.Combine(Environment.SystemDirectory, ARQUIVO);
            if (!File.Exists(scr))
            {
                log("Aviso: o protetor de tela " + NOME + " não existe nesta edição do Windows.");
                return false;
            }

            int segundos = MINUTOS * 60;
            int perfis = 0;

            // O perfil de quem esta rodando o programa.
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(CHAVE))
                    if (Gravar(k, scr, segundos)) perfis++;
            }
            catch (Exception ex)
            {
                log("Aviso: não consegui configurar o protetor de tela — " + ex.Message);
            }

            // O programa roda elevado. Quando a elevacao vem de uma conta de
            // administrador diferente da que esta usando o computador, o
            // HKEY_CURRENT_USER acima e o do administrador, e o cliente nunca
            // veria o protetor. Por isso os perfis reais carregados em
            // HKEY_USERS tambem recebem a configuracao - o do usuario logado
            // esta sempre entre eles.
            try
            {
                foreach (string sid in Registry.Users.GetSubKeyNames())
                {
                    // S-1-5-21-... = conta de pessoa; o resto e conta de
                    // sistema (SERVICE, NETWORK SERVICE) e nao tem area de
                    // trabalho. "_Classes" e o hive de tipos, nao de perfil.
                    if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;
                    if (sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (RegistryKey k = Registry.Users.CreateSubKey(sid + "\\" + CHAVE))
                            if (Gravar(k, scr, segundos)) perfis++;
                    }
                    catch { }   // perfil sem permissao: segue para o proximo
                }
            }
            catch { }

            // O registro vale a partir do proximo logon; isto faz valer agora,
            // na sessao que esta na tela.
            try
            {
                SystemParametersInfo(SPI_SETSCREENSAVETIMEOUT, (uint)segundos, IntPtr.Zero,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SystemParametersInfo(SPI_SETSCREENSAVEACTIVE, 1, IntPtr.Zero,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch { }

            if (perfis == 0)
            {
                log("Aviso: não consegui configurar o protetor de tela em nenhum perfil.");
                return false;
            }
            log(string.Format("Protetor de tela {0} configurado para {1} minutos.", NOME, MINUTOS));
            return true;
        }

        static bool Gravar(RegistryKey k, string scr, int segundos)
        {
            if (k == null) return false;
            k.SetValue("SCRNSAVE.EXE", scr, RegistryValueKind.String);
            k.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
            k.SetValue("ScreenSaveTimeOut", segundos.ToString(), RegistryValueKind.String);
            // Pedir senha ao voltar e preferencia de quem usa a maquina: so
            // define um valor se ainda nao houver nenhum, para nao baixar a
            // seguranca de quem ja escolheu o contrario.
            if (k.GetValue("ScreenSaverIsSecure") == null)
                k.SetValue("ScreenSaverIsSecure", "0", RegistryValueKind.String);
            return true;
        }
    }
}
