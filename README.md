# AutoInstall Pós-Formatação — Smells Like Tech Informática

Software para rodar **logo depois de formatar** uma máquina com Windows 10/11.
Ele deixa o Windows 100% atualizado (com reinicializações automáticas), instala
os programas essenciais e entrega um relatório final de tudo que foi feito.

Criado por Maicon Nunes · [www.smellsliketech.com.br](https://www.smellsliketech.com.br) · [@smellsliketechinfo](https://www.instagram.com/smellsliketechinfo)

## O que ele faz, na ordem

1. **Abertura** — **somente o Guaxinim recortado** (sem moldura nem retângulo
   de janela) aparece sobre a área de trabalho em *fade in* de 5 s e sai em
   *fade out* de 5 s, com o crédito e o site logo abaixo dele. Um clique pula
   a abertura; clicar no site abre o navegador. Só depois disso a janela do
   programa abre e o processo começa. O rodapé com o crédito e o site clicável
   fica fixo na janela do início ao fim.
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
   WinRAR e K-Lite Codec Pack Standard (codecs de áudio/vídeo atualizados +
   player leve MPC-HC). Se o winget ainda não existir (comum após formatar),
   o programa baixa e registra o App Installer sozinho.
6. **Office 365** — instalado pelo **Office Deployment Tool oficial** com XML
   de configuração explícito (pt-BR, 64 bits), e no fim **conferido no
   registro** do Click-to-Run. No começo da execução o programa pergunta uma
   única vez qual edição instalar — Personal/Família (padrão), Apps for
   enterprise, ou nenhuma — porque a edição errada instala mas não ativa.
   Sem resposta em 30 s, segue no padrão.

   > Por que não pelo winget: o pacote `Microsoft.Office` baixa o
   > `setup.exe` do ODT, que **sem um XML não instala nada** — e ainda assim
   > o winget devolve sucesso. Era essa a falha vista em campo (o app dizia
   > "instalado" e não havia Word nem Excel na máquina).
7. **Atualização geral de aplicativos** — em duas frentes:
   - **Apps da Microsoft Store**: aciona a mesma API do botão "Atualizar
     todos" da Loja (`AppInstallManager.SearchForAllUpdatesAsync`, via
     `tools\loja-update.ps1` embutido no exe), com progresso por app e no
     total; se a API falhar, abre a página de downloads da Loja como
     fallback. (O winget não cobre bem os apps UWP — visto em campo.)
   - **Programas de desktop**: `winget upgrade --all`, repetido em passadas
     até não restar nenhuma atualização pendente (máx. 5 passadas).
8. **Tela final** — o Guaxinim de novo + relatório completo: todas as
   atualizações de cada rodada, todos os programas com versão, e o registro
   das passadas de upgrade. Restaura o plano de energia **Equilibrado
   (recomendado)** e apaga o plano temporário. Botão *Fechar*, convite para
   o site e o Instagram, e um link discreto **"Refazer todas as etapas"**
   (com confirmação) para rodar tudo de novo na mesma máquina — útil quando
   o programa reabre já concluído e você quer reconferir alguma etapa.

## Pausar e parar

A tela de progresso tem os botões **Pausar/Continuar** e **Parar**. Como quase
tudo aqui é instalação, os dois valem **no fim do item atual**: uma atualização
ou um instalador em andamento nunca é cortado no meio (é assim que se quebra um
Windows). O download do Windows Update, esse sim, é abortado na hora — o que já
baixou fica em cache. Ao parar, o programa restaura o plano de energia, remove
a tarefa de retomada e mostra o relatório do que deu tempo de concluir.

## Como compilar

```bat
build.bat
```

Usa só o `csc.exe` do .NET Framework 4.x que vem com o Windows — **não precisa
de Visual Studio**. Na primeira compilação, o script gera automaticamente:

- `lib\Interop.WUApiLib.dll` — interop da API COM do Windows Update, criada a
  partir do `wuapi.dll` do próprio sistema (`tools\make-interop.ps1`);
- `assets\guaxinim.png` — o Guaxinim recortado do `Guaxinim.jpg` com fundo
  transparente (`tools\make-guaxinim-png.ps1`);
- `icon.ico` — ícone gerado do recorte (`tools\make-icon.ps1`).

### Sobre o recorte do Guaxinim

O capuz preto sobre fundo escuro derruba qualquer recorte por limiar simples —
medindo a foto, o capuz tem brilho mediano **15** e o fundo colado nele, **29**
(o fundo é mais *claro* que o capuz), e a tonalidade também não separa os dois.
Por isso `tools\make-guaxinim-png.ps1` combina: polígono traçado à mão para a
silhueta do corpo, inundação por faixa de brilho para limpar o fundo restante,
*matting* por luminância na região de pelo (é o que preserva bigodes e tufos
das orelhas), remoção de ilhas soltas e dissolução das bordas que a moldura da
foto corta. Alterou a arte? Apague `assets\guaxinim.png` e ajuste as constantes
no topo do script (`POLIGONO`, `LINHA_PELO`, `BORDA_CORREDOR`).

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
| `src/SplashGuaxinim.cs` | Abertura em janela sem moldura (UpdateLayeredWindow) |
| `src/MainForm.cs` | Janela única, rodapé fixo e o fluxo de fases |
| `src/Telas.cs` | Telas de progresso (pausar/parar), reinício e relatório |
| `src/Controles.cs` | Tema escuro/laranja, imagem com fade, barra de progresso |
| `src/ControleExecucao.cs` | Pausa e parada com pontos de checagem seguros |
| `src/AtualizadorWindows.cs` | Windows Update via COM com progresso real |
| `src/InstaladorApps.cs` | winget: bootstrap, instalação e upgrade geral |
| `src/InstaladorOffice.cs` | Office pelo ODT oficial + conferência no registro |
| `src/EscolhaOffice.cs` | Diálogo único da edição do Office (30 s de espera) |
| `src/LojaMicrosoft.cs` | Apps da Store via API AppInstallManager |
| `src/Energia.cs` | Planos de energia (powercfg) |
| `src/TarefaInicio.cs` | Tarefa agendada de retomada (schtasks + RunOnce) |
| `src/Estado.cs` | Persistência em JSON no ProgramData |
| `src/Executor.cs` | Execução de processos ocultos com captura de saída |
