# Gera lib\Interop.WUApiLib.dll a partir da type library do Windows Update
# (C:\Windows\System32\wuapi.dll), sem precisar de Visual Studio nem tlbimp.exe.
# DEVE rodar no Windows PowerShell 5.1 (powershell.exe), que usa .NET Framework.

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$dirLib = Join-Path $raiz 'lib'
if (-not (Test-Path $dirLib)) { New-Item -ItemType Directory -Path $dirLib | Out-Null }

$alvo = Join-Path $dirLib 'Interop.WUApiLib.dll'
if (Test-Path $alvo) { Write-Host "Ja existe: $alvo"; exit 0 }

$codigo = @'
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

public class GeradorInterop : ITypeLibImporterNotifySink
{
    // REGKIND_NONE = 2 (carrega a typelib sem registrar nada)
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern System.Runtime.InteropServices.ComTypes.ITypeLib LoadTypeLibEx(
        string strTypeLibName, int regKind);

    public void ReportEvent(ImporterEventKind eventKind, int eventCode, string eventMsg) { }
    public Assembly ResolveRef(object typeLib) { return null; }

    public static void Gerar(string caminhoTlb, string dirSaida, string nomeArquivo)
    {
        System.Runtime.InteropServices.ComTypes.ITypeLib tlb = LoadTypeLibEx(caminhoTlb, 2);
        TypeLibConverter conv = new TypeLibConverter();
        Environment.CurrentDirectory = dirSaida;
        AssemblyBuilder ab = conv.ConvertTypeLibToAssembly(
            tlb, nomeArquivo, 0, new GeradorInterop(),
            null, null, "WUApiLib", new Version(2, 0, 0, 0));
        ab.Save(nomeArquivo);
    }
}
'@

Add-Type -TypeDefinition $codigo
[GeradorInterop]::Gerar("$env:WINDIR\System32\wuapi.dll", $dirLib, 'Interop.WUApiLib.dll')

if (Test-Path $alvo) {
    Write-Host "OK: $alvo gerado."
} else {
    Write-Error "Falha ao gerar $alvo"
}
