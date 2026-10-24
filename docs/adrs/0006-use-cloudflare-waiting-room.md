# ADR-0006 — Fila virtual na borda (Cloudflare Waiting Room)

**Status**: Accepted | **Date**: 2026-10-24 | **Deciders**: Arquiteto de Software

## Context

Na abertura de vendas, **10 milhões de usuários** tentam comprar no mesmo instante (RNF-001). Sem proteção na borda, o pico derrubaria o ApiGateway e todo o backend. Também é preciso **bloquear bots** e garantir **fila justa** sem privilégio geográfico (RNF-002).

## Decision

Usar **Cloudflare Waiting Room** **antes** do ApiGateway:

- Usuários entram em sala de espera com posição e horário previsto.
- Liberação em **lotes controlados** (ex.: 100 por vez) no ritmo que a arquitetura suporta.
- **Validação na borda** (anti-bot) e **fila global justa** (sem vantagem por proximidade geográfica).

**Ambiente local (WSL/Docker)**: como não há Cloudflare, a fila é **simulada** por um `QueueSimulator` (middleware do ApiGateway) que emula lotes/posição — o fluxo interno permanece idêntico.

## Alternatives Considered

- **Fila própria no backend**: não bloqueia bots na origem e derruba o gateway antes de enfileirar.
- **Rate limiting simples**: limita, mas não oferece fila justa nem absorve o pico.

## Consequences

- ✅ Absorve o caos da demanda massiva sem tocar o backend; bloqueia bots na origem.
- ✅ Experiência justa e estável; sem mudar o fluxo interno de reserva/compra.
- ⚠️ Dependência de borda externa em produção; simulada localmente para desenvolvimento.
