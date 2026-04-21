# GotchaDNS

**DNS Sinkhole nativo para Windows** — bloqueia anúncios, telemetria e malware no nível do sistema operacional, antes que qualquer conexão TCP ocorra. Protege navegadores, jogos e processos em background sem causar lentidão perceptível.

## O que é

O GotchaDNS sequestra o tráfego DNS da sua máquina na porta UDP/53 e aplica regras de bloqueio em memória com foco em baixo custo de alocação. Domínios permitidos são encaminhados normalmente à Cloudflare (`1.1.1.1`). Domínios bloqueados recebem uma resposta DNS forjada com `0.0.0.0` — a conexão morre instantaneamente, sem timeout.

A interface desktop (Photino + TypeScript) consome uma API mínima exposta pelo motor para exibir logs em tempo real e gerenciar liberações manuais.

## Arquitetura

```
GotchaDNS/
├── GotchaDNS.Engine/     # Motor de rede (.NET 10, BackgroundService)
└── GotchaDNS.UI/         # Interface desktop (Photino.NET + Vite/TypeScript)
    └── frontend/         # App web compilado e servido pelo Photino
```

### Fluxo de um pacote DNS

1. `DnsMotorService` abre um socket UDP em `0.0.0.0:53` e aguarda pacotes.
2. Para cada pacote recebido:
   - Extrai o nome de domínio via `ParseDomainName` (operações em `Span<byte>` para reduzir alocação).
   - Filtra ruído local: ignora domínios `.lan`, `.local` e nomes vazios.
   - Consulta as regras de bloqueio em memória:
     - **Bloqueado** → constrói resposta DNS forjada (`0.0.0.0`) e devolve ao cliente. Enfileira um `DnsLogEntry` em `BlockedLogs` e grava assincronamente no SQLite.
     - **Permitido** → faz forwarding para `1.1.1.1:53` via `UdpClient` e retorna a resposta real ao cliente (com timeout configurável).
3. A UI consome `/api/logs` periodicamente e exibe os bloqueios na tela.

### Componentes principais

| Componente | Responsabilidade |
|---|---|
| `DnsMotorService` | BackgroundService: socket UDP, parse, filtro, sinkhole, forwarding (concorrente) |
| `DnsLogEntry` | Record com `id`, `timestamp`, `domain`, `clientIp`, `reason` |
| `BlockedLogs` | `ConcurrentQueue<DnsLogEntry>` em memória (limite ~50 entradas) |
| `WhitelistRequest` | Record com `domain` para o `POST /api/whitelist` |
| `GotchaDNS.UI` | Janela Photino que carrega o `index.html` do build Vite |

## Endpoints HTTP

A API mínima roda no mesmo processo do motor (ASP.NET Core Minimal APIs) na porta `5005`.

- `GET /api/logs` — Retorna os logs de domínios bloqueados (a UI inverte a lista para mostrar os mais recentes primeiro).
- `POST /api/whitelist` — Aceita `{ "domain": "dominio.tld" }` (validação básica e normalização).
- `GET /api/whitelist` — Lista domínios liberados.
- `DELETE /api/whitelist?domain=...` — Remove domínio da whitelist.

## Status atual (implementações recentes)

- [x] Persistência básica: SQLite + EF Core (`GotchaDbContext`, `WhitelistEntry`, `DnsLogEntity`).
- [x] Whitelist real: endpoints POST/GET/DELETE `/api/whitelist` com persistência e store em memória.
- [x] Sinkhole: resposta DNS forjada com A=0.0.0.0 para domínios bloqueados.
- [x] Concurrency: processamento concorrente de pacotes (fire-and-forget) com limite (`SemaphoreSlim`) para evitar spikes.
- [x] Timeout no forwarding upstream (2s) para evitar tasks pendentes.

## Requisitos

- .NET SDK 8/10
- Node.js + npm (para o frontend Vite)
- Permissões administrativas (ligação na porta UDP/53)

> No Windows, execute o process com privilégios elevados. Em Linux, pode haver conflito com o `systemd-resolved` — desative ou redirecione a porta conforme necessário.

## Como rodar (desenvolvimento)

1. Frontend

```powershell
cd GotchaDNS.UI/frontend
npm install
npm run build
```

Copie o diretório `dist` gerado para o caminho esperado pelo Photino (ou use os scripts abaixo):

```
GotchaDNS.UI/bin/Debug/net8.0/frontend/dist/
```

2. Motor (engine)

```powershell
cd GotchaDNS.Engine
# Execute com privilégios elevados
dotnet run
```

3. UI desktop

```powershell
cd GotchaDNS.UI
dotnet run
```

Scripts de apoio (Windows PowerShell):

- `scripts\setup-dev.ps1` — instala dependências do frontend, roda build e copia `dist` para o output do projeto UI.
- `scripts\run-engine-elevated.ps1` — abre PowerShell elevado e executa o engine.
- `scripts\run-all.ps1` — executa setup, roda engine elevado e UI em janelas separadas.

## Notas técnicas e limitações conhecidas

- Regras de bloqueio estão atualmente em memória (HashSet de exemplo); há endpoint para whitelist persistente.
- `EnsureCreated()` é usado no momento para criar o DB; para produção recomenda-se usar migrações EF Core.
- CORS aberto (`AllowAnyOrigin`) por conveniência de desenvolvimento — não adequado para produção.
- Resposta forjada cobre queries A em casos comuns; implementação pode ser estendida para AAAA, CNAME e perguntas múltiplas.
