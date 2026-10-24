# Implementation Plan: Plataforma TicketMaster

**Branch**: `001-ticketmaster-platform` | **Date**: 2026-10-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification de `specs/001-ticketmaster-platform/spec.md`

---

## Summary

Implementar o backend da réplica TicketMaster em **.NET 10 / C#** como um sistema distribuído que evolui em **10 camadas arquiteturais**, cada uma rastreada a um requisito. A fonte da verdade é o **PostgreSQL (ACID)**; leituras pesadas (100:1) são servidas por **cache Redis** e **Elasticsearch** (full-text); a concorrência da reserva é garantida por **Redis atômico (TTL 7min)** + **transação Postgres**; a sincronização busca/catálogo usa **CDC (Debezium → Kafka → Worker → Elasticsearch)**; o pagamento é **Stripe**; a borda absorve picos com **fila virtual (Cloudflare, simulada localmente)**. Tudo é containerizado para rodar no **WSL Ubuntu + Docker**.

---

## Clarificações aplicadas (sessão `/speckit-clarify` 2026-10-24)

| Decisão | Impacto no design |
|---------|-------------------|
| Assentos numerados **+** pista/GA | `tickets` com `seat` opcional + `area`/`quantity`/`capacity`; reserva GA por decremento atômico (script Lua) |
| Reserva **tudo ou nada** | reserva multi-item atômica com rollback no `BookingService` |
| Busca com filtros tipo + cidade | `GET /search` com `type`/`city`; índice ES com `city` (keyword) |
| Confirmação **sem e-mail** | sem serviço de notificação; confirmação síncrona + consulta do pedido |
| Anti-bot = fila justa + rate limiting | `RateLimiting` no gateway + `QueueSimulator`; detecção sofisticada na borda (produção) |

---

## Technical Context

| Item | Decisão |
|------|---------|
| **Language/Version** | C# 14 / .NET 10 (LTS), `net10.0` |
| **Primary Dependencies** | ASP.NET Core Minimal APIs; EF Core 10 (+ Npgsql); StackExchange.Redis; Elasticsearch.NET 8; MassTransit + Confluent.Kafka; Stripe.NET; YARP (gateway); OpenTelemetry |
| **Storage** | PostgreSQL 17 (fonte da verdade) + Redis 7 (cache/reserva) + Elasticsearch 8 (busca) + Kafka (buffer de eventos) |
| **Testing** | xUnit + Testcontainers (Postgres/Redis/Kafka/ES) + NBomber (carga) + Pact (contrato) |
| **Target Platform** | Linux containers (`mcr.microsoft.com/dotnet/aspnet:10.0`) no WSL Ubuntu via Docker Compose |
| **Project Type** | Backend distribuído (microsserviços) — 5 projetos de serviço + bibliotecas de contrato |
| **Performance Goals** | Busca p95 <500ms; reserva atômica sub-ms; absorver 10M usuários na abertura de vendas |
| **Constraints** | Leitura:escrita 100:1; sem venda dupla (invariante); idempotência em mutações; PCI-DSS delegado ao Stripe |
| **Scale/Scope** | 10M usuários simultâneos (pico); catálogo de eventos com mapa de assentos; eventos/venues/tickets/bookings |

---

## Constitution Check

*GATE: deve passar antes da Fase 0 (pesquisa) e ser re-verificado após a Fase 1 (design).*

### Gate I — Service-First

- [x] Capacidades isoladas em serviços com dono de dados claro (Event/Search/Booking/Worker).
- [x] Comunicação apenas por contratos (REST/OpenAPI + eventos).
- [x] Nenhum banco de escrita compartilhado entre serviços.

### Gate II — Contract-First

- [x] `contracts/openapi.yaml` definido antes do código.
- [x] `contracts/events.md` com schemas CDC + domínio antes do Worker.

### Gate III — Test-First (TDD)

- [x] Testes de contrato/integração/unidade escritos antes da implementação (ordem de criação no tasks.md).

### Gate IV — Integration Testing

- [x] Testcontainers para Postgres/Redis/Kafka/ES (sem mocks no crítico).
- [x] Teste de concorrência da reserva contra infraestrutura real.

### Gate V — Observability

- [x] OpenTelemetry (traces + métricas + logs JSON) em todos os serviços.

### Gate VI — Versioning

- [x] API versionada (`/v1`); eventos aditivos/backward-compatible.

### Gate VII — Simplicity

- [x] ≤5 projetos de serviço; cada evolução nasce de um requisito; sem future-proofing.

### Gate VIII — Anti-Abstraction

- [x] Uso direto de ASP.NET Core/EF Core/MassTransit/StackExchange.Redis/Confluent.Kafka.
- [x] Única representação de modelo por contexto (sem camadas de mapeamento redundantes).

### Gate IX — Data Integrity & Concurrency

- [x] Postgres ACID como fonte da verdade; reserva via `SET NX EX 420`; venda em transação.

**Resultado**: PASS — nenhuma violação; a tabela "Complexity Tracking" abaixo documenta apenas as decisões que adicionam componentes (justificadas por requisito).

---

## Project Structure

### Documentation (esta feature)

```text
specs/001-ticketmaster-platform/
├── spec.md              # user stories + requisitos + critérios de sucesso
├── plan.md              # este arquivo
├── research.md          # comparativos e racional das decisões técnicas
├── data-model.md        # ER, schemas SQL, índices, estratégia de chave/particionamento
├── quickstart.md        # cenários de validação + execução no WSL Ubuntu
├── contracts/
│   ├── openapi.yaml     # contrato REST (v1)
│   └── events.md        # schemas de eventos Kafka (CDC + domínio)
└── tasks.md             # tarefas executáveis (gerado a partir do plano)
```

