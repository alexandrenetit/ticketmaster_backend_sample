# Revisão Ultra-Rigorosa — SDD Plataforma TicketMaster

**Data**: 2026-10-24 | **Revisor**: Arquiteto de Software | **Escopo**: todos os 27 arquivos da SDD
**Alvo de implantação**: WSL Ubuntu + Docker (Engine nativo ou Docker Desktop)

---

## 1. Veredicto executivo

A SDD está **conceitualmente sólida e fiel ao Spec Kit** (constitution → spec → plan → data-model/contracts → tasks), com rastreabilidade requisito→solução completa e decisões arquiteturais corretas (Postgres ACID, Redis `SET NX EX`, ES + CDC, Kafka, Stripe, fila na borda).

Porém, na revisão focada em **"rodar de verdade no WSL Ubuntu + Docker"**, foram encontrados **4 defeitos críticos e 6 de severidade alta** que tornariam o deploy parcialmente não-funcional ou inconsistente. **Todos os críticos e altos foram corrigidos** nesta revisão. Restam **3 itens que exigem ação sua** (não são bugs de documento, são pré-requisitos de execução).

---

## 2. Método

1. Releitura de todos os 27 arquivos com viés de "isso sobe e funciona no WSL/Docker?".
2. Verificação de consistência cruzada (portas, variáveis, tópicos, formatos de eventos, fluxo de pagamento).
3. Validação de sintaxe/semântica de `docker-compose.yml`, `register-connector.sh`, `openapi.yaml`, `init.sql`, `events-index.json`.
4. Verificação factual da stack (.NET 10 / imagens / Debezium / KRaft / ES 8.15).

---

## 3. Findings corrigidos (severidade decrescente)

### 🔴 CRÍTICO

| ID | Local | Problema | Correção |
|----|-------|----------|----------|
| C1 | `infra/docker-compose.yml` | `docker compose up` tentava buildar os serviços .NET com Dockerfiles/csproj **que não existem** → falha imediata. | Serviços .NET movidos para `profiles: ["app"]`; agora `docker compose up -d` sobe só a infraestrutura (funciona já), e a stack completa usa `--profile app`. |
| C2 | `infra/docker-compose.override.yml` | `deploy.replicas` é **ignorado** pelo `docker compose up` (só Swarm: `docker stack`). O "HA" era no-op. | Substituído por guia correto (`--scale service=N`). |
| C3 | `infra/debezium/register-connector.sh` | `decimal.handling.mode: "double"` **corrompe valores monetários** (`price numeric(10,2)` vira float). | `decimal.handling.mode: "precise"`. |
| C4 | `contracts/events.md` | Descrevia o **envelope** `{before,after,op}` do Debezium, mas o conector usa o transform `ExtractNewRecordState` (unwrap) → o Worker recebe registro **achatado**. Contrato errado. | Documento reescrito para o formato achatado + marcador `__deleted`. |

### 🟠 ALTO

| ID | Local | Problema | Correção |
|----|-------|----------|----------|
| H1 | `register-connector.sh` | 3 conectores no mesmo banco = 3 **slots WAL** redundantes. | 1 conector único (`table.include.list` com as 3 tabelas). |
| H2 | `README.md`, `quickstart.md`, `docs/architecture.md` | Portas inconsistentes (5001/5002/5003) que não existem no compose (serviços internos). | Padronizado: serviços internos em `8080`, só o gateway exposto. |
| H3 | `contracts/openapi.yaml` + `docs/architecture.md` | Fluxo de pagamento ambíguo (`/pay` retornava 200 "efetivado" e também "webhook"). | `/pay` → **202** (pendente); efetivação via **webhook**; novo `GET /v1/bookings/{id}`. |
| H4 | `docker-compose.yml` | Prometheus sem arquivo de configuração (não coletava nada). | `infra/prometheus/prometheus.yml` + mount. |
| H5 | `quickstart.md` | Não mencionava **registrar o conector CDC** → pipeline Debezium→ES nunca funcionaria. | Passo `docker compose exec debezium bash /register-connector.sh`. |
| H6 | `contracts/events.md` | Afirmava partição por `event_id`, mas o Debezium particiona por **PK da tabela** (`id`). | Documentado corretamente + opção `message.key.columns` se precisar de ordem por evento. |

