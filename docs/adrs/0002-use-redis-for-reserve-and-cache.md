# ADR-0002 — Redis para reserva temporária (anti-oversell) e cache de leitura

**Status**: Accepted | **Date**: 2026-10-24 | **Deciders**: Arquiteto de Software

## Context

Dois problemas distintos: (1) leituras 100:1 com latência <500ms (RNF-005/006); (2) impedir que dois usuários reservem o mesmo assento em alta contenção (RNF-004), sob picos de 10M usuários.

## Decision

Usar **Redis 7** para ambos, com primitivas diferentes:

- **Cache de leitura (cache-aside)**: `EventService` lê `event:{id}`; miss → Postgres → grava cache (TTL 5min) → retorna.
- **Reserva (anti-oversell)**: `SET reserve:ticket:{ticketId} {userId} NX EX 420` — operação **atômica** com **TTL de 7 minutos**. Retorno `OK` = reserva adquirida; `nil` = ocupado → `409`.

Em produção: **Redis Cluster + Sentinel** para HA.

## Alternatives Considered

- **Postgres `SELECT…FOR UPDATE` para reserva**: correto, mas serializa na fonte da verdade sob 10M usuários → gargalo.
- **Lock otimista (version/retry)**: retries excessivos em contenção extrema; UX ruim no pico.
- **Distributed lock (Redlock)**: mais complexo; `SET NX EX` já modela "exclusividade + expiração".
- **IMemoryCache/Memcached**: não compartilha entre réplicas / sem atomicidade.

## Consequences

- ✅ Reserva sub-ms, escala horizontal, liberação automática via TTL (sem jobs de limpeza).
- ✅ Protege o Postgres da proporção 100:1.
- ⚠️ Estado volátil: a **efetivação** deve reconferir no Postgres (transação ACID) — o Redis é a barreira de vazão, não a fonte da verdade.
