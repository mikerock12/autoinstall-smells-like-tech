@echo off
rem Compila o AutoInstall Pos-Formatacao usando o csc.exe do .NET Framework 4.x,
rem presente em qualquer Windows 10/11 (nao precisa de Visual Studio).
rem /codepage:65001 -> os fontes sao UTF-8 (acentos corretos no executavel).
rem /link           -> embute a interop do Windows Update no exe (arquivo unico).

setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set PS=%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe

rem Interop do Windows Update (gerada uma unica vez a partir do wuapi.dll do sistema)
if not exist lib\Interop.WUApiLib.dll (
  "%PS%" -NoProfile -ExecutionPolicy Bypass -File tools\make-interop.ps1
)
if not exist lib\Interop.WUApiLib.dll (
  echo *** FALHA: lib\Interop.WUApiLib.dll nao foi gerada ***
  exit /b 1
)

rem Logo de exibicao: sangria de cor nas bordas (tira a franja verde do croma
rem ao redimensionar) e dissolucao das bordas cortadas pela moldura da imagem.
rem Gerado do recorte original, que fica intacto em assets\guaxinim-origem.png.
if not exist assets\guaxinim.png (
  "%PS%" -NoProfile -ExecutionPolicy Bypass -File tools\preparar-logo.ps1
)
if not exist assets\guaxinim.png (
  echo *** FALHA: coloque o recorte em assets\guaxinim-origem.png ***
  exit /b 1
)

rem Icone (gerado do recorte)
if not exist icon.ico (
  "%PS%" -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
)

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /codepage:65001 /out:bin\AutoInstall.exe ^
  /win32manifest:app.manifest ^
  /win32icon:icon.ico ^
  /resource:assets\guaxinim.png,guaxinim.png ^
  /resource:tools\loja-update.ps1,loja-update.ps1 ^
  /resource:tools\preparar-instaladores.ps1,preparar-instaladores.ps1 ^
  /link:lib\Interop.WUApiLib.dll ^
  /r:System.Core.dll ^
  /r:System.Web.Extensions.dll ^
  /recurse:src\*.cs

if %errorlevel% neq 0 (
  echo.
  echo *** FALHA NA COMPILACAO ***
  exit /b 1
)
echo.
echo OK: bin\AutoInstall.exe gerado (arquivo unico, pronto para o pendrive).

rem ------------------------------------------------------------------
rem Assinatura digital (Authenticode) - OPCIONAL.
rem So roda se o certificado estiver configurado nesta variavel; caso
rem contrario o build termina normalmente, apenas sem assinar.
rem
rem   set SLT_CERT_SUBJECT=Smells Like Tech Informatica
rem ------------------------------------------------------------------
if "%SLT_CERT_SUBJECT%"=="" goto :fim

set SIGNTOOL=
for %%p in (signtool.exe) do if not "%%~$PATH:p"=="" set SIGNTOOL=%%~$PATH:p
if "%SIGNTOOL%"=="" (
  for /f "delims=" %%f in ('dir /b /s "%ProgramFiles(x86)%\Windows Kits\10\bin\*\x64\signtool.exe" 2^>nul') do set SIGNTOOL=%%f
)
if "%SIGNTOOL%"=="" (
  echo AVISO: signtool.exe nao encontrado - executavel NAO assinado.
  goto :fim
)

echo Assinando com o certificado "%SLT_CERT_SUBJECT%"...
"%SIGNTOOL%" sign /n "%SLT_CERT_SUBJECT%" /fd SHA256 ^
  /tr http://timestamp.digicert.com /td SHA256 ^
  /d "AutoInstall Pos-Formatacao" ^
  /du "https://www.smellsliketech.com.br" ^
  bin\AutoInstall.exe
if %errorlevel% neq 0 (
  echo AVISO: falha ao assinar - executavel gerado, porem NAO assinado.
) else (
  echo OK: executavel assinado digitalmente.
)

:fim
endlocal
