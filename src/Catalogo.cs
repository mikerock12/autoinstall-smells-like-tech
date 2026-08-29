using System;
using System.Collections.Generic;

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

    // Catalogo dos programas oferecidos na tela inicial. A regra e "os melhores
    // de cada categoria", nao "todos": lista curta, escolha rapida.
    //
    // Todo id de winget aqui foi conferido com "winget show --id <id> -e".
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

        // Ordem em que as categorias aparecem na tela.
        public static readonly string[] Categorias = new string[]
        {
            NAVEGADORES, COMPACTADORES, TEXTO, DESENVOLVIMENTO,
            IMAGEM, VIDEO, SEGURANCA, IA, UTILITARIOS
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
                TEXTO, true,
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
        };

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
            foreach (AppCatalogo a in Apps)
                if (chaves.Contains(a.Chave)) lista.Add(a);
            return lista;
        }
    }
}
