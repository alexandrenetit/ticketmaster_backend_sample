# Research & Technical Decisions — Plataforma TicketMaster

> Fase 0 do Spec Kit: contexto técnico que fundamenta cada decisão do `plan.md`.
> Cada decisão referencia um ADR (Architecture Decision Record) em `docs/adrs/`.

---

## 1. Linguagem & runtime

| Opção | Veredicto |
|-------|-----------|
| **.NET 10 (C# 14)** ✅ | **Escolhido.** LTS, `async/await` nativo, Minimal APIs de baixo overhead, AOT/trimming opcional, contêineres oficiais otimizados, ótimo throughput para I/O-bound (gateway/serviços de rede). |
| Java/Spring Boot | Sólido, mas verboso e com maior footprint de memória/startup. |
| Go | Ótimo para alta concorrência, porém ecossistema de produtividade (EF Core-like, DI) menos maduro. |
| Node.js/TS | Velocidade de dev, porém CPU-bound de serialização/consistência mais fraco para invariantes de concorrência. |

**Racional**: a carga é **I/O-bound** (cache, busca, mensageria, banco), onde o async do .NET brilha; a consistência forte (Artigo IX) se beneficia do tipo-system e de transações EF Core.

---

## 2. API Gateway

| Opção | Veredicto |
|-------|-----------|
| **YARP (Yet Another Reverse Proxy)** ✅ | **Escolhido.** Reverse proxy da Microsoft sobre ASP.NET Core. Rate limiting (via `Microsoft.AspNetCore.RateLimiting`), autenticação, reescrita de rota, middleware nativo. Leve e integrado ao ecossistema. |
| Ocelot | Popular, mas projeto com histórico de manutenção irregular e menos performático que YARP. |
| Envoy/NGINX | Ótimos, porém adicionam stack fora do .NET e configuração distinta. |

---

## 3. Fonte da verdade — banco de dados

| Opção | Veredicto |
|-------|-----------|
| **PostgreSQL 17** ✅ | **Escolhido.** Relacional, **ACID** (compra), JSONB (seatMap flexível), **full-text** nativo, **WAL** (CDC via Debezium), **read-replicas** (leitura 100:1), índices parciais/expressão. |
| MySQL | ACID, porém CDC e full-text menos maduros; JSONB inferior. |
| NoSQL (Mongo/Dynamo) | Falta de ACID multi-documento torna a venda de ingresso frágil. |

**Racional**: a compra exige **consistência forte** (RNF-004); o Postgres é o único que oferece ACID + WAL/CDC + read-replicas + FTS no mesmo motor.

---

## 4. Cache de leitura (RNF-005/006)

**Decisão**: **Redis 7** com padrão **cache-aside** (`EventService` lê cache → miss → Postgres → grava cache → retorna).

| Opção | Veredicto |
|-------|-----------|
| **Redis (cache-aside)** ✅ | Latência sub-ms, TTL, evolução natural para reserva atômica (`SET NX EX`). |
| In-memory (IMemoryCache) | Não compartilha entre réplicas → inconsistência em escala horizontal. |
| Memcached | Simples, porém sem estruturas de dados/atomicidade para a reserva. |

---

## 5. Busca full-text (RNF-003/005)

| Opção | Veredicto |
|-------|-----------|
| **Elasticsearch 8** ✅ | **Escolhido.** Inverted index + analisadores (pt-BR: stemming, lowercase, ASCII-folding) + ranking + fuzziness (typos) + sharding para escala. |
| OpenSearch | Fork viável e livre; menor maturidade de ferramentas/queries avançadas. |
| Postgres `tsvector`/`pg_trgm` | Ótimo para escala pequena/média, porém índice e ranking no mesmo nó da fonte da verdade competem por CPU/I/O sob 100:1. |

**Racional**: separar busca do banco (Evolução 4) permite escala independente e protege a fonte da verdade. Analisador configurado para **pt-BR**.

---

## 6. Sincronização — CDC (Evoluções 4/5)

| Opção | Veredicto |
|-------|-----------|
| **Debezium (Kafka Connect)** ✅ | **Escolhido.** Lê o **WAL** do Postgres (não invasivo), publica eventos de mudança, maduro e amplamente usado. |
| Outbox pattern (aplicação) | Acopla o write da aplicação à publicação; exige mudar todo serviço. |
| Polling/delta | Latente, consome recursos e perde eventos. |

**Fluxo**: `Postgres (WAL) → Debezium → Kafka (tópicos) → Worker → Elasticsearch`. Kafka atua como **buffer** (reprocessamento, tolerância a picos, desacoplamento).

---

## 7. Mensageria (RNF-001/007)

| Opção | Veredicto |
|-------|-----------|
| **Apache Kafka (KRaft)** ✅ | **Escolhido.** Log retentivo (recuperação sem perda), particionamento por `event_id`, alta vazão, replay nativo. |
| RabbitMQ | Ótimo para roteamento, porém retenção/replay e vazão de streaming inferiores. |
| SQS/SNS | Gerenciado, mas lock-in cloud e sem replay de log nativo. |

**Consumidor**: **Worker .NET** com **MassTransit + Confluent.Kafka**, **idempotente** (dedup por offset + `_id` do documento ES), processamento *at-least-once*.

---

## 8. Reserva e concorrência (RNF-004)

| Opção | Veredicto |
|-------|-----------|
| **Redis `SET key NX EX 420`** ✅ | **Escolhido.** Atômica, sub-ms, TTL nativo (libera sozinho aos 7min), escala horizontal. |
| Postgres `SELECT … FOR UPDATE` | Correto, mas serializa na fonte da verdade sob 10M usuários → gargalo. |
| Lock otimista (version) | Retries excessivos em alta contenção; UX ruim em pico. |
| Distributed lock (Redlock) | Mais complexo que o necessário; o `SET NX` já modela "reserva exclusiva com expiração". |

**Invariante anti-oversell**: disponibilidade exibida = `available - reservas_ativas_no_redis`; a venda efetiva é **transação ACID** no Postgres.

**Pista/GA (clarificação aplicada)**: para áreas de admissão geral, a reserva usa **decremento atômico de estoque** via script Lua (`EVAL`), não `SET NX` — impede oversell de `quantity`. A reserva de carrinho é **tudo-ou-nada** (rollback se qualquer item falhar).

---

## 9. Pagamento (RF-003, RNF-009)

| Opção | Veredicto |
|-------|-----------|
| **Stripe** ✅ | **Escolhido.** PCI-DSS delegado (backend nunca vê cartão), webhooks assinados, retry idempotente, SDK .NET oficial. |
| Pagar.me/PayPal | Alternativas regionais viáveis; Stripe escolhido pela maturidade do SDK .NET e dos webhooks. |
| Gateway próprio | Inviável: PCI-DSS + armazenamento de cartão = alto risco/custo. |

---

## 10. Fila virtual / anti-bot (RNF-001/002)

| Opção | Veredicto |
|-------|-----------|
| **Cloudflare Waiting Room** ✅ | **Escolhido (produção).** Absorve 10M na borda, libera em **lotes controlados**, bloqueia bots, fila global justa (sem privilégio geográfico). |
| Fila própria no backend | Não bloqueia bots na origem; derruba o gateway antes de enfileirar. |
| Rate limiting simples | Limita, mas não dá fila justa nem absorve o pico. |

**Local (WSL/Docker)**: a borda Cloudflare é **simulada** por um `QueueSimulator` (middleware no ApiGateway que emite fila/lotes) — ver `quickstart.md`.

**Anti-bot no MVP (clarificação aplicada)**: o MVP local entrega **fila justa + rate limiting** (limite de requisições por usuário no gateway); a detecção sofisticada de bot é delegada à borda em produção.

---

## 11. Observabilidade (RNF-008)

**Decisão**: **OpenTelemetry** (SDK .NET) → OTLP → **Jaeger** (traces) + **Prometheus** (métricas) + logs JSON (Loki). Correlation via `traceparent` e `X-Correlation-Id` em toda a cadeia.

---

## Matriz de decisão → ADR

| Decisão | ADR |
|---------|-----|
| Postgres como fonte da verdade | `adrs/0001-use-postgres-as-source-of-truth.md` |
| Redis para reserva + cache | `adrs/0002-use-redis-for-reserve-and-cache.md` |
| Elasticsearch + CDC | `adrs/0003-use-elasticsearch-with-cdc.md` |
| Kafka como buffer | `adrs/0004-use-kafka-as-buffer.md` |
| Stripe para pagamento | `adrs/0005-use-stripe-for-payments.md` |
| Cloudflare Waiting Room | `adrs/0006-use-cloudflare-waiting-room.md` |
