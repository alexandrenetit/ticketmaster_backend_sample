#!/usr/bin/env bash
# ============================================================================
# Registra o conector CDC do Debezium (Postgres → Kafka).
# Executar dentro do container: docker compose exec debezium bash /register-connector.sh
#
# CORREÇÃO DE REVISÃO:
#   - 1 conector único para as 3 tabelas (antes eram 3 conectores = 3 slots WAL
#     redundantes no mesmo banco).
#   - decimal.handling.mode=precise (antes "double" CORROMPIA valores monetários
#     de price numeric(10,2)).
#   - Transform ExtractNewRecordState (unwrap): o Worker consome o registro
#     "achatado" (sem envelope before/after). Ver contracts/events.md.
# ============================================================================
set -euo pipefail

KAFKA_CONNECT="http://localhost:8083"

echo "Aguardando Kafka Connect..."
until curl -fs "$KAFKA_CONNECT/connectors" >/dev/null 2>&1; do
  sleep 2
done

curl -fs -X PUT "$KAFKA_CONNECT/connectors/tm-postgres-cdc/config" \
  -H "Content-Type: application/json" \
  -d '{
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "postgres",
    "database.port": "5432",
    "database.user": "ticketmaster",
    "database.password": "ticketmaster",
    "database.dbname": "ticketmaster",
    "topic.prefix": "tm",
    "table.include.list": "public.events,public.tickets,public.bookings",
    "plugin.name": "pgoutput",
    "slot.name": "tm_cdc_slot",
    "publication.autocreate.mode": "filtered",
    "decimal.handling.mode": "precise",
    "transforms": "unwrap",
    "transforms.unwrap.type": "io.debezium.transforms.ExtractNewRecordState",
    "transforms.unwrap.delete.handling.mode": "rewrite"
  }'

echo ""
echo "Conector registrado. Tópicos: tm.public.events | tm.public.tickets | tm.public.bookings"
echo "Conectores ativos:"
curl -fs "$KAFKA_CONNECT/connectors"
echo ""
