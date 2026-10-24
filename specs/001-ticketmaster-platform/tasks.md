# Tasks: Plataforma TicketMaster

**Input**: design docs de `specs/001-ticketmaster-platform/` (plan.md, data-model.md, contracts/, spec.md).

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅.

**Tests**: Incluídos — a Constituição (Artigo III) torna **Test-First NÃO-NEGOCIÁVEL**. Escrever testes ANTES da implementação, confirmar falha (Red), depois implementar (Green).

**Clarificações refletidas**: pista/GA (reserva por decremento Lua), reserva atômica "tudo ou nada", filtros de busca (tipo + cidade), anti-bot = fila justa + rate limiting, confirmação sem e-mail.

**Formato**: `[ID] [P?] [Story] Descrição` — `[P]` = paralelizável; `[Story]` = US1/US2/US3.

---

## Fase 1: Setup (Infraestrutura compartilhada)

**Objetivo**: inicialização do projeto e estrutura.

- [x] T001 Criar `ticketmaster-platform/TicketMaster.sln` e pastas `src/`, `tests/`, `infra/` (ver `solution/structure.md`).
- [x] T002 [P] Criar `infra/docker-compose.yml` com Postgres, Redis, Kafka, Debezium, Elasticsearch (serviços .NET em `profiles: ["app"]`).
- [x] T003 [P] Criar `infra/postgres/init.sql` — schema GA: `tickets.seat` opcional + `area` + `quantity` + `capacity`, `venues.city`, índices únicos parciais (`uq_ticket_seat`/`uq_ticket_area`), seed com assentos `A1`/`A2` + área `pista` (quantity=2).
- [x] T004 [P] Criar `infra/debezium/register-connector.sh` — 1 conector para `public.events,public.tickets,public.bookings`, `decimal.handling.mode=precise`, `ExtractNewRecordState` + `delete.handling.mode=rewrite`.
- [x] T005 [P] Criar `infra/elasticsearch/events-index.json` — analyzer pt-BR (stemming + asciifolding + sinônimos) + campos `type`/`city` (keyword).
- [x] T006 Criar `src/BuildingBlocks/Contracts` (DTOs + eventos) e `src/BuildingBlocks/EventBus` (abstrações Kafka).

---

## Fase 2: Fundacional (pré-requisitos bloqueantes)

**⚠️ Nenhuma user story começa antes desta fase.**

- [x] T007 Criar `src/ApiGateway` — YARP + `gateway.json` (rotas `/v1/events`, `/v1/search`, `/v1/bookings`) + **rate limiting por usuário** (anti-bot, RNF-2) + `QueueSimulatorMiddleware` (fila justa dev).
- [ ] T008 [P] Configurar OpenTelemetry (traces OTLP → Jaeger; métricas Prometheus `/metrics`) nos serviços.
- [ ] T009 [P] Configurar autenticação JWT bearer no gateway (validar token; MVP assume `userId` já autenticado — ver spec.md Assumptions).
- [x] T010 Criar `src/EventService` com EF Core + Npgsql e migração inicial — tabelas de `data-model.md` (tickets com `seat` opcional, `area`, `quantity`, `capacity`, `chk_ticket_mode`).
- [ ] T011 [P] Criar base de teste de contrato OpenAPI (Pact) em `tests/ContractTests`.
- [ ] T012 Configurar `tests/` com Testcontainers (fixtures Postgres/Redis/Kafka/ES).

**Checkpoint**: fundação pronta — user stories podem começar em paralelo.

---

## Fase 3: User Story 1 — Visualizar eventos (P1) 🎯 MVP

**Goal**: Catálogo público — listagem e detalhe de eventos com mapa de assentos/áreas e disponibilidade.

**Independent Test**: `GET /v1/events` e `GET /v1/events/{id}` retornam o catálogo via cache em <500ms; o detalhe mostra assentos numerados **e** áreas de pista/GA.

### Testes (escrever primeiro, confirmar FAIL)

- [ ] T013 [P] [US1] Teste de contrato para `GET /v1/events/{id}` (inclui `seatMap` com `area`/`quantity`) em `tests/ContractTests/Events.Get.cs`.
- [ ] T014 [P] [US1] Teste de integração do cache-aside (hit/miss) em `tests/EventService.IntegrationTests/CacheAsideTests.cs`.

### Implementação

- [x] T015 [P] [US1] Entidades `Event`, `Venue` (com `city`), `Ticket` (com `seat` opcional, `area`, `quantity`, `capacity`) em `src/EventService/Domain`.
- [x] T016 [P] [US1] `EventRepository` (EF Core) em `src/EventService/Infrastructure`.
- [x] T017 [US1] `RedisCacheService` (cache-aside) em `src/EventService/Infrastructure`.
- [x] T018 [US1] Use cases `GetEvent`, `ListEvents` em `src/EventService/Application`.
- [x] T019 [US1] Endpoints `GET /v1/events`, `GET /v1/events/{id}`, `POST/PUT/DELETE` (admin, protegido) em `src/EventService/Program.cs`.
- [x] T020 [US1] Validação + tratamento de erro padronizado (`404`, DTO `Error`).

**Checkpoint**: US1 funcional e testável isoladamente.

---

## Fase 4: User Story 2 — Pesquisar eventos (P1)

**Goal**: Busca full-text com typos/stemming e filtros por data, **tipo** e **cidade**.

**Independent Test**: `GET /v1/search?keyword=casao&type=show&city=sao%20paulo` retorna resultados relevantes em <500ms.

### Testes (escrever primeiro)

