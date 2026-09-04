# Poe os PROPRIOS instaladores em dia antes de instalar qualquer programa.
#
# Atualiza, pela API WinRT da Loja (AppInstallManager), os dois pacotes de que
# todo o resto depende:
#   - Microsoft.DesktopAppInstaller  = o winget
#   - Microsoft.WindowsStore         = o cliente da Loja (a fonte msstore)
#
# Por que pela Loja e nao pelo "winget upgrade": um winget velho o bastante
# pode nao conseguir se atualizar sozinho, e um cliente da Loja
# desatualizado derruba a fonte msstore inteira. A Loja atualiza os dois por
# fora, sem depender deles.
#
# Roda no Windows PowerShell 5.1. Reporta o andamento em linhas "SLT-..."
# que o AutoInstall interpreta (LojaMicrosoft.cs, classe LojaAppInstaller):
#   SLT-INICIO | SLT-INFO:msg | SLT-ALVO:familia | SLT-PROG:pct:familia
#   SLT-OK:familia | SLT-SEM:familia | SLT-ERRO:familia:estado
#   SLT-FIM:atualizados:erros | SLT-FALHA:mensagem
# (linhas do protocolo sao ASCII puro: a saida redirecionada usa a codepage
# OEM e acentos virariam lixo)
#
# Este arquivo e embutido no exe como recurso e extraido para %TEMP% na hora.

$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallManager, Windows.ApplicationModel.Store.Preview.InstallControl, ContentType = WindowsRuntime]
    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]

    function Await($op, $tipo) {
        $t = $asTaskGeneric.MakeGenericMethod($tipo).Invoke($null, @($op))
        $null = $t.Wait(-1)
        return $t.Result
    }

    Write-Output 'SLT-INICIO'
    $mgr = New-Object -TypeName Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallManager
    $tipoItem = [Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallItem]

    $alvos = @(
        'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe',
        'Microsoft.WindowsStore_8wekyb3d8bbwe'
    )

    $atualizados = 0
    $erros = 0
    # Curto de proposito: isto e so o preparo. Se a Loja demorar demais, o
    # AutoInstall segue com o winget que tem - melhor do que travar aqui.
    $limite = (Get-Date).AddMinutes(12)

    foreach ($familia in $alvos) {
        Write-Output ('SLT-ALVO:' + $familia)
        $item = $null
        try {
            $item = Await ($mgr.UpdateAppByPackageFamilyNameAsync($familia)) $tipoItem
        }
        catch {
            # Pacote nao instalado pela Loja, ou API indisponivel nesta edicao.
            Write-Output ('SLT-ERRO:' + $familia + ':' + $_.Exception.GetType().Name)
            $erros++
            continue
        }

        if ($null -eq $item) {
            Write-Output ('SLT-SEM:' + $familia)
            continue
        }

        # A fila e da Loja: aqui so acompanhamos ate terminar.
        $ultimo = -1
        while ($true) {
            $st = $null
            try { $st = $item.GetCurrentStatus() } catch { }
            if ($null -eq $st) {
                Write-Output ('SLT-OK:' + $familia)
                $atualizados++
                break
            }
            $es = [string]$st.InstallState
            if ($es -eq 'Completed') {
                Write-Output ('SLT-OK:' + $familia)
                $atualizados++
                break
            }
            if ($es -eq 'Error' -or $es -eq 'Canceled') {
                Write-Output ('SLT-ERRO:' + $familia + ':' + $es)
                $erros++
                break
            }
            $pct = [int]$st.PercentComplete
            if ($pct -ne $ultimo) {
                $ultimo = $pct
                Write-Output ('SLT-PROG:' + $pct + ':' + $familia)
            }
            if ((Get-Date) -gt $limite) {
                Write-Output ('SLT-ERRO:' + $familia + ':Tempo')
                $erros++
                break
            }
            Start-Sleep -Seconds 2
        }
    }

    Write-Output ('SLT-FIM:' + $atualizados + ':' + $erros)
    exit 0
}
catch {
    Write-Output ('SLT-FALHA:' + $_.Exception.Message)
    exit 1
}
