# Arquitetura — Plataforma TicketMaster

> Diagramas de arquitetura (C4 + componentes + sequência) em Mermaid.
> Complementa `plan.md` e é a referência visual das 10 evoluções.

---

## 1. Contexto (C4 — nível 1)

```mermaid
flowchart LR
    U[Usuário / Fã] --> CF[Cloudflare\nFila virtual + Anti-bot]
    CF --> SYS[TicketMaster Platform\nBackend .NET 10]
    SYS --> STR[Stripe\nPagamento PCI-DSS]
    SYS --> ES[(Elasticsearch\nBusca)]
    SYS --> PG[(PostgreSQL\nFonte da verdade)]
    SYS --> RD[(Redis\nCache + Reserva)]
    SYS --> KF[Kafka\nBuffer de eventos]
    ADM[Admin/Produtor de evento] --> SYS
```

---

## 2. Contêineres (C4 — nível 2)

```mermaid
flowchart TB
    subgraph Edge["Borda"]
        CF[Cloudflare Waiting Room]
        LB[Load Balancer]
    end

    subgraph Core["Núcleo .NET 10"]
        GW[ApiGateway\nYARP + RateLimit + QueueSimulator]
        EV[EventService\nCatálogo + Cache-aside]
        SE[SearchService\nConsulta ES]
        BK[BookingService\nReserve + Pay + Booking]
        WK[Worker\nConsome Kafka → indexa ES]
    end

    subgraph Data["Dados"]
        PG[(PostgreSQL\n+ read-replicas)]
        RD[(Redis Cluster\n+ Sentinel)]
        ES[(Elasticsearch)]
        KF[Kafka\nKRaft]
        DBZ[Debezium\nKafka Connect]
    end

    STR[Stripe\nWebhook]

    CF --> LB --> GW
    GW --> EV & SE & BK
    EV --> RD & PG
    SE --> ES
    BK --> RD & STR & PG
    PG -- "WAL" --> DBZ --> KF --> WK --> ES
    STR -- "webhook" --> BK
```

---

## 3. Evoluções da arquitetura (1 → 10)

### E1 — Base

```mermaid
flowchart LR
    U[User] --> GW[ApiGateway] --> EV[EventService] --> PG[(Postgres)]
```

### E2 — Cache-aside (RNF-5/6)

```mermaid
flowchart LR
    EV[EventService] -->|1. GET| RD[(Redis)]
    RD -->|miss| PG[(Postgres)]
    PG -->|2. grava| RD
    RD -->|3. retorna| EV
```

### E3/E4 — Full-text + Search Service + CDC (RNF-3/5)

```mermaid
flowchart LR
    PG[(Postgres)] -- "WAL" --> DBZ[Debezium]
    DBZ --> KF[Kafka]
    KF --> WK[Worker]
    WK --> ES[(Elasticsearch)]
    U[User] --> GW[Gateway] --> SE[SearchService] --> ES
```

### E6/E7/E8 — Booking + Redis + Stripe + Postgres (RNF-4, RF-3)

```mermaid
flowchart LR
    U[User] --> BK[BookingService]
    BK -->|SET NX EX 420| RD[(Redis Reserva)]
    BK -->|PaymentIntent| STR[Stripe]
    STR -->|webhook| BK
    BK -->|transação ACID| PG[(Postgres Booking)]
```

### E9 — Fila virtual (RNF-1/2)

```mermaid
flowchart LR
    U[10M Users] --> CF[Cloudflare\nWaiting Room]
    CF -->|lotes de 100| GW[ApiGateway]
```

### E10 — Alta disponibilidade (RNF-7)

```mermaid
flowchart TB
    LB[Load Balancer]
    LB --> GW1[ApiGateway 1] & GW2[ApiGateway 2] & GW3[ApiGateway N]
    GW1 & GW2 & GW3 --> SVCS[Event / Search / Booking\nréplicas N]
    SVCS --> RD[Redis Cluster\n+ Sentinel failover]
    SVCS --> PG[Postgres primary\n+ read-replicas]
    SVCS --> ES[Elasticsearch\n+ shards/réplicas]
    KF[Kafka retentivo\nrecuperação sem perda] --- SVCS
    CF[Cloudflare borda] --> LB
```

