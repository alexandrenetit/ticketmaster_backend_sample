# Contratos Assíncronos — Eventos Kafka (CDC + Documentos)

> Schemas que trafegam no Kafka. Governados pelo **Artigo VI** da Constituição:
> evolução **aditiva** e **backward-compatible** — campos novos são sempre opcionais.
> O Worker consome o CDC (Debezium) e indexa **documentos** no Elasticsearch.

---

## 1. Eventos CDC (Debezium → Kafka)

O conector usa o transform **ExtractNewRecordState** (`unwrap`), então o Worker consome o **registro achatado** (o estado `after` da linha), **sem** o envelope `before/after/op`.

**Tópicos**: `tm.public.events`, `tm.public.tickets`, `tm.public.bookings` (prefixo `tm`).

**Formato consumido (achatado)**:

```json
{
  "id": "a1b2c3d4-e5f6-...",
  "description": "Show da Banda X",
  "artist": "Banda X",
  "date": 1789531200000000,
  "status": "published",
  "__deleted": false
}
```

- `__deleted: false` → **upsert** (criar/atualizar o documento).
- `__deleted: true`  → **remover** o documento do índice (`delete.handling.mode=rewrite`).
- `timestamptz` chega como **epoch em µs** (inteiro) — o Worker converte para ISO-8601.
- `numeric` (ex.: `price`) chega com **precisão exata** (`decimal.handling.mode=precise`).

---

## 2. Documentos indexados no Elasticsearch

O Worker converte o CDC e aplica de forma **idempotente** (dedup por `event_id` + `version`).

### 2.1 `EventUpserted`

```json
{
  "event_id": "uuid",
  "version": 17,
  "occurred_at": "2026-10-24T12:00:00Z",
  "description": "Show da Banda X",
  "artist": "Banda X",
  "type": "show",
  "date": "2027-01-15T21:00:00Z",
  "status": "published",
  "venue": "Estádio Y",
  "price_min": 120.00
}
```

### 2.2 `EventDeleted`

```json
{
  "event_id": "uuid",
  "version": 18,
  "occurred_at": "2026-10-24T12:00:00Z",
  "deleted": true
}
```

### 2.3 `TicketStatusChanged`

```json
{
  "ticket_id": "uuid",
  "event_id": "uuid",
  "seat": "A1",
  "status": "booked",
  "version": 9,
  "occurred_at": "2026-10-24T12:05:00Z"
}
```

> `TicketStatusChanged` atualiza a **disponibilidade** no Elasticsearch (e dispara a invalidação do cache do `EventService`).

---

## 3. Partição & ordenação

- **Chave de partição (default do Debezium)**: a **PK da tabela** (`id`). Logo, `tm.public.tickets` é particionado por `tickets.id`, **não** por `event_id`.
- **Consequência**: mudanças de tickets do mesmo evento podem cair em partições diferentes → **ordem por evento NÃO é garantida** no CDC bruto.
- **Impacto aceitável**: a busca é **eventualmente consistente**; o Worker usa `version` (idempotência, último estado vence). O status exibido de disponibilidade vem do **Redis** (consistência forte), não do ES.
- **Se ordenação estrita por evento for necessária** no futuro: `message.key.columns=tickets:event_id` no conector (Debezium ≥ 1.9).

---

## 4. Tópicos

| Tópico | Produtor | Consumidor | Semântica |
|--------|----------|-----------|-----------|
| `tm.public.events` | Debezium (conector único) | Worker | mudanças da tabela `events` |
| `tm.public.tickets` | Debezium (conector único) | Worker | mudanças da tabela `tickets` |
| `tm.public.bookings` | Debezium (conector único) | Worker | mudanças da tabela `bookings` |
| `tm.events.indexed` | Worker | (observabilidade) | ack de indexação (métrica/auditoria) |
