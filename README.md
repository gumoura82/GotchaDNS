# GotchaDNS

**DNS Sinkhole nativo para Windows** — bloqueia anúncios, telemetria e malware no nível do sistema operacional, antes que qualquer conexão TCP ocorra. Protege navegadores, jogos e processos em background sem causar lentidão perceptível.

## O que é

O GotchaDNS sequestra o tráfego DNS da sua máquina na porta UDP/53 e aplica regras de bloqueio em memória com foco em zero-allocation. Domínios permitidos são encaminhados normalmente à Cloudflare (`1.1.1.1`). Domínios bloqueados recebem uma resposta DNS forjada com `0.0.0.0` — a conexão morre instantaneamente, sem timeout.

A interface desktop (Photino + TypeScript) consome uma API mínima exposta pelo motor para exibir logs em tempo real e gerenciar liberações manuais.

## Arquitetura

```
GotchaDNS/
├── GotchaDNS.Engine/     # Motor de rede (.NET 8, BackgroundService)
└── GotchaDNS.UI/         # Interface desktop (Photino.NET + Vite/TypeScript)
    └── frontend/         # App web compilado e servido pelo Photino
```

### Fluxo de um pacote DNS

1. `DnsMotorService` abre um socket UDP em `0.0.0.0:53` e aguarda pacotes.
2. Para cada pacote recebido:
   - Extrai o nome de domínio via `ParseDomainName` (operações em `Span<byte>`, sem alocação).
   - Filtra ruído local: ignora domínios `.lan`, `.local` e nomes vazios.
   - Consulta as regras de bloqueio em memória:
     - **Bloqueado** → constrói resposta DNS forjada (`0.0.0.0`) e devolve ao cliente. Enfileira um `DnsLogEntry` em `BlockedLogs`.
     - **Permitido** → faz forwarding para `1.1.1.1:53` via `UdpClient` e retorna a resposta real ao cliente.
3. A UI consome `/api/logs` periodicamente e exibe os bloqueios na tela.

### Componentes principais

| Componente | Responsabilidade |
|---|---|
| `DnsMotorService` | BackgroundService: socket UDP, parse, filtro, sinkhole, forwarding |
| `DnsLogEntry` | Record com `id`, `timestamp`, `domain`, `clientIp`, `reason` |
| `BlockedLogs` | `ConcurrentQueue<DnsLogEntry>` em memória (limite ~50 entradas) |
| `WhitelistRequest` | Record com `domain` para o `POST /api/whitelist` |
| `GotchaDNS.UI` | Janela Photino que carrega o `index.html` do build Vite |

## Endpoints HTTP

A API mínima roda no mesmo processo do motor (ASP.NET Core Minimal APIs) na porta `5005`.

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/logs` | Retorna os logs de domínios bloqueados (a UI inverte a lista para mostrar os mais recentes primeiro) |
| `POST` | `/api/whitelist` | Aceita `{ "domain": "dominio.tld" }` — ponto de integração para liberar domínios |

## Status atual

### O que funciona

- Interceptação de pacotes DNS na porta UDP/53 com parsing binário via `Span<byte>`
- Filtragem de ruído de rede local (`.lan`, `.local`)
- Forwarding transparente para Cloudflare com devolução da resposta ao cliente
- **Sinkhole ativo**: domínios bloqueados recebem resposta `0.0.0.0` imediata (sem timeout)
- Comunicação Engine → UI via Minimal API e `ConcurrentQueue`
- Interface desktop com Photino exibindo logs em tempo real

### Próximos passos

- [ ] **Persistência** — SQLite + Entity Framework Core para salvar histórico de interceptações e regras de whitelist
- [ ] **Blocklists dinâmicas** — rotina para baixar e carregar listas como [StevenBlack/hosts](https://github.com/StevenBlack/hosts) em memória com lookup < 1ms
- [ ] **Automação do Windows** — assumir/devolver o DNS primário da placa de rede via `netsh` na abertura/fechamento do app
- [ ] **Whitelist real** — implementar o mecanismo que altera o comportamento do motor (cache de permissões), não apenas registra a ação no console
- [ ] **Testes** — unitários e de integração para parse DNS, forwarding e API

## Requisitos

- .NET SDK 8 ou superior
- Node.js + npm (para o frontend Vite)
- Permissões administrativas (ligação na porta UDP/53)

> No Windows, execute o processo com privilégios elevados. Em Linux, pode haver conflito com o `systemd-resolved` — desative ou redirecione a porta conforme necessário.

## Como rodar

### 1. Frontend

```bash
cd GotchaDNS.UI/frontend
npm install
npm run build
```

Copie o diretório `dist` gerado para o caminho esperado pelo Photino:

```
GotchaDNS.UI/bin/Debug/net8.0/frontend/dist/
```

Para desenvolvimento com hot-reload, aponte `PhotinoWindow.Load` para o servidor Vite (`npm run dev`) em vez do build estático.

### 2. Motor (engine)

```bash
cd GotchaDNS.Engine
# Execute com privilégios elevados
dotnet run
```

### 3. UI desktop

```bash
cd GotchaDNS.UI
dotnet run
```

A janela Photino carrega `{AppContext.BaseDirectory}/frontend/dist/index.html`. Se a API estiver offline, a UI exibe dados de exemplo automaticamente.

## Notas técnicas e limitações conhecidas

- **Regras de bloqueio** são estáticas e definidas diretamente no código (sem arquivo de configuração ainda).
- **`BlockedLogs`** é mantido apenas em memória — não há persistência entre sessões (alvo do próximo milestone).
- **CORS aberto** (`AllowAnyOrigin`) por conveniência de desenvolvimento — não adequado para produção.
- **`ArrayPool<byte>`** é usado no loop de recepção para reduzir alocações temporárias.
