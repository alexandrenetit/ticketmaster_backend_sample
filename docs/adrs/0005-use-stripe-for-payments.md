# ADR-0005 — Stripe para processamento de pagamento

**Status**: Accepted | **Date**: 2026-10-24 | **Deciders**: Arquiteto de Software

## Context

A compra (RF-003) exige cobrança de cartão com segurança. Processar cartão internamente implicaria **PCI-DSS** completo e armazenamento de dados sensíveis — risco e custo desproporcionais ao MVP.

## Decision

Usar **Stripe** (modo test localmente):

- **PaymentIntent** para a cobrança; o backend **nunca** vê o número do cartão.
- **Webhook assinado** (`Stripe-Signature`) notifica o resultado (`payment_intent.succeeded` / `payment_intent.payment_failed`).
- **Idempotência** por `Idempotency-Key` (dedup de webhook/retry).

## Alternatives Considered

- **Gateway próprio**: inviável (PCI-DSS + armazenamento de cartão).
- **Pagar.me/PayPal/Adyen**: alternativas válidas; Stripe escolhida pela maturidade do SDK .NET e dos webhooks.

## Consequences

- ✅ PCI-DSS delegado; webhooks assinados e idempotentes.
- ✅ Fluxo: aprovação → Booking Service grava no Postgres (efetiva); recusa/expira → libera reserva no Redis.
- ⚠️ Dependência de terceiro; modo test no WSL, chaves via variáveis de ambiente (nunca versionadas).
