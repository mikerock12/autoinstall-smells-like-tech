# Windows Update: diagnóstico e validação (2.3)

## O que causava a espera sem resposta

A implementação anterior chamava `IUpdateSearcher.Search` de forma síncrona,
sem timeout nem cancelamento. Antes da busca, `AddService2` usava o sinalizador
de registro online imediato, introduzindo outra chamada de rede síncrona.
O log local mostrou uma busca sem conclusão registrada e uma nova execução
aproximadamente 11 minutos depois que voltou a avançar. O log antigo não
permite distinguir qual das duas chamadas ficou retida naquela ocasião.

Download e instalação dependiam exclusivamente de callbacks que sinalizavam
um `ManualResetEvent`. A instalação usava `WaitOne()` sem limite. Além disso,
as classes de callback eram privadas em um assembly com `ComVisible(false)`.
Não havia consulta de `IsCompleted` como alternativa. Esses caminhos podiam
esperar indefinidamente sem refletir o estado real do agente.

Ao parar um download, o código esperava 20 segundos e chamava `EndDownload`
mesmo sem confirmação do término. Falhas dessa chamada eram ignoradas e a
interface anunciava download concluído. A execução também reiniciava depois
de cada lote, sem consultar o sinalizador de reinicialização.

## Correção

- Registro do Microsoft Update permite registro pendente, sem exigir rede
  síncrona (`AddService2`, flags 1 + 4).
- Busca usa `BeginSearch`, consulta `IsCompleted` e tem limite de 10 minutos.
- Downloads consultam `GetProgress`; bytes recebidos renovam o prazo de
  15 minutos sem avanço, inclusive quando a porcentagem permanece igual.
- A parada cancela busca/download. O agente tem 60 segundos para confirmar
  o término; só então uma nova tentativa pode começar.
- `EndSearch`, `EndDownload`, `EndInstall` e `CleanUp` só são chamados após
  conclusão confirmada. Sem confirmação, a aplicação suspende a sequência.
- Callbacks são públicos e explicitamente visíveis ao COM. Não acessam a
  interface nem retêm o job. O acompanhamento usa polling independente deles.
- Instalações não são abortadas. O progresso e o tempo de espera são exibidos;
  o pedido de parar é respeitado antes do próximo lote/exclusiva.
- Apenas itens realmente baixados entram na instalação. Códigos HRESULT,
  resultados por item e avisos são registrados no log/relatório.
- Reinício exige indicação do agente; sem isso, uma rodada com instalações
  bem-sucedidas segue para nova busca. Rodada sem sucesso não entra em loop.
- O estado de versão 2 continua compatível e todas as chaves antigas do
  catálogo foram preservadas.

## Mensagens de espera e contagem (2.3.1)

Durante uma instalação, o percentual de um item pode ficar parado enquanto
Windows conclui etapas internas. A versão 2.3.0 repetia a cada 30 segundos
"sem mudança de progresso" na tela e no log, dando a impressão de erro mesmo
quando a instalação continuava normalmente. Esse aviso não era um resultado
de falha do agente.

A tela agora informa "Instalação em andamento" e o tempo decorrido. O aviso
periódico não preenche o log; cancelamentos, timeouts reais e resultados por
item continuam registrados. Enquanto um cancelamento está pendente, o aviso
de cancelamento permanece na tela.

A fila de instalação usa somente os itens baixados. A numeração e o percentual
agora seguem essa fila, mantendo o título da atualização original. Por exemplo,
35 itens encontrados e 25 baixados mostram uma posição entre 1 e 25, incluindo
as exclusivas instaladas separadamente, em vez de "32 de 35" numa etapa de 25.

## Resultados parciais e `0x80248007` (2.4.1)

Uma captura de bancada mostrou `Resultado do download: orcSucceeded` seguido de
três erros `0x80248007` e nenhuma instalação. A Microsoft define esse HRESULT
como `WU_E_DS_NODATA`: a informação solicitada não está no repositório do
Windows Update. A captura não distingue se a exceção veio de `GetUpdateResult`
ou de `IsDownloaded`; a implementação anterior permitia que ambas interrompessem
o processamento do lote depois que o download geral já havia terminado.

A confirmação do download e a leitura do resultado detalhado agora são isoladas
por item. Se o detalhe estiver ausente mas `IsDownloaded` confirmar o conteúdo,
a atualização continua apta a instalar. Se o download não puder ser confirmado,
o item fica fora da fila e o relatório conserva o título, o resultado disponível
e o HRESULT. Uma exceção nessas consultas não inicia três novos downloads do
lote inteiro nem descarta os itens válidos. A leitura dos resultados da
instalação recebeu o mesmo isolamento, sem declarar sucesso quando o resultado
não pode ser lido.

Nenhum cache do Windows Update é apagado e nenhum serviço é reiniciado por essa
correção. As causas externas de um download realmente malsucedido continuam
exigindo diagnóstico no Windows; a alteração corrige o tratamento dos resultados
parciais pela aplicação.

## Verificação automatizada

Além dos cenários anteriores, os testes reproduzem `0x80248007` na leitura do
resultado e na confirmação do download, preservam um download já confirmado,
mantêm pendente um item não confirmado e continuam o registro da instalação
seguinte. São 47 verificações na versão 2.4.1.

`tools/testar.ps1` usa relógio simulado para reproduzir a ausência de callbacks,
timeout, parada durante a busca, cancelamento que não termina, bytes avançando,
download que deixa de avançar e instalação demorada. Confere também a exposição
COM dos callbacks, compatibilidade do estado, ausência de chaves duplicadas,
seleção preservada nos filtros e relatório de falha de consulta. Também reproduz
uma instalação com percentual parado por minutos, verifica que o log permanece
sem avisos falsos e confere a contagem com downloads parciais e exclusivas.

`-WindowsUpdateLocal` acrescenta uma busca real no cache do Windows Update,
sem registrar serviços, aceitar licenças, baixar ou instalar atualizações.
Essa consulta foi concluída com `orcSucceeded` na validação da alteração.

## Validação em bancada

Em uma máquina de teste com atualizações pendentes, executar primeiro apenas
Windows Update. Conferir busca, download, instalação e retomada após um reboot
solicitado pelo agente. Repetir com parada durante busca/download e com rede
indisponível. Verificar que falhas não aparecem como sucesso, que o botão Parar
não encerra uma instalação ativa e que não há instalações sobrepostas.

Essas instalações e reinicializações reais não foram executadas na estação
de desenvolvimento. Uma instalação realmente lenta continua sendo aguardada;
a aplicação não encerra processos ou serviços do Windows para forçar seu fim.

Log: `%ProgramData%\SmellsLikeTech\AutoInstall\log.txt`.
Estado: `%ProgramData%\SmellsLikeTech\AutoInstall\estado.json`.
Se busca/download atingir o limite sem avanço, o log registra o cancelamento. Aguarde a operação do Windows terminar antes de reabrir para retomar.

## Referências

- [Diretrizes da Microsoft para operações WUA assíncronas](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/guidelines-for-asynchronous-wua-operations)
- [Registro de serviços com AddService2](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdateservicemanager2-addservice2)
- [IsCompleted de uma instalação](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iinstallationjob-get_iscompleted)
- [CleanUp aguarda a conclusão antes de liberar callbacks](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iinstallationjob-cleanup)
