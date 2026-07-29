# AutoInstall Pós-Formatação — Smells Like Tech Informática

Software para rodar **logo depois de formatar** uma máquina com Windows 10/11.
Ele deixa o Windows 100% atualizado (com reinicializações automáticas), instala
os programas essenciais e entrega um relatório final de tudo que foi feito.

Criado por Maicon Nunes · [www.smellsliketech.com.br](https://www.smellsliketech.com.br) · [@smellsliketechinfo](https://www.instagram.com/smellsliketechinfo)

## O que ele faz, na ordem

1. **Abertura** — o Guaxinim da Smells Like Tech surge em *fade in* de 5 s
   (e sai em *fade out* de 5 s). O rodapé com o crédito e o site clicável fica
   fixo na janela do início ao fim.
2. **Energia no máximo (temporário)** — duplica o plano *Desempenho Máximo*
   (Ultimate Performance; cai para *Alto Desempenho* se a edição não tiver),
   ativa e zera todos os timeouts: nunca desligar a tela, nunca suspender,
   nunca hibernar, nunca desligar os discos.
3. **Windows Update completo** — busca **todas** as atualizações pendentes,
   **incluindo opcionais e drivers** (e o Microsoft Update, que traz Office
   etc.). A tela mostra: quantas foram encontradas, quantas são
   opcionais/drivers, "baixando/instalando X de Y" e **percentual real** de
   download e de instalação (callbacks nativos do agente do Windows Update).
4. **Reinicia e retoma sozinho** — após instalar, pede para reiniciar (com
   contagem regressiva de 60 s). Uma tarefa agendada reabre o programa no
   logon, que **procura de novo e instala de novo**, repetindo o ciclo até
   não sobrar nenhuma atualização (limite de segurança: 8 reinicializações).
5. **Programas essenciais via winget** — Google Chrome, Adobe Acrobat Reader,
   WinRAR, K-Lite Codec Pack Standard (codecs de áudio/vídeo atualizados +
   player leve MPC-HC) e Microsoft 365 (Office). Se o winget ainda não existir
   (comum após formatar), o programa baixa e registra o App Installer sozinho.
6. **Atualização geral de aplicativos** — em duas frentes:
   - **Apps da Microsoft Store**: aciona a mesma API do botão "Atualizar
     todos" da Loja (`AppInstallManager.SearchForAllUpdatesAsync`, via
     `tools\loja-update.ps1` embutido no exe), com progresso por app e no
     total; se a API falhar, abre a página de downloads da Loja como
     fallback. (O winget não cobre bem os apps UWP — visto em campo.)
   - **Programas de desktop**: `winget upgrade --all`, repetido em passadas
     até não restar nenhuma atualização pendente (máx. 5 passadas).
7. **Tela final** — o Guaxinim de novo + relatório completo: todas as
   atualizações de cada rodada, todos os programas com versão, e o registro
   das passadas de upgrade. Restaura o plano de energia **Equilibrado
   (recomendado)** e apaga o plano temporário. Botão *Fechar* e convite para
   o site e o Instagram.

## Como compilar

```bat
build.bat
```

Usa só o `csc.exe` do .NET Framework 4.x que vem com o Windows — **não precisa
de Visual Studio**. Na primeira compilação, o script gera automaticamente:

- `lib\Interop.WUApiLib.dll` — interop da API COM do Windows Update, criada a
  partir do `wuapi.dll` do próprio sistema (`tools\make-interop.ps1`);
- `icon.ico` — ícone gerado do `Guaxinim.jpg` (`tools\make-icon.ps1`).

Saída: **`bin\AutoInstall.exe`** — arquivo único (imagem e interop embutidas),
pronto para o pendrive. Requer administrador (manifesto).

Assinatura digital opcional: defina `SLT_CERT_SUBJECT` antes de rodar o build
(mesmo esquema do Diagnóstico & Reparo).

## Uso

| Comando | O que faz |
|---|---|
| `AutoInstall.exe` | Execução normal (começa do zero ou retoma a fase salva) |
| `AutoInstall.exe --resume` | Usado pela tarefa agendada após reiniciar (splash curto) |
| `AutoInstall.exe --preview` | **Só demonstra as telas** com dados fictícios — não mexe em nada |

## Estado e logs

- `C:\ProgramData\SmellsLikeTech\AutoInstall\estado.json` — fase atual,
  rodadas de update, programas e versões (é o que permite retomar após cada
  reinicialização). Apague para recomeçar do zero.
- `C:\ProgramData\SmellsLikeTech\AutoInstall\log.txt` — log corrido de tudo.
- Tarefa agendada de retomada: `SmellsLikeTech AutoInstall` (removida
  automaticamente ao final; fallback: chave RunOnce).

## Estrutura

| Arquivo | Papel |
|---|---|
| `src/MainForm.cs` | Janela única, rodapé fixo, splash e o fluxo de fases |
| `src/Telas.cs` | Telas de progresso, reinicialização e relatório final |
| `src/Controles.cs` | Tema escuro/laranja, imagem com fade, barra de progresso |
| `src/AtualizadorWindows.cs` | Windows Update via COM com progresso real |
| `src/InstaladorApps.cs` | winget: bootstrap, instalação e upgrade geral |
| `src/Energia.cs` | Planos de energia (powercfg) |
| `src/TarefaInicio.cs` | Tarefa agendada de retomada (schtasks + RunOnce) |
| `src/Estado.cs` | Persistência em JSON no ProgramData |
| `src/Executor.cs` | Execução de processos ocultos com captura de saída |
