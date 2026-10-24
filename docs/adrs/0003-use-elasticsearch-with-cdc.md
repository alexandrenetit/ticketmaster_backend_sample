# ADR-0003 — Elasticsearch para busca full-text, sincronizado via CDC

**Status**: Accepted | **Date**: 2026-10-24 | **Deciders**: Arquiteto de Software

## Context

A pesquisa precisa de **tolerância a typos e stemming** (RNF-003: "casa"→casas/casarão/casinha) com **latência <500ms** (RNF-005) em escala. `LIKE '%…%'` faz table scan e não escala; índices locais do Postgres competem por CPU/I/O com a fonte da verdade sob 100:1.

## Decision

Usar **Elasticsearch 8** como índice de busca, **separado** da fonte da verdade:

- **Analisador pt-BR**: lowercase + `asciifolding` + stemming português + sinônimos.
- **Query**: `multi_match` com `fuzziness: AUTO` + `match_phrase`.
- **Sincronização por CDC** (Debezium lê o WAL do Postgres → Kafka → Worker → ES), mantendo o Postgres como fonte da verdade.

## Alternatives Considered

- **Postgres `tsvector`/`pg_trgm`**: viável em escala pequena/média, mas ranking e índice no mesmo nó do banco competem sob 100:1.
- **OpenSearch**: viável (fork livre), menor maturidade de tooling/queries avançadas.
- **Algolia/Meilisearch (SaaS)**: conveniência, porém lock-in e menor controle do CDC.

## Consequences

- ✅ Busca relevante (typo/stemming) e <500ms; escala independente da busca.
- ✅ Fonte da verdade intacta; índice sempre atualizado sem acoplar a aplicação.
- ⚠️ Consistência **eventual** (segundos) entre Postgres e ES — aceitável para busca; o status de estoque exibido vem do Redis (anti-oversell).
