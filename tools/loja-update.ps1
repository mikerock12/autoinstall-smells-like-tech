# Atualiza TODOS os apps da Microsoft Store pela mesma via do botao
# "Atualizar todos" da Loja: API WinRT AppInstallManager.SearchForAllUpdatesAsync.
# Roda no Windows PowerShell 5.1. Reporta o andamento em linhas "SLT-..."
# que o AutoInstall interpreta (LojaMicrosoft.cs):
#   SLT-INICIO | SLT-INFO:msg | SLT-TOTAL:n | SLT-PROG:pct:feitos:total
#   SLT-OK:app | SLT-ERRO:app:estado | SLT-TEMPO | SLT-FIM:feitos:erros
#   SLT-FALHA:mensagem
# (linhas do protocolo sao ASCII puro: a saida redirecionada usa a codepage
# OEM e acentos virariam lixo)
#
# A verificacao roda ate 3 vezes: o catalogo da Loja as vezes so lista uma
# atualizacao na SEGUNDA olhada (visto em campo - a primeira devolveu vazio e
# minutos depois havia atualizacao pendente). Vazio na primeira -> espera 30s
# e olha de novo; achou e instalou -> olha mais uma vez para pegar retardatarias.
#
# Este arquivo e embutido no exe como recurso e extraido para %TEMP% na hora.

$ErrorActionPreference = 'Stop'
try {
    # Gatilho extra de varredura via ponte MDM (funciona so em alguns cenarios;
    # falha e ignorada de proposito)
    try {
        Get-CimInstance -Namespace 'Root\cimv2\mdm\dmmap' `
            -ClassName 'MDM_EnterpriseModernAppManagement_AppManagement01' -ErrorAction Stop |
            Invoke-CimMethod -MethodName UpdateScanMethod -ErrorAction Stop | Out-Null
    } catch { }

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
    $tipoLista = [System.Collections.Generic.IReadOnlyList[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallItem]]

    $limite = (Get-Date).AddMinutes(60)
    $totalGeral = 0
    $feitosGeral = 0
    $errosGeral = 0
    $estourouTempo = $false

    for ($verificacao = 1; $verificacao -le 3; $verificacao++) {
        Write-Output ('SLT-INFO:Verificacao ' + $verificacao + ': consultando o catalogo da Loja...')
        $itens = Await ($mgr.SearchForAllUpdatesAsync()) $tipoLista
        if ($null -eq $itens) { $itens = @() }

        if ($itens.Count -eq 0) {
            if ($totalGeral -eq 0 -and $verificacao -eq 1) {
                Write-Output 'SLT-INFO:Nada na primeira verificacao; conferindo de novo em 30 segundos...'
                Start-Sleep -Seconds 30
                continue
            }
            Write-Output 'SLT-INFO:Nova verificacao nao achou mais nada - Loja em dia.'
            break
        }

        $totalGeral += $itens.Count
        Write-Output ('SLT-TOTAL:' + $totalGeral)

        $vistos = @{}
        $feitosAntes = $feitosGeral
        while ($true) {
            $soma = 0.0
            $feitos = 0
            $erros = 0
            foreach ($item in $itens) {
                $nome = ''
                try { $nome = $item.PackageFamilyName } catch { }
                $st = $null
                try { $st = $item.GetCurrentStatus() } catch { }
                if ($null -eq $st) {
                    # item saiu da fila (normalmente: concluido)
                    $soma += 100
                    $feitos++
                    if (-not $vistos[$nome]) { $vistos[$nome] = 1; Write-Output ('SLT-OK:' + $nome) }
                    continue
                }
                $es = [string]$st.InstallState
                if ($es -eq 'Completed') {
                    $soma += 100
                    $feitos++
                    if (-not $vistos[$nome]) { $vistos[$nome] = 1; Write-Output ('SLT-OK:' + $nome) }
                } elseif ($es -eq 'Error' -or $es -eq 'Canceled') {
                    $soma += 100
                    $feitos++
                    $erros++
                    if (-not $vistos[$nome]) { $vistos[$nome] = 1; Write-Output ('SLT-ERRO:' + $nome + ':' + $es) }
                } else {
                    $soma += [double]$st.PercentComplete
                }
            }
            $feitosGeral = $feitosAntes + $feitos
            $pct = [int]((($feitosAntes * 100.0) + $soma) / $totalGeral)
            Write-Output ('SLT-PROG:' + $pct + ':' + $feitosGeral + ':' + $totalGeral)
            if ($feitos -ge $itens.Count) { $errosGeral += $erros; break }
            if ((Get-Date) -gt $limite) {
                Write-Output 'SLT-TEMPO'
                $errosGeral += $erros
                $estourouTempo = $true
                break
            }
            Start-Sleep -Seconds 3
        }
        if ($estourouTempo) { break }
    }

    if ($totalGeral -eq 0) { Write-Output 'SLT-TOTAL:0' }
    Write-Output ('SLT-FIM:' + $feitosGeral + ':' + $errosGeral)
    exit 0
}
catch {
    Write-Output ('SLT-FALHA:' + $_.Exception.Message)
    exit 1
}
