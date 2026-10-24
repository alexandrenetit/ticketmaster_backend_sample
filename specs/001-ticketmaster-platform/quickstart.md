# Quickstart & Cenários de Validação — Plataforma TicketMaster

> Fase 1 do Spec Kit: como subir a stack e validar cada cenário de aceitação.
> Alvo: **WSL Ubuntu** com Docker Engine.

---

## 1. Pré-requisitos

```bash
# No Windows: habilite WSL 2 e instale uma distro Ubuntu
wsl --install -d Ubuntu

# Dentro do WSL Ubuntu:
sudo apt update && sudo apt install -y docker.io docker-compose-v2
sudo usermod -aG docker $USER   # reabra o terminal
docker --version && docker compose version

# (Opcional, para buildar as imagens .NET) .NET SDK 10
sudo apt install -y wget && \
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O pkg.deb && \
sudo dpkg -i pkg.deb && sudo apt update && \
sudo apt install -y dotnet-sdk-10.0
```

---

## 2. Subir a stack

```bash
# Acesse a pasta da SDD pelo caminho montado do Windows
cd /mnt/c/Estudos/ArquiteturaDeSoftware/Arquitetendo_TicketMaster/SDD

cd infra
cp .env.example .env
docker compose up -d                        # só infraestrutura (sobe sem o código .NET)
docker compose --profile app up -d --build  # stack completa (exige scaffold em ../solution)
docker compose ps
```

**Serviços que sobem** (`docker compose --profile app up -d`):

| Serviço | Porta | Papel |
|---------|-------|-------|
| postgres | 5432 | fonte da verdade |
| redis | 6379 | cache + reserva |
| kafka (KRaft) | 9092 | buffer de eventos |
| debezium (connect) | 8083 | CDC (lê WAL) |
| elasticsearch | 9200 | busca |
| api-gateway | 8080 | YARP + fila simulada (dev) — único exposto ao host |
| event-service | 8080 (interno) | catálogo + cache-aside |
| search-service | 8080 (interno) | consulta ES |
| booking-service | 8080 (interno) | reserve + pay + booking |
| worker | 8080 (interno, /metrics) | consome Kafka → indexa ES |
| jaeger | 16686 | traces |
| prometheus | 9090 | métricas |

> **Fila virtual**: em produção usa Cloudflare Waiting Room; localmente o `QueueSimulator` (middleware no ApiGateway) emula a sala de espera com lotes.

---

## 3. Cenários de validação (mapeados às Acceptance Scenarios)

### US1 — Visualizar eventos

```bash
# Listar eventos
curl -s http://localhost:8080/v1/events | jq

# Detalhe de um evento (com seatMap + disponibilidade)
curl -s http://localhost:8080/v1/events/<eventId> | jq '.seatMap[] | select(.status=="available")'

# Evento inexistente → 404
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/v1/events/00000000-0000-0000-0000-000000000000
# esperado: 404
```

### US2 — Pesquisar eventos (typo + stemming)

```bash
# Busca com typo ("casao" deve achar "casas"/"casarão"/"casinha")
curl -s "http://localhost:8080/v1/search?keyword=casao" | jq '.items[].description'

# Busca com filtro de data
curl -s "http://localhost:8080/v1/search?keyword=show&startDate=2027-01-01&endDate=2027-12-31" | jq
```

### US3 — Comprar ingressos

```bash
# 0) IDs do seed — o evento fixo é 22222222-... ; busque os IDs dos tickets:
docker compose exec postgres psql -U ticketmaster -d ticketmaster \
  -c "SELECT id, seat, area, status FROM tickets WHERE event_id='22222222-2222-2222-2222-222222222222';"

# 1) Reserva temporária (TTL 7min)
curl -s -X POST http://localhost:8080/v1/bookings/reserve \
  -H 'Content-Type: application/json' \
  -d '{"tickets":["<ticketId1>","<ticketId2>"],"userId":"<userId>"}' | jq
# esperado: 202 com reservationId + expiresAt

# 2) Dois usuários no mesmo assento → exatamente um ganha
curl -s -X POST .../bookings/reserve -d '{"tickets":["<mesmoTicket>"],"userId":"<userA>"}' -o /dev/null -w "%{http_code}\n"
curl -s -X POST .../bookings/reserve -d '{"tickets":["<mesmoTicket>"],"userId":"<userB>"}' -o /dev/null -w "%{http_code}\n"
# esperado: 202 e 409 (respectivamente)

# 3) Pagamento (Stripe em modo test) — retorna 202 (pendente); efetivação via webhook
curl -s -X POST http://localhost:8080/v1/bookings/<reservationId>/pay \
  -H 'Content-Type: application/json' \
  -d '{"paymentMethodId":"pm_card_visa"}' | jq
# esperado: 202 com paymentRef + status "pending"/"requires_action"

# 3b) Encaminhar webhooks do Stripe localmente (Stripe CLI):
#   stripe listen --forward-to http://localhost:8080/v1/webhooks/stripe

# 3c) Consultar status final do booking
curl -s http://localhost:8080/v1/bookings/<bookingId> | jq
# esperado: status "confirmed"

# 4) Verificar no Postgres que o ticket virou "booked"
docker compose exec postgres psql -U ticketmaster -d ticketmaster \
  -c "SELECT seat,status FROM tickets WHERE id='<ticketId>';"
```

### Concorrência / anti-oversell (RNF-004)

```bash
# Dispara N reservas concorrentes do mesmo assento; deve haver exatamente 1 sucesso
for i in $(seq 1 50); do
  curl -s -X POST http://localhost:8080/v1/bookings/reserve \
    -H 'Content-Type: application/json' \
    -d "{\"tickets\":[\"<ticketId>\"],\"userId\":\"user-$i\"}" -o /dev/null -w "%{http_code}\n" &
done | sort | uniq -c
# esperado: 1×202, 49×409
```

---

## 4. Verificação de sincronização (CDC → ES)

```bash
# 0) Registrar o conector CDC (uma vez, após subir o debezium):
docker compose exec debezium bash /register-connector.sh

# Atualiza um evento no Postgres
docker compose exec postgres psql -U ticketmaster -d ticketmaster \
  -c "UPDATE events SET description='Show da Banda X (adiado)' WHERE id='<eventId>';"

# Em segundos, o documento no ES reflete a mudança
curl -s "http://localhost:9200/events-index/_doc/<eventId>" | jq '._source.description'
```

---

## 5. Observabilidade

- **Traces**: http://localhost:16686 (Jaeger) — procure `POST /v1/bookings/reserve` e veja os spans Redis/Postgres/Stripe.
- **Métricas**: http://localhost:9090 (Prometheus) — `http_server_duration_ms`, `cache_hit_ratio`, `reservation_ttl_seconds`.

---

## 6. Encerrar

```bash
docker compose down          # para tudo (mantém volumes)
docker compose down -v       # remove também os dados
```
