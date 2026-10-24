-- ============================================================================
-- TicketMaster — Schema + Seed (executado na inicialização do Postgres)
-- Fonte da verdade. Ver data-model.md para a definição completa.
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";  -- gen_random_uuid()

-- ------------------------------------------------------------------ users
CREATE TABLE IF NOT EXISTS users (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name        text NOT NULL,
    birthdate   date,
    email       text NOT NULL UNIQUE,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- ----------------------------------------------------------------- venues
CREATE TABLE IF NOT EXISTS venues (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    description text NOT NULL,
    address     text,
    city        text,
    capacity    integer NOT NULL CHECK (capacity > 0)
);

-- ----------------------------------------------------------------- events
CREATE TABLE IF NOT EXISTS events (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    venue_id    uuid NOT NULL REFERENCES venues(id),
    description text NOT NULL,
    date        timestamptz NOT NULL,
    type        text NOT NULL,
    artist      text,
    status      text NOT NULL DEFAULT 'draft',
    seat_map    jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_events_date   ON events (date);
CREATE INDEX IF NOT EXISTS idx_events_status ON events (status);
CREATE INDEX IF NOT EXISTS idx_events_seat_map ON events USING gin (seat_map);

-- ----------------------------------------------------------------- tickets
CREATE TABLE IF NOT EXISTS tickets (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id   uuid NOT NULL REFERENCES events(id),
    seat       text,
    area       text,
    type       text NOT NULL,
    price      numeric(10,2) NOT NULL CHECK (price >= 0),
    quantity   integer NOT NULL DEFAULT 1 CHECK (quantity >= 0),
    capacity   integer NOT NULL DEFAULT 1 CHECK (capacity > 0),
    status     text NOT NULL DEFAULT 'available',
    CONSTRAINT chk_ticket_mode CHECK (
        (seat IS NOT NULL AND area IS NULL) OR
        (seat IS NULL     AND area IS NOT NULL)
    )
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ticket_seat ON tickets (event_id, seat) WHERE seat IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_ticket_area ON tickets (event_id, area) WHERE area IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tickets_event_status ON tickets (event_id, status);

-- ---------------------------------------------------------------- bookings
CREATE TABLE IF NOT EXISTS bookings (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ticket_id       uuid NOT NULL REFERENCES tickets(id),
    user_id         uuid NOT NULL REFERENCES users(id),
    status          text NOT NULL DEFAULT 'confirmed',
    payment_ref     text,
    idempotency_key text NOT NULL UNIQUE,
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_bookings_user ON bookings (user_id);

-- ------------------------------------------------------------------- seed
-- Venue + Evento de exemplo com 3 assentos (para os cenários do quickstart).
INSERT INTO venues (id, description, address, city, capacity)
VALUES ('11111111-1111-1111-1111-111111111111', 'Estádio Exemplo', 'Av. Teste, 100', 'São Paulo', 4)
ON CONFLICT DO NOTHING;

INSERT INTO events (id, venue_id, description, date, type, artist, status, seat_map)
VALUES ('22222222-2222-2222-2222-222222222222',
        '11111111-1111-1111-1111-111111111111',
        'Show da Banda X', '2027-01-15T21:00:00-03:00', 'show', 'Banda X', 'published',
        '[{"seat":"A1","type":"vip","price":500.00},{"seat":"A2","type":"vip","price":500.00},{"area":"pista","type":"pista","price":120.00,"quantity":2}]')
ON CONFLICT DO NOTHING;

INSERT INTO tickets (event_id, seat, area, type, price, quantity, capacity, status) VALUES
    ('22222222-2222-2222-2222-222222222222', 'A1',  NULL,    'vip',   500.00, 1, 1, 'available'),
    ('22222222-2222-2222-2222-222222222222', 'A2',  NULL,    'vip',   500.00, 1, 1, 'available'),
    ('22222222-2222-2222-2222-222222222222', NULL, 'pista', 'pista', 120.00, 2, 2, 'available')
ON CONFLICT DO NOTHING;