- [ ] T021 [P] [US2] Teste de contrato para `GET /v1/search` (parâmetros `keyword`, `startDate`, `endDate`, `type`, `city`) em `tests/ContractTests/Search.Get.cs`.
- [ ] T022 [P] [US2] Teste de integração do fluxo CDC→ES (Postgres→Kafka→Worker→ES) em `tests/Worker.IntegrationTests/CdcPipelineTests.cs`.

### Implementação

- [ ] T023 [P] [US2] `src/SearchService` — query Elasticsearch `multi_match` + `fuzziness: AUTO`, filtros `date`/`type`/`city` em `src/SearchService/Program.cs`.
- [ ] T024 [P] [US2] `src/Worker` — consumidor Kafka (MassTransit + Confluent.Kafka) em `src/Worker`.
- [ ] T025 [US2] Indexers `EventUpserted`, `EventDeleted`, `TicketStatusChanged` em `src/Worker/Indexers` — incluir `city` e `type` no documento.
- [ ] T026 [US2] Idempotência do Worker (dedup por `event_id`+`version`; `__deleted` → remover documento).
- [ ] T027 [US2] Aplicar `events-index` com analyzer pt-BR + campos `type`/`city` (keyword) no Elasticsearch.
- [ ] T028 [US2] Suite de validação de **relevância** (SC-004): ≥90% das consultas com typo/stemming retornam o evento esperado — em `tests/SearchService.IntegrationTests/RelevanceTests.cs`.

**Checkpoint**: US1 e US2 funcionando independentemente.

---

## Fase 5: User Story 3 — Comprar ingressos (P1)

**Goal**: Fluxo reserva → pagamento → efetivação, com reserva **atômica (tudo ou nada)** e anti-oversell para assento numerado **e** pista/GA.

**Independent Test**: `reserve` (múltiplos itens com `{ticketId, quantity}`) → `pay` → booking no Postgres; dois usuários nunca reservam o mesmo assento; pista/GA nunca excede `capacity`.

### Testes (escrever primeiro)

- [ ] T029 [P] [US3] Teste de contrato para `POST /v1/bookings/reserve` (payload `tickets: [{ticketId, quantity}]`, semântica tudo-ou-nada) e `POST /v1/bookings/{id}/pay` em `tests/ContractTests/Bookings.cs`.
- [ ] T030 [P] [US3] Teste de concorrência: 50 reservas do mesmo assento → 1×202/49×409; GA: reservas paralelas nunca excedem `capacity` — em `tests/BookingService.IntegrationTests/ConcurrencyTests.cs`.

### Implementação

- [ ] T031 [P] [US3] `ReservationService` — assento numerado via `SET NX EX 420`; pista/GA via **script Lua** (decremento atômico de estoque); **rollback tudo-ou-nada** se qualquer item falhar — em `src/BookingService/Application`.
- [ ] T032 [P] [US3] `PaymentService` (Stripe: create+confirm PaymentIntent; webhook assinado idempotente) em `src/BookingService/Infrastructure`.
- [ ] T033 [US3] `BookingRepository` — transação Postgres `SELECT…FOR UPDATE` + `UPDATE tickets SET quantity = quantity - n` (GA) / `status='booked'` (assento) + `INSERT booking ON CONFLICT (idempotency_key)` em `src/BookingService/Infrastructure`.
- [ ] T034 [US3] Endpoints `POST /v1/bookings/reserve`, `POST /v1/bookings/{id}/pay`, `POST /v1/webhooks/stripe`, `GET /v1/bookings/{id}`.
- [ ] T035 [US3] Anti-oversell na exibição: disponibilidade = `available - reservas_ativas_no_redis` (descontar assento **e** estoque GA) no `EventService`.

**Checkpoint**: US1+US2+US3 funcionando — MVP monetizável completo.

---

## Fase 6: Escala, disponibilidade e polish

- [ ] T036 [P] Validar `docs/capacity-planning.md` + teste de carga NBomber (RNF-001/005; inclui fila simulada) em `tests/LoadTests`.
- [ ] T037 [P] Read-replicas do Postgres + Redis Sentinel (`docker-compose.override.yml` guia / tarefa T036 do design).
- [ ] T038 [P] Réplicas de serviço + Load Balancer (`docker compose up --scale service=N`).
- [ ] T039 [P] Atualizar `solution/structure.md` e `docs/architecture.md` (refletir GA, reserva atômica, filtros).
- [ ] T040 Rodar `quickstart.md` end-to-end e validar todos os cenários de aceitação.

---

## Dependências e ordem de execução

- **Setup (Fase 1)** → **Fundacional (Fase 2)** → **US1/US2/US3 em paralelo (Fases 3–5)** → **Polish (Fase 6)**.
- US3 depende de US1 (eventos/tickets existem), mas é **independentemente testável** após a fundação (T010).
- **Dentro de cada story**: testes primeiro (falham) → modelos → serviços → endpoints → validação.

## Oportunidades de paralelismo

- T002–T005 (compose/init/connector/index) rodam juntos.
- T013/T014, T021/T022, T029/T030 são pares de teste paralelizáveis.
- Após a Fase 2, US1, US2 e US3 podem ser implementadas por devs diferentes.
- Fase 6 totalmente paralelizável.

## Notas

- `[P]` = arquivos distintos, sem dependência.
- Commitar após cada tarefa ou grupo lógico.
- Verificar testes **falhando** antes de implementar (Red-Green-Refactor).
- Parar em cada checkpoint para validar a story isoladamente.
