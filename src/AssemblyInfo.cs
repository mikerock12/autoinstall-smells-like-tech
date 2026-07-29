using System.Reflection;
using System.Runtime.InteropServices;

// Metadados do executavel. O csc.exe converte estes atributos no bloco
// VERSIONINFO do Win32 (o que aparece em Propriedades > Detalhes).
// Um binario SEM esses dados e um dos sinais de maior peso nos motores
// heuristicos do Windows Defender e do SmartScreen.
[assembly: AssemblyTitle("AutoInstall — Pós-Formatação")]
[assembly: AssemblyDescription("Automatiza o pós-formatação do Windows: plano de energia de desempenho máximo temporário, todas as atualizações do Windows Update (incluindo opcionais e drivers) com retomada automática após reiniciar, instalação de programas essenciais via winget e atualização de todos os aplicativos, inclusive os da Microsoft Store.")]
[assembly: AssemblyProduct("AutoInstall Pós-Formatação")]
[assembly: AssemblyCompany("Smells Like Tech Informática")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Maicon Nunes - Smells Like Tech Informática")]
[assembly: AssemblyTrademark("Smells Like Tech Informática - www.smellsliketech.com.br")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCulture("")]

[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

[assembly: ComVisible(false)]
