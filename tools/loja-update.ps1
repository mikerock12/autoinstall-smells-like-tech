# Atualiza TODOS os apps da Microsoft Store pela mesma via do botao
# "Atualizar todos" da Loja: API WinRT AppInstallManager.SearchForAllUpdatesAsync.
# Roda no Windows PowerShell 5.1. Reporta o andamento em linhas "SLT-..."
# que o AutoInstall interpreta (LojaMicrosoft.cs):
#   SLT-INICIO | SLT-TOTAL:n | SLT-PROG:pct:feitos:total
#   SLT-OK:app | SLT-ERRO:app:estado | SLT-TEMPO | SLT-FIM:feitos:erros
#   SLT-FALHA:mensagem
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
    $itens = Await ($mgr.SearchForAllUpdatesAsync()) $tipoLista
    if ($null -eq $itens) { $itens = @() }
    Write-Output ('SLT-TOTAL:' + $itens.Count)
    if ($itens.Count -eq 0) { Write-Output 'SLT-FIM:0:0'; exit 0 }

    $vistos = @{}
    $limite = (Get-Date).AddMinutes(60)
    $feitos = 0
    $erros = 0
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
        Write-Output ('SLT-PROG:' + [int]($soma / $itens.Count) + ':' + $feitos + ':' + $itens.Count)
        if ($feitos -ge $itens.Count) { break }
        if ((Get-Date) -gt $limite) { Write-Output 'SLT-TEMPO'; break }
        Start-Sleep -Seconds 3
    }
    Write-Output ('SLT-FIM:' + $feitos + ':' + $erros)
    exit 0
}
catch {
    Write-Output ('SLT-FALHA:' + $_.Exception.Message)
    exit 1
}
