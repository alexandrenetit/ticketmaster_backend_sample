# SDD — Plataforma TicketMaster (Backend .NET 10 / C#)

> **Software Design Document** completo, gerado com a metodologia **[Spec-Driven Development (SDD)](https://github.com/github/spec-kit)** do GitHub Spec Kit.
>
> Domínio: uma réplica didática da plataforma **TicketMaster** — venda de ingressos para eventos de alta demanda (shows, esportes, teatros).

---

## 1. Visão geral

Este diretório contém a **especificação executável** da plataforma. Seguindo a inversão de poder do SDD, a *specification* é a **fonte da verdade**: o código `.NET 10 / C#` passa a ser a *expressão* (geração de último quilômetro) desta especificação — e não o contrário.

A arquitetura evolui em **10 camadas incrementais**, cada uma nascida de um requisito específico:

| # | Evolução | Requisito atendido |
|---|----------|-------------------|
| 1 | API Gateway + Event Service + Postgres | Separação de responsabilidades / fonte da verdade |
| 2 | Cache-aside (Redis) | RNF-5 (latência <500ms) e RNF-6 (leitura 100:1) |
| 3 | Full-text search | RNF-3 (typos/stemming) |
| 4 | Search Service + Elasticsearch + CDC | RNF-3 e RNF-5 |
| 5 | Kafka + Worker (pipeline assíncrono) | Tolerância a picos / desacoplamento |
| 6 | Booking Service + Redis (reserva TTL 7min) | RNF-4 (consistência/concorrência) |
| 7 | Pagamento com Stripe | RF-3 (compra) |
| 8 | Persistência do booking no Postgres | RF-3 + consistência ACID |
| 9 | Fila virtual (Cloudflare Waiting Room) | RNF-1 (10M usuários) e RNF-2 (anti-bot) |
| 10 | Alta disponibilidade (redundância em camadas) | RNF-7 (HA) |

---

## 2. Estrutura do repositório

```
root/
├── README.md                          ← este arquivo (índice e visão geral)
│
├── .specify/                          ← núcleo do Spec Kit (governança e templates)
│   ├── memory/
│   │   └── constitution.md            ← Constituição do projeto (9 artigos)
│   └── templates/                     ← templates reutilizáveis do processo SDD
│
├── specs/
│   └── 001-ticketmaster-platform/     ← especificação executável da feature
│       ├── spec.md                    ← USER STORIES, requisitos FR/RNF, entidades, critérios
│       ├── plan.md                    ← PLANO DE IMPLEMENTAÇÃO (stack .NET 10, estrutura, gates)
│       ├── research.md                ← PESQUISA TÉCNICA (comparativos e racional de decisões)
│       ├── data-model.md              ← MODELO DE DADOS (ER, schemas, índices, particionamento)
│       ├── quickstart.md              ← CENÁRIOS DE VALIDAÇÃO + execução no WSL Ubuntu
│       ├── contracts/                 ← CONTRATOS (OpenAPI REST + eventos Kafka/CDC)
│       └── tasks.md                   ← TAREFAS EXECUTÁVEIS (por user story, fases e paralelismo)
│
├── docs/
│   ├── architecture.md                ← DIAGRAMAS DE ARQUITETURA (C4, componentes, sequência — Mermaid)
│   ├── capacity-planning.md           ← ESTIMATIVAS DE CAPACIDADE (QPS, armazenamento, banda)
│   └── adrs/                          ← ARCHITECTURE DECISION RECORDS (decisões registradas)
│
├── infra/                             ← INFRAESTRUTURA (docker-compose + configs) para WSL Ubuntu
│   ├── docker-compose.yml
│   ├── docker-compose.override.yml
│   ├── .env.example
│   ├── postgres/init.sql
│   ├── debezium/register-connector.sh
│   └── elasticsearch/events-index.json
│
└── solution/                          ← ESTRUTURA DE REFERÊNCIA da solution .NET 10
    └── structure.md                   ← árvore de projetos e responsabilidades
```

---

## 3. Stack tecnológica (decisões de alto nível)

| Camada | Tecnologia | Justificativa (resumo) |
|--------|-----------|------------------------|
| Linguagem/Runtime | **.NET 10 (LTS) / C# 14** | Performance, async nativo, ecossistema cloud, ASP.NET Core Minimal APIs |
| API Gateway | **YARP** (reverse proxy da Microsoft) | Leve, configurável, rate limiting nativo |
| Serviços | **ASP.NET Core Minimal APIs** | Baixo overhead, startup rápido, ideal para microsserviços |
| Fonte da verdade | **PostgreSQL 17** | ACID, JSONB, full-text search, WAL (CDC), read-replicas |
| Cache / Reserva | **Redis 7 (Cluster + Sentinel)** | cache-aside + `SET NX` atômico com TTL (anti-oversell) |
| Busca | **Elasticsearch 8** | inverted index, stemming, typo tolerance, <500ms |
| CDC | **Debezium (Kafka Connect)** | lê o WAL do Postgres, publica em tópicos Kafka |
| Mensageria | **Apache Kafka (KRaft)** | buffer tolerante a picos, reprocessamento, escala independente |
| Consumo/indexação | **Worker .NET (MassTransit + Confluent.Kafka)** | BackgroundService, idempotente |
| Pagamento | **Stripe** | PCI-DSS, webhooks, cobrança de cartão |
| Borda / Fila virtual | **Cloudflare Waiting Room** | absorve pico de 10M usuários, anti-bot, fila justa |
| Observabilidade | **OpenTelemetry** (logs, métricas, traces) | observabilidade end-to-end |

Detalhes e comparativos completos: [`specs/001-ticketmaster-platform/research.md`](specs/001-ticketmaster-platform/research.md).

---

## 4. Como ler esta SDD (fluxo Spec Kit)

O Spec Kit define um fluxo em camadas — **cada artefato depende do anterior**:

```
constitution.md  →  spec.md  →  plan.md  →  data-model.md + contracts/  →  tasks.md
     (o quê/por quê)    (como)        (dados + APIs)            (execução)
```

1. **Constitution** — princípios imutáveis que governam qualquer implementação.
2. **spec.md** — *o quê* construir e *por quê* (user stories, requisitos, critérios de sucesso).
3. **plan.md** — *como* construir (stack, arquitetura, estrutura de projetos, gates de simplicidade).
4. **research.md / data-model.md / contracts/** — contexto técnico, modelo de dados e contratos de API.
5. **tasks.md** — lista executável, derivada dos contratos/entidades/cenários.
6. **docs/** — diagramas de arquitetura, estimativas de capacidade e ADRs (rastreabilidade decisão→requisito).

---

## 5. Requisitos atendidos (mapa de rastreabilidade)

| Requisito | Solução | Onde está detalhado |
|-----------|---------|---------------------|
| RF-1 Visualizar eventos | Event Service + cache | spec.md, contracts/openapi.yaml |
| RF-2 Pesquisar eventos | Search Service + Elasticsearch | spec.md, contracts, research.md |
| RF-3 Comprar ingressos | Booking Service + Redis + Stripe + Postgres | spec.md, plan.md, data-model.md |
| RNF-1 10M usuários simultâneos | Cloudflare Waiting Room + LB + escala horizontal | capacity-planning.md, architecture.md |
| RNF-2 Anti-bot / fila justa | Cloudflare (borda) | architecture.md, ADR-0006 |
| RNF-3 Pesquisa inteligente | Full-text + Elasticsearch | research.md, ADR-0003 |
| RNF-4 Sem venda dupla | `SET NX EX 420` no Redis + Postgres ACID | plan.md, data-model.md, ADR-0002 |
| RNF-5 Latência <500ms | Cache + Elasticsearch | capacity-planning.md |
| RNF-6 Leitura 100:1 | Cache + read-replicas | capacity-planning.md |
| RNF-7 Alta disponibilidade | Redundância em todas as camadas | architecture.md, ADRs |

---

## 6. Execução local (WSL Ubuntu + Docker)

Guia completo em [`specs/001-ticketmaster-platform/quickstart.md`](specs/001-ticketmaster-platform/quickstart.md).

```bash
# Dentro do WSL Ubuntu (ex.: /mnt/c/Estudos/ArquiteturaDeSoftware/Arquitetendo_TicketMaster/SDD)
cd infra
cp .env.example .env
docker compose up -d                        # só infraestrutura (sobe sem o código .NET)
docker compose --profile app up -d --build  # stack completa (exige scaffold em ../solution)
```

---

## 7. Fontes de entrada

- **System Design - Plataforma TicketMaster.md** — `Arquitetura Ticket Master\System Design - Plataforma TicketMaster.md` (requisitos funcionais/não funcionais e evoluções da arquitetura).
- **[Slides_Solucao.pdf](../Slides_Solucao.pdf)** — slides com os diagramas e o fluxo consolidado.
- **[github/spec-kit](https://github.com/github/spec-kit)** — metodologia e templates SDD.

---

*Documento gerado por um agente arquiteto sênior .NET/cloud. Última atualização: 2026-10-24.*
