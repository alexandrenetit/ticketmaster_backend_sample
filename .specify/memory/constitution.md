# Constituição da Plataforma TicketMaster

> **Documento governante** do processo de Spec-Driven Development (Spec Kit).
> Os princípios abaixo são **imutáveis** para qualquer implementação deste repositório.
> Nenhum código, plano ou tarefa pode violá-los sem justificativa documentada (ver "Governança").

---

## Core Principles

### I. Service-First (Bounded Contexts como serviços independentes)

Cada capacidade de negócio é um **serviço .NET 10 autônomo e implantável** com limite de contexto claro (`EventService`, `SearchService`, `BookingService`, `Worker`). Nenhuma regra de negócio pode vazar entre contextos. Contratos são a única fronteira de comunicação.

- Serviços são autocontidos, testáveis isoladamente e documentados.
- Cada serviço tem **dono de dados** claro (o `BookingService` é dono de reservas/booking; o `EventService` é dono do catálogo).
- Proibido acoplamento direto entre serviços por banco compartilhado de escrita.

### II. Contract-First (Contratos antes do código)

Nenhuma implementação começa antes de os contratos existirem:

- **REST**: OpenAPI 3.1 em `specs/.../contracts/openapi.yaml`.
- **Assíncrono**: schemas de eventos (CDC/Debezium + eventos de domínio) em `contracts/events.md`.
- Contratos são a API pública estável; quebrá-los exige mudança de versão (ver Artigo VI).

### III. Test-First (TDD — NÃO NEGOCIÁVEL)

Toda implementação segue Test-Driven Development estrito:

1. Testes de **contrato** escritos e aprovados.
2. Testes confirmados **FALHANDO** (fase Red).
3. Implementação até passar (Green) e refatoração (Refactor).

Nenhum código de produção é escrito antes de os testes correspondentes existirem e falharem.

### IV. Integration Testing (Testes com infraestrutura real)

Testes de integração usam **ambientes realistas** (Testcontainers: Postgres, Redis, Kafka, Elasticsearch), nunca mocks para o que é crítico:

- Contrato de serviços (API) e contratos de mensagens.
- Consistência/concorrência da reserva (anti-oversell) testada contra Redis e Postgres reais.
- Replicação CDC testada ponta a ponta (Postgres → Kafka → Worker → ES).

### V. Observability (Observabilidade como cidadã de primeira classe)

Todos os serviços emitem **OpenTelemetry**:

- **Traces** correlacionados via `traceparent` em toda a cadeia (borda → gateway → serviço → banco/cache/mensageria).
- **Métricas** (Prometheus): QPS, latência p50/p95/p99, taxa de cache hit, tamanho de fila, tempo de reserva.
- **Logs estruturados** (JSON) com contexto (correlationId, eventId, userId, ticketId).

### VI. Versioning & Breaking Changes (Versionamento e mudanças de quebra)

- Versionamento **SemVer** (`MAJOR.MINOR.PATCH`).
- Mudança de quebra em contrato exige `MAJOR` + período de convivência (API `/v2`) ou **schema registry** com evolução compatível (`backward compatible`).
- Eventos assíncronos evoluem de forma **aditiva** (campos novos opcionais) para nunca quebrar consumidores.

### VII. Simplicity (Simplicidade — YAGNI)

- Comece simples; adicione complexidade somente quando um requisito **medido** a exigir.
- A arquitetura evolui em camadas (1→10) — **nenhuma camada é adicionada sem o requisito correspondente**.
- Sem *future-proofing* especulativo, sem *gold-plating*.

### VIII. Anti-Abstraction (Confie no framework)

- Use os recursos do framework diretamente (ASP.NET Core Minimal APIs, EF Core, MassTransit, StackExchange.Redis, Confluent.Kafka) em vez de criar camadas de abstração desnecessárias.
- Uma única representação de modelo por contexto; não duplique DTOs/entidades sem necessidade.
- Abstrações próprias só com justificativa documentada (ver "Complexity Tracking" no plan.md).

### IX. Data Integrity & Concurrency (Integridade de dados e concorrência)

O **PostgreSQL é a fonte única da verdade** e toda mutação crítica é **ACID**. Para alta contenção:

- Reserva temporária é **atômica no Redis** (`SET key value NX EX 420`) — nunca `GET` seguido de `SET` (race condition).
- A venda efetiva é uma **transação no Postgres** (checagem de estoque + inserção do booking + atualização do ticket na mesma transação).
- **Anti-oversell** é invariante de sistema: a disponibilidade exibida desconta reservas ativas do Redis.

---

## Security Requirements

- **Anti-bot** na borda (Cloudflare) — validação de tráfego legítimo antes de qualquer processamento.
- **Segredos** (Stripe keys, credenciais de banco) gerenciados via variáveis de ambiente / secret store — **nunca** versionados.
- **PCI-DSS**: o backend **nunca** vê o número do cartão — o fluxo usa Stripe (token/checkout) e webhooks assinados (`Stripe-Signature`).
- Comunicação interna via rede Docker isolada; TLS na borda.
- Idempotência em **todos** os endpoints de mutação (`Idempotency-Key`) e consumidores Kafka (processamento *at-least-once* com deduplicação).

## Performance Standards

- **Latência de busca**: p95 < 500ms.
- **Proporção leitura:escrita**: 100:1 — cache obrigatório para leituras quentes.
- **Reserva**: operação atômica sub-milissegundo no Redis.
- **Escala**: 10M usuários simultâneos na abertura de vendas, absorvidos pela fila virtual + escala horizontal.

## Deployment (WSL Ubuntu + Docker)

- Todo artefato é **containerizado** (Docker) e orquestrado via `docker compose`.
- Imagens multi-stage (`build` → `runtime` em `mcr.microsoft.com/dotnet/aspnet:10.0`).
- Configuração 12-factor: configuração via ambiente, processo stateless, paridade dev/prod.
- Execução alvo: **WSL Ubuntu** com Docker Engine (guia em `quickstart.md`).

## Development Workflow (fluxo Spec Kit)

1. Atualizar `constitution.md` (se necessário, via processo de emenda).
2. `spec.md` → `plan.md` → `data-model.md`/`contracts/` → `tasks.md`.
3. Implementar tarefa a tarefa, com **testes antes do código**.
4. Revisar em PR: verificar conformidade constitucional + testes verdes + observabilidade.

## Governance

- **A Constituição prevalece** sobre qualquer outra prática ou convenção do repositório.
- **Emenda**: mudanças exigem (a) racional documentado, (b) revisão por mantenedor, (c) avaliação de compatibilidade retroativa e (d) bump de versão.
- **Exceções**: violações pontuais de simplicidade/abstração devem ser registradas na seção "Complexity Tracking" do `plan.md` com justificativa e alternativa rejeitada.
- Todo PR deve verificar conformidade com os Artigos I–IX.

---

**Version**: 1.0.0 | **Ratified**: 2026-10-24 | **Last Amended**: 2026-10-24
