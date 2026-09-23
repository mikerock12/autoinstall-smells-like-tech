# Verificacao somente leitura: consulta IDs no repositorio oficial do winget.
# Requer GitHub CLI autenticado e acesso a rede. Nao instala aplicativos.
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$fonte = Get-Content -LiteralPath (Join-Path $raiz 'src\Catalogo.cs') -Raw
$ids = [regex]::Matches($fonte, 'Winget\("([^"]+)"\)') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$falhas = @()
foreach ($id in $ids) {
    $caminho = 'manifests/' + $id.Substring(0, 1).ToLowerInvariant() + '/' + $id.Replace('.', '/')
    $resposta = & gh api "repos/microsoft/winget-pkgs/contents/$caminho" --jq '.[0].path' 2>&1
    if ($LASTEXITCODE -eq 0) { Write-Output "OK $id" }
    else { $falhas += $id; Write-Output "FALHA $id : $resposta" }
}
Write-Output "Conferidos: $($ids.Count); falhas: $($falhas.Count)."
if ($falhas.Count -gt 0) { exit 1 }
