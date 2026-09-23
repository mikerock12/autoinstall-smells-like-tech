param([switch]$WindowsUpdateLocal)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $raiz
$compilador = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilador)) {
    $compilador = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
New-Item -ItemType Directory -Path bin -Force | Out-Null
& $compilador /nologo /target:exe /codepage:65001 /main:RegressionTests /out:bin\RegressionTests.exe /link:lib\Interop.WUApiLib.dll /r:System.Core.dll /r:System.Web.Extensions.dll /recurse:src\*.cs tests\RegressionTests.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($WindowsUpdateLocal) { & .\bin\RegressionTests.exe --wu-smoke }
else { & .\bin\RegressionTests.exe }
exit $LASTEXITCODE
