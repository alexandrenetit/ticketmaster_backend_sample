# ADR-0004 — Kafka como buffer de eventos (entre CDC e indexação)

**Status**: Accepted | **Date**: 2026-10-24 | **Deciders**: Arquiteto de Software

## Context

Entre o Debezium (captura do WAL) e o Worker (indexação no Elasticsearch) há necessidade de **buffer durável**: absorver picos, permitir **reprocessamento** (replay) e **desacoplar** captura de consumo, com **recuperação sem perda** (RNF-007).

## Decision

Usar **Apache Kafka (KRaft)** como buffer:

- **Partição por `event_id`** → ordem total por evento.
- **Retenção de 7 dias** → replay de eventos de mudança.
- **Consumidor** `Worker` com **at-least-once** + **idempotência** (dedup `event_id`+`version`), commit após indexação bem-sucedida.

## Alternatives Considered

- **RabbitMQ**: ótimo para roteamento, porém retenção/replay e vazão de streaming inferiores.
- **SQS/SNS**: gerenciado, mas lock-in cloud e sem replay de log nativo.
- **Pipeline síncrono (escrever direto no ES no write)**: acopla a latência da compra à indexação e falha junto.

## Consequences

- ✅ Tolerância a picos, reprocessamento e escala independente de cada etapa.
- ✅ Recuperação sem perda (log retentivo) — RNF-007.
- ⚠️ Adiciona infraestrutura (3 brokers) — justificado por RNF-001/007, não por especulação.
