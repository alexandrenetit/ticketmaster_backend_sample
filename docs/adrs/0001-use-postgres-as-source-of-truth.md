# ADR-0001 — PostgreSQL como fonte única da verdade

**Status**: Accepted | **Date**: 2026-10-24 | **Deciders**: Arquiteto de Software

## Context

A plataforma precisa de **consistência forte** para a venda de ingressos (RNF-004: nunca vender o mesmo assento duas vezes) e de **alta disponibilidade/leitura em escala** (RNF-006: 100:1). O motor de dados deve suportar transações ACID, consultas JSON flexíveis (seatMap) e sincronização com a busca sem acoplar a aplicação.

## Decision

Usar **PostgreSQL 17** como fonte única da verdade, com:

- **ACID** para a efetivação da venda (transação `SELECT…FOR UPDATE` + `UPDATE` + `INSERT`).
- **JSONB** para `seat_map` (flexibilidade sem perder o relacional).
- **WAL + Debezium** para CDC (sincronização desacoplada com Elasticsearch).
- **Read-replicas** para absorver a leitura 100:1.
- **`UNIQUE (event_id, seat)`** como defesa em profundidade contra venda dupla.

## Alternatives Considered

- **MongoDB/DynamoDB**: sem transações multi-documento robustas → frágil para venda de ingresso.
- **MySQL**: CDC e full-text menos maduros; JSONB inferior.
- **CockroachDB/Spanner**: over-engineering (escala global desnecessária no MVP) e custo/operacional maiores.

## Consequences

- ✅ Consistência forte garantida pelo banco; CDC nativo via WAL.
- ✅ Leitura em escala via replicas; busca indexada desacoplada.
- ⚠️ Escreve na fonte da verdade devem ser protegidas por cache/reserva (Redis) para não virar gargalo.
