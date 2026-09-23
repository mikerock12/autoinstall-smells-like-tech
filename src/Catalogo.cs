using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoInstall
{
    // Como um programa e instalado. A ordem em AppCatalogo.Metodos e a ordem
    // de tentativa: o primeiro que colocar o programa na maquina ganha.
    public enum Via
    {
        Winget,       // Alvo = id do pacote no winget (fonte padrao)
        MSStore,      // Alvo = id do produto na Microsoft Store (winget -s msstore)
        PowerShell,   // Alvo = comando PowerShell (o caso do "irm ... | iex")
        Direto,       // Alvo = URL do instalador oficial; Args = argumentos silenciosos
        ODT           // Office Deployment Tool (InstaladorOffice.cs)
    }

    public class MetodoInstalacao
    {
        public Via Via;
        public string Alvo;
        public string Args;

        public MetodoInstalacao(Via via, string alvo) { Via = via; Alvo = alvo; }
        public MetodoInstalacao(Via via, string alvo, string args) { Via = via; Alvo = alvo; Args = args; }

        // Como esse metodo aparece na tela de escolha e no relatorio.
        public string Rotulo
        {
            get
            {
                switch (Via)
                {
                    case Via.Winget: return "winget";
                    case Via.MSStore: return "Microsoft Store";
                    case Via.PowerShell: return "PowerShell";
                    case Via.Direto: return "instalador oficial";
                    case Via.ODT: return "Office Deployment Tool";
                }
                return "";
            }
        }
    }

    public class AppCatalogo
    {
        public string Chave;        // identificador estavel: e o que fica salvo no estado
        public string Nome;
        public string Descricao;    // resumo de uma linha, para ajudar a escolher
        public string Categoria;
        public bool Recomendado;    // vem marcado quando o tecnico clica em "Recomendados"
        public MetodoInstalacao[] Metodos;

        public AppCatalogo(string chave, string nome, string descricao, string categoria,
            bool recomendado, params MetodoInstalacao[] metodos)
        {
            Chave = chave;
            Nome = nome;
            Descricao = descricao;
            Categoria = categoria;
            Recomendado = recomendado;
            Metodos = metodos;
        }

        // "winget · instalador oficial" — mostrado em cinza ao lado do nome,
        // para o tecnico saber por onde a instalacao vai sair.
        public string Vias
        {
            get
            {
                var partes = new List<string>();
                foreach (MetodoInstalacao m in Metodos)
                    if (!partes.Contains(m.Rotulo)) partes.Add(m.Rotulo);
                return string.Join(" · ", partes.ToArray());
            }
        }
    }

    // Catalogo por publico e finalidade. Novas opcoes sao opt-in para nao
    // transformar "Recomendados" numa instalacao de todos os perfis.
    //
    // IDs conferidos no repositorio oficial microsoft/winget-pkgs.
    // Onde existe um endereco oficial que sempre aponta para a ultima versao,
    // ele entra como metodo de reserva — assim uma falha do winget (manifesto
    // com hash velho, fonte fora do ar) nao derruba a instalacao.
    public static class Catalogo
    {
        public const string NAVEGADORES = "Navegadores";
        public const string COMPACTADORES = "Compactadores";
        public const string TEXTO = "Editores de texto e documentos";
        public const string DESENVOLVIMENTO = "Desenvolvimento de software";
        public const string IMAGEM = "Editores de imagem";
        public const string VIDEO = "Vídeo, áudio e codecs";
        public const string SEGURANCA = "Antivírus e segurança";
        public const string IA = "Inteligência artificial";
        public const string UTILITARIOS = "Utilitários do dia a dia";
        public const string PDF = "PDF e digitalização";
        public const string COMUNICACAO = "Comunicação e reuniões";
        public const string PRODUTIVIDADE = "Organização e produtividade";
        public const string NUVEM = "Nuvem e sincronização";
        public const string ESTUDOS = "Estudos e pesquisa";
        public const string AUDIO = "Música e produção de áudio";
        public const string MODELAGEM = "3D, CAD e eletrônica";
        public const string JOGOS = "Jogos e plataformas";
        public const string SUPORTE = "Acesso remoto e redes";
        public const string DIAGNOSTICO = "Diagnóstico e manutenção";
        public const string ACESSIBILIDADE = "Acessibilidade e leitura";

        // Ordem em que as categorias aparecem na tela.
        public static readonly string[] Categorias = new string[]
        {
            NAVEGADORES, COMPACTADORES, TEXTO, PDF, COMUNICACAO,
            PRODUTIVIDADE, NUVEM, ESTUDOS, DESENVOLVIMENTO, IMAGEM,
            VIDEO, AUDIO, MODELAGEM, JOGOS, SEGURANCA, IA, SUPORTE,
            DIAGNOSTICO, ACESSIBILIDADE, UTILITARIOS
        };

        static MetodoInstalacao Winget(string id) { return new MetodoInstalacao(Via.Winget, id); }
        static MetodoInstalacao Loja(string id) { return new MetodoInstalacao(Via.MSStore, id); }
        static MetodoInstalacao Direto(string url, string args) { return new MetodoInstalacao(Via.Direto, url, args); }
        static MetodoInstalacao Ps(string comando) { return new MetodoInstalacao(Via.PowerShell, comando); }

        public static readonly AppCatalogo[] Apps = new AppCatalogo[]
        {
            // ---------------- Navegadores ----------------
            new AppCatalogo("chrome", "Google Chrome",
                "O navegador mais usado do mundo; sincroniza com o Google.",
                NAVEGADORES, true,
                Winget("Google.Chrome"),
                // O Google republica o instalador no mesmo endereco e o hash do
                // manifesto do winget fica velho — por isso a reserva oficial.
                Direto("https://dl.google.com/tag/s/dl/chrome/install/googlechromestandaloneenterprise64.msi", null)),

            new AppCatalogo("firefox", "Mozilla Firefox",
                "Independente e forte em privacidade, com anti-rastreamento.",
                NAVEGADORES, false,
                Winget("Mozilla.Firefox"),
                Direto("https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=pt-BR", "/S")),

            new AppCatalogo("brave", "Brave",
                "Base do Chrome, com bloqueador de anúncios embutido.",
                NAVEGADORES, false,
                Winget("Brave.Brave"),
                Direto("https://laptop-updates.brave.com/latest/winx64", "/silent /install")),

            // ---------------- Compactadores ----------------
            new AppCatalogo("7zip", "7-Zip",
                "Compactador leve e gratuito; abre ZIP, RAR, 7z e ISO.",
                COMPACTADORES, true,
                Winget("7zip.7zip")),

            new AppCatalogo("winrar", "WinRAR",
                "O compactador clássico; cria e abre arquivos RAR.",
                COMPACTADORES, false,
                Winget("RARLab.WinRAR")),

            // ---------------- Texto e documentos ----------------
            new AppCatalogo("notepadpp", "Notepad++",
                "Bloco de notas com abas, sintaxe colorida e busca em pastas.",
                TEXTO, true,
                Winget("Notepad++.Notepad++")),

            new AppCatalogo("office365", "Microsoft 365 (Word, Excel, PowerPoint)",
                "Word, Excel e PowerPoint em português, pela via oficial.",
                TEXTO, false,
                new MetodoInstalacao(Via.ODT, InstaladorOffice.PRODUTO)),

            new AppCatalogo("libreoffice", "LibreOffice",
                "Suíte de escritório gratuita; abre os formatos do Office.",
                TEXTO, false,
                Winget("TheDocumentFoundation.LibreOffice")),

            new AppCatalogo("acrobat", "Adobe Acrobat Reader",
                "O leitor de PDF padrão do mercado, com assinatura digital.",
                PDF, true,
                Winget("Adobe.Acrobat.Reader.64-bit")),

            // ---------------- Desenvolvimento ----------------
            new AppCatalogo("vscode", "Visual Studio Code",
                "Editor de código da Microsoft, leve e cheio de extensões.",
                DESENVOLVIMENTO, true,
                Winget("Microsoft.VisualStudioCode"),
                Direto("https://update.code.visualstudio.com/latest/win32-x64-user/stable",
                       "/VERYSILENT /NORESTART /MERGETASKS=!runcode")),

            new AppCatalogo("git", "Git",
                "Controle de versão; obrigatório para trabalhar com GitHub.",
                DESENVOLVIMENTO, true,
                Winget("Git.Git")),

            new AppCatalogo("node", "Node.js LTS",
                "Runtime JavaScript com npm; base de todo projeto web.",
                DESENVOLVIMENTO, false,
                Winget("OpenJS.NodeJS.LTS")),

            new AppCatalogo("python", "Python 3",
                "Linguagem geral, forte em automação, dados e IA.",
                DESENVOLVIMENTO, false,
                Winget("Python.Python.3.13")),

            new AppCatalogo("innosetup", "Inno Setup",
                "Cria instaladores .exe profissionais para Windows.",
                DESENVOLVIMENTO, false,
                Winget("JRSoftware.InnoSetup")),

            new AppCatalogo("terminal", "Windows Terminal",
                "Terminal com abas para PowerShell, CMD e WSL juntos.",
                DESENVOLVIMENTO, false,
                Winget("Microsoft.WindowsTerminal"),
                Loja("9N0DX20HK701")),

            new AppCatalogo("pwsh", "PowerShell 7",
                "A versão atual do PowerShell, bem mais rápida.",
                DESENVOLVIMENTO, false,
                Winget("Microsoft.PowerShell"),
                // Instalador oficial da Microsoft por script — o caminho "irm".
                Ps("iex \"& { $(irm https://aka.ms/install-powershell.ps1) } -UseMSI -Quiet\"")),

            // ---------------- Imagem ----------------
            new AppCatalogo("paintnet", "paint.net",
                "Editor de imagens rápido, com camadas e efeitos.",
                IMAGEM, true,
                Winget("dotPDN.PaintDotNet")),

            new AppCatalogo("gimp", "GIMP",
                "Editor profissional gratuito, alternativa ao Photoshop.",
                IMAGEM, false,
                Winget("GIMP.GIMP")),

            new AppCatalogo("inkscape", "Inkscape",
                "Desenho vetorial (SVG) para logos e ilustrações.",
                IMAGEM, false,
                Winget("Inkscape.Inkscape")),

            new AppCatalogo("irfanview", "IrfanView",
                "Visualizador de imagens instantâneo, converte em lote.",
                IMAGEM, false,
                Winget("IrfanSkiljan.IrfanView")),

            // ---------------- Vídeo, áudio e codecs ----------------
            new AppCatalogo("vlc", "VLC Media Player",
                "Toca qualquer vídeo ou áudio sem instalar mais nada.",
                VIDEO, true,
                Winget("VideoLAN.VLC")),

            new AppCatalogo("klite", "K-Lite Codec Pack",
                "Codecs de áudio e vídeo atualizados + o player MPC-HC.",
                VIDEO, true,
                Winget("CodecGuide.K-LiteCodecPack.Standard")),

            new AppCatalogo("obs", "OBS Studio",
                "Gravação de tela e transmissão ao vivo; padrão do meio.",
                VIDEO, false,
                Winget("OBSProject.OBSStudio")),

            new AppCatalogo("shotcut", "Shotcut",
                "Editor de vídeo gratuito e sem marca d'água.",
                VIDEO, false,
                Winget("Meltytech.Shotcut")),

            new AppCatalogo("handbrake", "HandBrake",
                "Converte e comprime vídeos para qualquer formato.",
                VIDEO, false,
                Winget("HandBrake.HandBrake")),

            // ---------------- Segurança ----------------
            new AppCatalogo("malwarebytes", "Malwarebytes",
                "Remove vírus e adware que o antivírus comum deixa passar.",
                SEGURANCA, true,
                Winget("Malwarebytes.Malwarebytes")),

            new AppCatalogo("adwcleaner", "AdwCleaner",
                "Faxina adware, toolbars e sequestro de navegador.",
                SEGURANCA, false,
                Winget("Malwarebytes.AdwCleaner")),

            new AppCatalogo("bitdefender", "Bitdefender",
                "Antivírus completo, sempre no topo dos testes.",
                SEGURANCA, false,
                Winget("Bitdefender.Bitdefender")),

            new AppCatalogo("bitwarden", "Bitwarden",
                "Cofre de senhas gratuito; sincroniza com o celular.",
                SEGURANCA, false,
                Winget("Bitwarden.Bitwarden")),

            // ---------------- IA ----------------
            new AppCatalogo("chatgpt", "ChatGPT",
                "O aplicativo oficial da OpenAI para Windows.",
                IA, true,
                Loja("9PLM9XGG6VKS")),

            new AppCatalogo("claude", "Claude",
                "App oficial da Anthropic; forte em texto e programação.",
                IA, true,
                Winget("Anthropic.Claude")),

            new AppCatalogo("ollama", "Ollama",
                "Roda modelos de IA no seu PC, sem internet.",
                IA, false,
                Winget("Ollama.Ollama"),
                Direto("https://ollama.com/download/OllamaSetup.exe", "/VERYSILENT /NORESTART")),

            new AppCatalogo("lmstudio", "LM Studio",
                "Interface para baixar e conversar com IAs locais.",
                IA, false,
                Winget("ElementLabs.LMStudio")),

            // ---------------- Utilitários ----------------
            new AppCatalogo("powertoys", "Microsoft PowerToys",
                "Utilitários oficiais: renomear em lote, cores, atalhos.",
                UTILITARIOS, true,
                Winget("Microsoft.PowerToys")),

            new AppCatalogo("everything", "Everything",
                "Acha qualquer arquivo do PC na hora, pelo nome.",
                UTILITARIOS, false,
                Winget("voidtools.Everything")),

            new AppCatalogo("rufus", "Rufus",
                "Cria pendrive bootável de instalação do Windows.",
                UTILITARIOS, false,
                Winget("Rufus.Rufus")),

            new AppCatalogo("edge", "Microsoft Edge", "Navegador da Microsoft com perfis e sincronização.",
                NAVEGADORES, false, Winget("Microsoft.Edge")),
            new AppCatalogo("vivaldi", "Vivaldi", "Navegador com abas organizáveis e personalização.",
                NAVEGADORES, false, Winget("Vivaldi.Vivaldi")),
            new AppCatalogo("peazip", "PeaZip", "Compactação gratuita com criptografia de arquivos.",
                COMPACTADORES, false, Winget("Giorgiotani.Peazip")),
            new AppCatalogo("onlyoffice", "ONLYOFFICE Desktop", "Documentos, planilhas e apresentações no desktop.",
                TEXTO, false, Winget("ONLYOFFICE.DesktopEditors")),

            new AppCatalogo("sumatrapdf", "SumatraPDF", "Leitor leve de PDFs, e-books e quadrinhos.",
                PDF, false, Winget("SumatraPDF.SumatraPDF")),
            new AppCatalogo("pdf24", "PDF24 Creator", "Cria, junta, comprime e converte arquivos PDF.",
                PDF, false, Winget("geeksoftwareGmbH.PDF24Creator")),
            new AppCatalogo("naps2", "NAPS2", "Digitaliza documentos e reconhece texto com OCR.",
                PDF, false, Winget("Cyanfish.NAPS2")),

            new AppCatalogo("whatsapp", "WhatsApp", "Mensagens e chamadas; requer conta e vinculação.",
                COMUNICACAO, false, Loja("9NKSQGP7F2NH")),
            new AppCatalogo("telegram", "Telegram Desktop", "Mensagens, grupos e canais sincronizados.",
                COMUNICACAO, false, Winget("Telegram.TelegramDesktop")),
            new AppCatalogo("signal", "Signal", "Mensagens privadas vinculadas ao seu celular.",
                COMUNICACAO, false, Winget("OpenWhisperSystems.Signal")),
            new AppCatalogo("discord", "Discord", "Comunidades, chat e chamadas de voz e vídeo.",
                COMUNICACAO, false, Winget("Discord.Discord")),
            new AppCatalogo("zoom", "Zoom Workplace", "Videoconferências para trabalho, aulas e reuniões.",
                COMUNICACAO, false, Winget("Zoom.Zoom")),
            new AppCatalogo("teams", "Microsoft Teams", "Reuniões e colaboração; requer conta compatível.",
                COMUNICACAO, false, Winget("Microsoft.Teams")),
            new AppCatalogo("thunderbird", "Mozilla Thunderbird", "E-mail, agenda e contatos em um só aplicativo.",
                COMUNICACAO, false, Winget("Mozilla.Thunderbird")),

            new AppCatalogo("obsidian", "Obsidian", "Notas locais em Markdown com links entre assuntos.",
                PRODUTIVIDADE, false, Winget("Obsidian.Obsidian")),
            new AppCatalogo("joplin", "Joplin", "Notas e tarefas com opção de sincronização.",
                PRODUTIVIDADE, false, Winget("Joplin.Joplin")),
            new AppCatalogo("notion", "Notion", "Notas, projetos e bases de dados; requer conta.",
                PRODUTIVIDADE, false, Winget("Notion.Notion")),

            new AppCatalogo("googledrive", "Google Drive", "Sincroniza arquivos do Google; requer conta.",
                NUVEM, false, Winget("Google.GoogleDrive")),
            new AppCatalogo("onedrive", "Microsoft OneDrive", "Sincroniza pastas e arquivos com a conta Microsoft.",
                NUVEM, false, Winget("Microsoft.OneDrive")),
            new AppCatalogo("dropbox", "Dropbox", "Arquivos na nuvem e compartilhamento; requer conta.",
                NUVEM, false, Winget("Dropbox.Dropbox")),
            new AppCatalogo("syncthing", "Syncthing", "Sincroniza pastas entre dispositivos; exige configuração.",
                NUVEM, false, Winget("Syncthing.Syncthing")),

            new AppCatalogo("anki", "Anki", "Cartões de estudo com revisão espaçada.",
                ESTUDOS, false, Winget("Anki.Anki")),
            new AppCatalogo("calibre", "calibre", "Organiza, converte e lê livros digitais.",
                ESTUDOS, false, Winget("calibre.calibre")),
            new AppCatalogo("zotero", "Zotero", "Organiza referências, artigos e citações acadêmicas.",
                ESTUDOS, false, Winget("DigitalScholar.Zotero")),
            new AppCatalogo("geogebra", "GeoGebra Classic", "Geometria, álgebra e gráficos para aprender matemática.",
                ESTUDOS, false, Winget("GeoGebra.Classic")),

            new AppCatalogo("githubdesktop", "GitHub Desktop", "Interface gráfica para repositórios Git e GitHub.",
                DESENVOLVIMENTO, false, Winget("GitHub.GitHubDesktop")),
            new AppCatalogo("githubcli", "GitHub CLI", "Issues, pull requests e repositórios pelo terminal.",
                DESENVOLVIMENTO, false, Winget("GitHub.cli")),
            new AppCatalogo("dbeaver", "DBeaver Community", "Cliente SQL para consultar e administrar bancos.",
                DESENVOLVIMENTO, false, Winget("DBeaver.DBeaver.Community")),
            new AppCatalogo("postman", "Postman", "Cria e testa requisições de APIs.",
                DESENVOLVIMENTO, false, Winget("Postman.Postman")),

            new AppCatalogo("krita", "Krita", "Pintura digital e ilustração com mesa digitalizadora.",
                IMAGEM, false, Winget("KDE.Krita")),
            new AppCatalogo("darktable", "darktable", "Revelação de fotos RAW e fluxo de fotografia.",
                IMAGEM, false, Winget("darktable.darktable")),
            new AppCatalogo("kdenlive", "Kdenlive", "Edição de vídeo com múltiplas faixas e efeitos.",
                VIDEO, false, Winget("KDE.Kdenlive")),
            new AppCatalogo("audacity", "Audacity", "Gravação e edição de áudio para voz e podcasts.",
                AUDIO, false, Winget("Audacity.Audacity")),
            new AppCatalogo("musescore", "MuseScore Studio", "Cria, edita e reproduz partituras musicais.",
                AUDIO, false, Winget("Musescore.Musescore")),
            new AppCatalogo("spotify", "Spotify", "Músicas e podcasts; requer conta, com plano opcional.",
                AUDIO, false, Winget("Spotify.Spotify")),
            new AppCatalogo("foobar2000", "foobar2000", "Player leve para organizar e ouvir músicas locais.",
                AUDIO, false, Winget("PeterPawlowski.foobar2000")),

            new AppCatalogo("blender", "Blender", "Modelagem, animação e renderização 3D.",
                MODELAGEM, false, Winget("BlenderFoundation.Blender")),
            new AppCatalogo("freecad", "FreeCAD", "CAD paramétrico para projetos e peças 3D.",
                MODELAGEM, false, Winget("FreeCAD.FreeCAD")),
            new AppCatalogo("kicad", "KiCad", "Esquemas eletrônicos e desenho de placas de circuito.",
                MODELAGEM, false, Winget("KiCad.KiCad")),
            new AppCatalogo("cura", "UltiMaker Cura", "Prepara modelos para impressão 3D; configure sua máquina.",
                MODELAGEM, false, Winget("Ultimaker.Cura")),

            new AppCatalogo("steam", "Steam", "Loja e biblioteca de jogos; requer conta.",
                JOGOS, false, Winget("Valve.Steam")),
            new AppCatalogo("epicgames", "Epic Games Launcher", "Biblioteca de jogos da Epic; requer conta.",
                JOGOS, false, Winget("EpicGames.EpicGamesLauncher")),
            new AppCatalogo("gog", "GOG GALAXY", "Cliente da loja GOG e biblioteca de jogos.",
                JOGOS, false, Winget("GOG.Galaxy")),
            new AppCatalogo("playnite", "Playnite", "Reúne bibliotecas de diferentes lojas de jogos.",
                JOGOS, false, Winget("Playnite.Playnite")),

            new AppCatalogo("keepassxc", "KeePassXC", "Cofre de senhas local com banco criptografado.",
                SEGURANCA, false, Winget("KeePassXCTeam.KeePassXC")),
            new AppCatalogo("veracrypt", "VeraCrypt", "Volumes criptografados; configuração manual necessária.",
                SEGURANCA, false, Winget("IDRIX.VeraCrypt")),

            new AppCatalogo("teamviewer", "TeamViewer", "Suporte remoto; uso profissional exige licença.",
                SUPORTE, false, Winget("TeamViewer.TeamViewer")),
            new AppCatalogo("anydesk", "AnyDesk", "Suporte remoto; uso profissional pode exigir licença.",
                SUPORTE, false, Winget("AnyDesk.AnyDesk")),
            new AppCatalogo("winscp", "WinSCP", "Transferência de arquivos por SFTP, SCP e FTP.",
                SUPORTE, false, Winget("WinSCP.WinSCP")),
            new AppCatalogo("putty", "PuTTY", "Conexões SSH e serial para administração de sistemas.",
                SUPORTE, false, Winget("PuTTY.PuTTY")),
            new AppCatalogo("wireshark", "Wireshark", "Análise de tráfego de rede; captura pode exigir Npcap.",
                SUPORTE, false, Winget("WiresharkFoundation.Wireshark")),

            new AppCatalogo("cpuz", "CPU-Z", "Identifica processador, placa-mãe e memória.",
                DIAGNOSTICO, false, Winget("CPUID.CPU-Z")),
            new AppCatalogo("hwinfo", "HWiNFO", "Sensores e hardware; confira a licença para uso comercial.",
                DIAGNOSTICO, false, Winget("REALiX.HWiNFO")),
            new AppCatalogo("crystaldiskinfo", "CrystalDiskInfo", "Mostra saúde e dados SMART de SSDs e discos.",
                DIAGNOSTICO, false, Winget("CrystalDewWorld.CrystalDiskInfo")),
            new AppCatalogo("windirstat", "WinDirStat", "Visualiza quais pastas e arquivos ocupam o disco.",
                DIAGNOSTICO, false, Winget("WinDirStat.WinDirStat")),

            new AppCatalogo("nvda", "NVDA", "Leitor de tela gratuito para pessoas com deficiência visual.",
                ACESSIBILIDADE, false, Winget("NVAccess.NVDA")),
            new AppCatalogo("balabolka", "Balabolka", "Lê textos em voz alta usando as vozes do Windows.",
                ACESSIBILIDADE, false, Winget("CrossPlusA.Balabolka")),
            new AppCatalogo("sharex", "ShareX", "Capturas de tela, gravações e anotações.",
                UTILITARIOS, false, Winget("ShareX.ShareX")),
            new AppCatalogo("localsend", "LocalSend", "Transfere arquivos entre aparelhos na mesma rede.",
                UTILITARIOS, false, Winget("LocalSend.LocalSend")),
        };

        public static bool Corresponde(AppCatalogo app, string termo, string categoria)
        {
            if (!string.IsNullOrEmpty(categoria) && app.Categoria != categoria) return false;
            if (string.IsNullOrWhiteSpace(termo)) return true;
            string texto = app.Nome + " " + app.Descricao + " " + app.Categoria;
            return CultureInfo.GetCultureInfo("pt-BR").CompareInfo.IndexOf(texto, termo.Trim(),
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        public static AppCatalogo Achar(string chave)
        {
            foreach (AppCatalogo a in Apps)
                if (string.Equals(a.Chave, chave, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        public static List<AppCatalogo> Da(string categoria)
        {
            var lista = new List<AppCatalogo>();
            foreach (AppCatalogo a in Apps)
                if (a.Categoria == categoria) lista.Add(a);
            return lista;
        }

        // Resolve as chaves salvas no estado para os itens do catalogo,
        // preservando a ordem do catalogo (categorias juntas no relatorio).
        public static List<AppCatalogo> Resolver(List<string> chaves)
        {
            var lista = new List<AppCatalogo>();
            if (chaves == null) return lista;
            foreach (string categoria in Categorias)
                foreach (AppCatalogo a in Da(categoria))
                    if (chaves.Contains(a.Chave)) lista.Add(a);
            return lista;
        }
    }
}