### 🟡 MÉDIO (documentados; sem correção de código, são decisões/ambientes)

| ID | Local | Finding | Status |
|----|-------|---------|--------|
| M1 | quickstart | Build de `.NET` em `/mnt/c` (9p) é lento e sujeito a permissões. | Recomendação: manter o repo no FS nativo (`~/ticketmaster`) ou usar volume de cache de build. |
| M2 | docker-compose | `postgres-replica` e Redis Sentinel do HA são **esquemáticos** (T036/T037). | Documentado no override; implementar na Fase 6. |
| M3 | register-connector.sh | Credenciais do Postgres **hardcoded** (`ticketmaster`), enquanto o compose usa `${POSTGRES_USER}`. | Se mudar `.env`, parametrizar o script. |
| M4 | quickstart | Chaves Stripe `sk_test_dummy`/`whsec_dummy` **não funcionam**; precisa chaves de teste reais + Stripe CLI. | Documentado no passo 3b. |
| M5 | events-index.json | O mapping não é carregado automaticamente; o Worker deve criar o índice (ou aplicar via curl). | Confirmar que o Worker aplica o template na inicialização. |
| M6 | ES healthcheck | `curl` no healthcheck assume que a imagem ES 8.15 inclui curl (padrão comum, mas verificar no primeiro boot). | Verificar; se falhar, trocar por healthcheck TCP. |

### 🟢 BAIXO

| ID | Local | Finding |
|----|-------|---------|
| L1 | events-index.json | Interação `synonym` + `stemmer` pode reordenar tokens; testar os sinônimos "casa→casas/casarão/casinha" no ES real. |
| L2 | spec.md | Cancelamento/estorno assumido fora do MVP (confirmado em Assumptions). |
| L3 | capacity-planning.md | Números são estimativas de ordem de grandeza; validar com o teste de carga NBomber (T035). |

---

## 4. Itens que exigem AÇÃO SUA (pré-requisitos, não bugs)

1. **Scaffold do código .NET 10** — a SDD é o design; para `--profile app up --build` funcionar, é preciso gerar `TicketMaster.sln` + `csproj` + `Dockerfile` dos 5 serviços (o alvo está em `solution/structure.md` e as tarefas em `tasks.md` T001–T034). Posso gerar esse scaffolding como próximo passo.
2. **Chaves Stripe de teste reais** — trocar `sk_test_dummy` por chaves reais + rodar `stripe listen` para webhooks locais.
3. **Sistema de arquivos** — recomendo clonar/mover o projeto para `~/` dentro do WSL (não `/mnt/c`) para builds rápidos e sem problema de permissão.

---

## 5. Verificação de consistência final

| Checagem | Resultado |
|----------|-----------|
| Portas compõem um único ponto de entrada (8080) | ✅ |
| Comandos de subida divididos em infra (agora) e stack completa (após scaffold) | ✅ |
| CDC: 1 conector → 3 tópicos → Worker → ES, com formato achatado e `__deleted` | ✅ |
| Pagamento: `/pay` 202 → webhook → transação ACID → `GET /bookings/{id}` | ✅ |
| Dinheiro (`price`) preservado com precisão (`precise`) | ✅ |
| `.env`/segredos fora de versionamento (`.gitignore` criado) | ✅ |
| HA documentado de forma honesta (o que funciona agora vs. T036/T037) | ✅ |

---

## 6. Conclusão

A SDD passou de "conceitualmente correta, mas não-implantável" para **"implantável em duas etapas, com infraestrutura funcional imediata e stack completa após o scaffold"**. As correções desta revisão eliminaram todos os bloqueadores de deploy de infraestrutura e as inconsistências de contrato/porta/pagamento. Os 3 itens de ação restantes são pré-requisitos de execução, não defeitos de especificação.
