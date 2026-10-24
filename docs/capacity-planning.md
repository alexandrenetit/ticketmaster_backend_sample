# Capacity Planning & Estimativas — Plataforma TicketMaster

> Passo 2 da metodologia (antes de desenhar a solução): dimensionar **QPS, armazenamento e banda** para validar cada decisão arquitetural.

---

## 1. Premissas de escala

| Parâmetro | Valor |
|-----------|-------|
| Usuários simultâneos no pico (abertura de vendas) | **10.000.000** |
| Proporção leitura : escrita | **100 : 1** |
| Usuários ativos pós-fila (concorrentes reais no backend) | ~**100.000** |
| Tamanho médio de um venue | 10.000 assentos |
| Eventos por ano | 10.000 |
| Horizonte de retenção de dados | 3 anos |
| Latência alvo de busca (p95) | < 500ms |

> **Por que a fila virtual muda o cálculo**: os 10M ficam na borda (Cloudflare). O backend recebe apenas os **lotes liberados** (~100K ativos). Sem a fila, o backend precisaria de capacidade para 10M × QPS — inviável e caro.

---

## 2. Throughput (QPS)

### 2.1 Leituras (busca + visualização)

- 100.000 usuários ativos × 1 requisição de leitura a cada 5s = **20.000 QPS** de leitura.

### 2.2 Escritas (reserva/compra)

- 20.000 ÷ 100 (proporção 100:1) = **200 QPS** de escrita.

| Métrica | Valor |
|---------|-------|
| QPS leitura (pico) | 20.000 |
| QPS escrita (pico) | 200 |
| QPS total no gateway | ~20.200 |

**Validação**: `EventService` (cache-aside) serve a maior parte das leituras do Redis; o Postgres recebe apenas misses (~5–10% → ~2.000 QPS, cobertos por read-replicas). A reserva (200 QPS) é sub-ms no Redis — trivial.

---

## 3. Armazenamento (PostgreSQL)

### 3.1 Tickets (tabela dominante)

- 10.000 eventos/ano × 10.000 assentos = **100M tickets/ano**.
- 3 anos = **300M tickets**.
- Tamanho por linha (id + event_id + seat + type + price + status + índices) ≈ **120 bytes**.
- **300M × 120B ≈ 36 GB**.

### 3.2 Bookings

- Sell-through ~50% → 150M bookings em 3 anos.
- ~160 bytes/linha → **24 GB**.

### 3.3 Eventos + venues

- 30.000 eventos × ~2KB (com seat_map resumido) = 60 MB; venues desprezível.

| Tabela | Volume (3 anos) | Tamanho |
|--------|-----------------|---------|
| tickets | 300M | ~36 GB |
| bookings | 150M | ~24 GB |
| events | 30K | ~0,06 GB |
| **Total dados** | | **~60 GB** |
| **Total com índices (×2)** | | **~120 GB** |

**Decisão**: particionamento de `tickets`/`bookings` por `event_id`/`created_at` + read-replicas. Volume cabe em instância única com replicas (sem sharding no MVP).

---

## 4. Cache (Redis)

- Conjunto quente: ~1.000 eventos populares × ~50KB (detalhe + disponibilidade) ≈ **50 MB**.
- Reservas ativas: 100K usuários × 2 ingressos = 200K chaves × ~100B ≈ **20 MB**.
- **Redis 16 GB** é folgado; o objetivo é latência (sub-ms), não capacidade.

---

## 5. Busca (Elasticsearch)

- 30K documentos de evento (pequenos) + índice invertido ≈ **< 2 GB**.
- 3 shards primários × 1 réplica → tolerância a falha e escala horizontal fácil.

---

## 6. Mensageria (Kafka)

- Escritas CDC: **200 msg/s** (mudanças de tickets/bookings/events).
- Retenção 7 dias: 200 msg/s × 86.400s × 7 × ~1KB ≈ **~120 GB** de log (1 partição por event_id).
- Kafka KRaft com 3 brokers (HA) cobre com folga; o valor é **replay e buffer**, não vazão.

---

## 7. Banda de rede

| Fluxo | Cálculo | Resultado |
|-------|---------|-----------|
| Respostas de leitura | 20.000 QPS × 2KB | ~40 MB/s (320 Mbps) |
| Escritas | 200 QPS × 1KB | ~0,2 MB/s |
| CDC → Kafka → ES | 200 msg/s × 1KB | ~0,2 MB/s |

**Borda**: a maior pressão (10M × page views) é absorvida pela **CDN/borda Cloudflare**, não pelo backend.

---

## 8. Resumo de dimensionamento

| Componente | Dimensionamento |
|------------|-----------------|
| ApiGateway (YARP) | N réplicas atrás de LB |
| EventService | N réplicas (stateless) |
| SearchService | N réplicas (stateless) |
| BookingService | N réplicas (stateless) |
| PostgreSQL | primary + 2 read-replicas (~120GB) |
| Redis | Cluster 16GB + Sentinel (HA) |
| Elasticsearch | 3 shards × 1 réplica (<2GB) |
| Kafka | 3 brokers KRaft (~120GB log) |
| Cloudflare | absorve 10M na borda |

---

## 9. Teste de carga (validação dos números)

`tests/LoadTests` (NBomber) valida:

- 20.000 QPS de leitura com p95 < 500ms (cache quente).
- 200 QPS de escrita sem degradar a leitura.
- 50 reservas concorrentes do mesmo assento → exatamente 1 sucesso.
- Rampa de 10M (simulada) absorvida pela fila virtual.