### Source Code (raiz do repositório)

```text
ticketmaster-platform/
├── TicketMaster.sln
├── src/
│   ├── ApiGateway/                    # YARP + rate limiting + auth + simulação de fila (dev)
│   │   ├── Program.cs
│   │   └── gateway.json               # rotas /event, /search, /bookings
│   ├── EventService/                  # catálogo + CRUD + cache-aside
│   │   ├── Program.cs
│   │   ├── Domain/                    # Event, Venue, Ticket, Booking (agregados)
│   │   ├── Application/               # use cases (GetEvent, ListEvents, UpsertEvent)
│   │   └── Infrastructure/            # EF Core (Postgres), RedisCache, EventBus (Kafka)
│   ├── SearchService/                 # consulta Elasticsearch
│   ├── BookingService/                # reserve (Redis) + pay (Stripe) + booking (Postgres)
│   ├── Worker/                        # consome Kafka → indexa Elasticsearch (idempotente)
│   └── BuildingBlocks/
│       ├── Contracts/                 # DTOs + eventos (NuGet interno, versionado)
│       └── EventBus/                  # abstrações Kafka (MassTransit + Confluent)
├── tests/
│   ├── EventService.UnitTests/
│   ├── EventService.IntegrationTests/
│   ├── BookingService.IntegrationTests/   # concorrência reserva (Testcontainers)
│   ├── ContractTests/                     # Pact / OpenAPI
│   └── LoadTests/                         # NBomber (RNF-001/005)
└── infra/
    ├── docker-compose.yml
    ├── docker-compose.override.yml
    ├── Dockerfile.<service>
    ├── postgres/init.sql
    ├── debezium/register-connector.sh
    └── elasticsearch/events-index.json
```

**Structure Decision**: Microsserviços (5 processos) + bibliotecas compartilhadas de contrato, com **Clean Architecture** apenas nos serviços que têm regra de negócio rica (`EventService`, `BookingService`). `SearchService` e `ApiGateway` permanecem finos (proxy/configuração). Detalhes em `solution/structure.md`.

---

## Arquitetura — as 10 evoluções (design de alto nível)

Cada evolução é um incremento **rastreável a um requisito**. Diagramas completos (C4, componentes, sequência) em [`docs/architecture.md`](../../docs/architecture.md).

1. **Base** — `ApiGateway (YARP) → EventService → Postgres`. CRUD de eventos.
2. **Cache** — cache-aside no `EventService` (Redis): miss → Postgres → grava cache → retorna.
3. **Full-text** — substitui `LIKE '%…%'` (table scan) por índice invertido.
4. **Search desacoplado** — `SearchService → Elasticsearch`, sincronizado por CDC (Debezium lê WAL do Postgres).
5. **Kafka + Worker** — Debezium publica no Kafka; Worker consome e indexa (buffer/tolerância a picos).
6. **Booking + Redis** — `POST /bookings/reserve` **atômica (tudo ou nada)**: `SET NX EX 420` para assento numerado e decremento Lua para pista/GA (anti-oversell, TTL 7min).
7. **Stripe** — `POST /bookings/{id}/pay` processa cartão; webhook notifica resultado.
8. **Persistência** — pagamento aprovado → transação Postgres efetiva o booking.
9. **Fila virtual** — Cloudflare Waiting Room (lotes controlados, anti-bot, fila justa). Localmente: `QueueSimulator`.
10. **HA** — LB + réplicas de serviço, Redis Sentinel, Postgres read-replicas, Kafka retentivo, borda protegida.

### Fluxo consolidado

```text
User → Cloudflare (fila virtual) → Load Balancer → ApiGateway
  ├─→ SearchService  → Elasticsearch
  ├─→ EventService   → Redis (cache) / Postgres
  └─→ BookingService → Redis (reserve TTL 7min) → Stripe → Postgres (booking)

Sincronização: Postgres (WAL) → Debezium → Kafka → Worker → Elasticsearch
```

---

## Complexity Tracking

> Nenhuma violação constitucional. Componentes adicionais são justificados por requisito (não por especulação).

| Componente | Por que é necessário | Alternativa mais simples rejeitada (por quê) |
|------------|----------------------|---------------------------------------------|
| Elasticsearch (serviço de busca separado) | RNF-003/005: typos+stemming e <500ms em escala | `LIKE`/`tsvector` do Postgres faz table scan/índice local e não escala para 100:1 global |
| Kafka + Debezium (CDC) | Desacoplamento e tolerância a picos na indexação (RNF-001/007) | Sincronização síncrona no write acopla latência da compra à indexação |
| Redis (reserva) | RNF-004: operação atômica `SET NX EX` de alta vazão | Lock pessimista/`SELECT FOR UPDATE` no Postgres congestiona a fonte da verdade sob 10M usuários |
| Stripe (pagamento) | RF-003 + PCI-DSS delegado (RNF-009) | Pagamento próprio exigiria PCI-DSS e armazenar cartão — alto risco/custo |
| Cloudflare Waiting Room (borda) | RNF-001/002: absorver 10M e bloquear bots | Fila própria no backend não bloqueia bots na origem e derruba o gateway |
| 5 projetos de serviço | Cada contexto tem dono de dados e escala independentes (Artigo I) | Monólito único viola Service-First e impede escala seletiva da reserva vs. busca |

---

## Referências

- [spec.md](spec.md) — requisitos e user stories.
- [research.md](research.md) — comparativos técnicos.
- [data-model.md](data-model.md) — esquema relacional e estratégia de chaves.
- [architecture.md](../../docs/architecture.md) — diagramas.
- [capacity-planning.md](../../docs/capacity-planning.md) — estimativas de escala.
- [tasks.md](tasks.md) — lista executável.
