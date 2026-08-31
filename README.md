# AutoInstall — Smells Like Tech

Aplicação para automatizar o pós-formatação de computadores com Windows 10/11.

O técnico escolhe o que deseja executar em uma única tela e o programa cuida de atualizações, instalações, reinicializações e retomada do processo.

![Tela de escolha](docs/tela-1-escolha.png)

## O que automatiza

### Windows Update

- atualizações obrigatórias e opcionais;
- drivers;
- Microsoft Update;
- instalação de atualizações exclusivas separadamente;
- reinicialização automática quando necessária;
- retomada do ponto salvo após o logon;
- novas rodadas até não restarem atualizações ou atingir o limite de segurança.

### Instalação de programas

Catálogo por categorias com múltiplas estratégias de instalação:

- winget;
- Microsoft Store;
- instalador oficial;
- script oficial do fabricante;
- Office Deployment Tool.

A instalação é validada pelo estado real do sistema, e não apenas pelo código de saída do instalador.

### Atualização do software instalado

- `winget upgrade --all`;
- atualizações dos aplicativos da Microsoft Store;
- múltiplas passadas quando necessário.

## Stack

C# · .NET Framework · WinForms · Windows Update COM API · winget · Microsoft Store API · PowerShell

## Fluxo de execução

```text
Escolha
   │
   ├── Windows Update
   ├── Instalar programas
   └── Atualizar programas existentes
             │
             ▼
       execução automática
             │
       reinicia se necessário
             │
       retoma do estado salvo
             │
             ▼
          relatório
```

| Execução | Relatório |
| --- | --- |
| ![Progresso](docs/tela-3-progresso.png) | ![Relatório](docs/tela-4-relatorio.png) |

## Algumas decisões técnicas

### Atualização sem interromper operações críticas

Pausar ou parar é respeitado entre itens. Um instalador em execução não é encerrado abruptamente.

### Persistência entre reinicializações

O estado da execução é salvo antes do reboot. Uma tarefa agendada reabre a aplicação e o processo continua do ponto correto.

### Office pelo ODT

O Microsoft 365 é instalado pelo Office Deployment Tool com configuração explícita e a presença real do Click-to-Run é verificada ao final.

### Estratégias de fallback

Quando uma via de instalação falha, o catálogo pode tentar outra fonte oficial definida para aquele programa.

## Como compilar

```bat
build.bat
```

O projeto utiliza o compilador do .NET Framework presente no Windows e gera um executável para uso em bancada.

## Uso

```text
AutoInstall.exe
AutoInstall.exe --resume
```

`--resume` é usado pela retomada automática após reinicialização.

## Autor

**Maicon Nunes** — Smells Like Tech Informática  
[GitHub](https://github.com/mikerock12) · [Site](https://www.smellsliketech.com.br)
