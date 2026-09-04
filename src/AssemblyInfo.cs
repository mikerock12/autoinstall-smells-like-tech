using System.Reflection;
using System.Runtime.InteropServices;

// Metadados do executavel. O csc.exe converte estes atributos no bloco
// VERSIONINFO do Win32 (o que aparece em Propriedades > Detalhes).
// Um binario SEM esses dados e um dos sinais de maior peso nos motores
// heuristicos do Windows Defender e do SmartScreen.
[assembly: AssemblyTitle("AutoInstall Smells Like Tech")]
[assembly: AssemblyDescription("Preparação automática de máquinas Windows 10 e 11: o técnico escolhe as etapas em uma tela só — Windows Update completo com reinicializações automáticas, preparo dos próprios instaladores (winget, cliente da Loja e catálogos de pacotes) antes de qualquer instalação, instalação dos programas selecionados de um catálogo por categoria (winget, Microsoft Store, script oficial ou instalador do fabricante) e atualização geral do que já está instalado, incluindo os apps da Microsoft Store. Ao terminar, opção de deixar a máquina em desempenho máximo permanente com protetor de tela.")]
[assembly: AssemblyProduct("AutoInstall Smells Like Tech")]
[assembly: AssemblyCompany("Smells Like Tech Informática")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Maicon Nunes - Smells Like Tech Informática")]
[assembly: AssemblyTrademark("Smells Like Tech Informática - www.smellsliketech.com.br")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCulture("")]

[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

[assembly: ComVisible(false)]