---

## 4. Sequência — Pesquisa (US2)

```mermaid
sequenceDiagram
    participant U as User
    participant GW as ApiGateway
    participant SE as SearchService
    participant ES as Elasticsearch
    U->>GW: GET /v1/search?keyword=casao
    GW->>SE: forward (rate-limited)
    SE->>ES: multi_match fuzziness AUTO + match_phrase
    ES-->>SE: hits ranqueados
    SE-->>GW: JSON results
    GW-->>U: 200 <500ms
```

---

## 5. Sequência — Compra (US3)

```mermaid
sequenceDiagram
    participant U as User
    participant GW as ApiGateway
    participant BK as BookingService
    participant RD as Redis
    participant STR as Stripe
    participant PG as Postgres

    U->>GW: POST /v1/bookings/reserve {tickets,userId}
    GW->>BK: forward
    BK->>RD: SET reserve:ticket:N userId NX EX 420
    alt assento livre
        RD-->>BK: OK
        BK-->>U: 202 reservationId + expiresAt
    else assento ocupado
        RD-->>BK: nil
        BK-->>U: 409 Conflict
    end

    U->>GW: POST /v1/bookings/{id}/pay {paymentMethodId}
    GW->>BK: forward
    BK->>STR: create+confirm PaymentIntent
    STR-->>BK: pending / succeeded (requires_action)
    BK-->>U: 202 paymentRef (status pendente)
    Note over STR,BK: webhook assíncrono (idempotente, assinado)
    STR->>BK: POST /v1/webhooks/stripe (payment_intent.succeeded)
    BK->>PG: BEGIN; SELECT..FOR UPDATE; UPDATE ticket=booked; INSERT booking; COMMIT
    BK->>RD: DEL reserve:ticket:N
    Note over BK,U: GET /v1/bookings/{id} → confirmed
```

---

## 6. Sequência — Sincronização CDC (Evoluções 4/5/8)

```mermaid
sequenceDiagram
    participant PG as Postgres (WAL)
    participant DBZ as Debezium
    participant KF as Kafka
    participant WK as Worker
    participant ES as Elasticsearch

    PG->>DBZ: mudança (INSERT/UPDATE/DELETE)
    DBZ->>KF: publish {after, op, ts_ms}
    KF->>WK: consume (at-least-once)
    WK->>WK: dedup (event_id + version)
    WK->>ES: index/update/delete
    ES-->>WK: ack
    WK->>KF: commit offset
```

---

## 7. Diagrama de implantação (WSL Ubuntu + Docker)

```mermaid
flowchart TB
    subgraph WSL["WSL Ubuntu — Docker Compose"]
        subgraph net["rede ticketmaster-net"]
            GW[api-gateway :8080]
            EV[event-service]
            SE[search-service]
            BK[booking-service]
            WK[worker]
            PG[postgres :5432]
            RD[redis :6379]
            KF[kafka :9092]
            DBZ[debezium-connect :8083]
            ES[elasticsearch :9200]
            JA[jaeger :16686]
            PR[prometheus :9090]
        end
    end
    HOST[Navegador / curl no Windows] -->|localhost:8080| GW
    GW --> EV & SE & BK
    EV --> RD & PG
    SE --> ES
    BK --> RD & PG
    PG --> DBZ --> KF --> WK --> ES
    EV & SE & BK & WK -.OTLP.-> JA & PR
```

---

## 8. Rastreabilidade requisito → componente

| Requisito | Componente(s) | Diagrama |
|-----------|---------------|----------|
| RNF-001 10M | Cloudflare + LB + escala horizontal | E9, E10 |
| RNF-002 anti-bot | Cloudflare (borda) | E9 |
| RNF-003 busca | Elasticsearch + analisador pt-BR | E3/E4, Seq.4 |
| RNF-004 concorrência | Redis `SET NX EX` + Postgres ACID | E6, Seq.5 |
| RNF-005 <500ms | Redis cache + Elasticsearch | E2, E3 |
| RNF-006 100:1 | Cache + read-replicas | E2, E10 |
| RNF-007 HA | Redundância em camadas | E10 |
