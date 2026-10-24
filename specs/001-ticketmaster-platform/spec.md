# Feature Specification: Plataforma TicketMaster

**Feature Branch**: `001-ticketmaster-platform`

**Created**: 2026-10-24

**Status**: Approved

**Input**: Réplica da plataforma TicketMaster — venda de ingressos para eventos de alta demanda (shows, esportes, teatro). Os usuários visualizam eventos, pesquisam com tolerância a erros de digitação e variações de palavra, e compram ingressos com a garantia de que o mesmo assento nunca é vendido duas vezes, mesmo sob picos de milhões de acessos simultâneos.

---

## Clarifications

### Session 2026-10-24

- Q: Todos os eventos têm assentos numerados, ou também suportam áreas de pista/admissão geral (GA)? → A: Ambos — assentos numerados **e** áreas de pista/admissão geral vendidas por quantidade.
- Q: A reserva de vários ingressos é "tudo ou nada" ou parcial? → A: Tudo ou nada — se qualquer ingresso falhar, nenhum é reservado (rollback).
- Q: A busca precisa de filtros além de palavra-chave e data? → A: Palavra-chave + data + tipo de evento + local/cidade.
- Q: O MVP precisa enviar o ingresso/confirmação por e-mail? → A: Não — confirmação em tela + consulta do pedido (sem e-mail).
- Q: Qual o nível de proteção anti-bot no MVP? → A: Fila justa + limite de taxa; detecção sofisticada delegada à borda em produção.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Visualizar eventos (Priority: P1)

O usuário acessa a plataforma e navega pelos eventos disponíveis, vendo título, artista, data, local e a disponibilidade de ingressos.

**Why this priority**: É a porta de entrada do produto e pré-requisito para pesquisar e comprar. Sem catálogo visível, nenhuma outra história tem valor.

**Independent Test**: Pode ser validado isoladamente — abrir a listagem e o detalhe de um evento retorna o catálogo completo e o mapa de assentos, com baixa latência. Entrega valor como catálogo público.

**Acceptance Scenarios**:

1. **Given** um evento publicado, **When** o usuário abre a página do evento, **Then** vê título, descrição, artista, data, local e o mapa de assentos com preços e disponibilidade, em menos de 500ms.
2. **Given** múltiplos eventos publicados, **When** o usuário abre a listagem, **Then** vê os eventos paginados e ordenados por data, com disponibilidade consolidada.
3. **Given** um identificador de evento inexistente, **When** o usuário tenta abri-lo, **Then** vê uma mensagem de "evento não encontrado".

---

### User Story 2 — Pesquisar eventos (Priority: P1)

O usuário busca eventos por palavra-chave, com tolerância a erros de digitação e variações morfológicas (ex.: "casa" encontra "casas", "casarão", "casinha").

**Why this priority**: Descoberta é a principal jornada de conversão; uma busca ruim perde vendas. O requisito de "busca inteligente" (RNF-3) é estruturante para a arquitetura.

**Independent Test**: Pode ser validado isoladamente — buscar um termo (com typo) retorna os resultados relevantes, sem tocar no fluxo de compra. Entrega valor como busca pública.

**Acceptance Scenarios**:

1. **Given** eventos catalogados, **When** o usuário busca "casao" (com erro de digitação), **Then** encontra "casas", "casarão" e "casinha", ordenados por relevância.
2. **Given** filtros de data, **When** o usuário busca um termo com intervalo de datas, **Then** vê apenas eventos do período, em menos de 500ms.
3. **Given** um termo sem correspondência, **When** o usuário busca, **Then** vê uma lista vazia (sem erro).
4. **Given** eventos de tipos e locais variados, **When** o usuário filtra por tipo de evento e local/cidade, **Then** vê apenas os eventos que correspondem aos filtros combinados com a palavra-chave.

---

### User Story 3 — Comprar ingressos (Priority: P1)

O usuário seleciona assentos, **reserva** temporariamente durante o checkout, **paga** via cartão e, se aprovado, **efetiva a compra** recebendo a confirmação do ingresso em tela.

**Why this priority**: É o objetivo de negócio central (monetização). Concentra os requisitos mais difíceis: consistência/concorrência (RNF-4), anti-bot (RNF-2) e pagamento (RF-3).

**Independent Test**: Pode ser validado isoladamente — o fluxo reserva → pagamento → confirmação, com a garantia de que dois usuários nunca reservam o mesmo assento. Entrega o MVP monetizável.

**Acceptance Scenarios**:

1. **Given** um assento disponível, **When** o usuário inicia o checkout, **Then** o assento fica reservado por 7 minutos e indisponível para terceiros.
2. **Given** uma reserva ativa, **When** o pagamento é aprovado, **Then** a compra é confirmada e o ingresso passa a pertencer ao usuário.
3. **Given** dois usuários disputando o mesmo assento, **When** ambos tentam reservar ao mesmo tempo, **Then** exatamente um obtém sucesso e o outro é informado de que o assento não está mais disponível.
4. **Given** uma reserva cujo tempo expirou, **When** os 7 minutos terminam, **Then** o assento é liberado automaticamente e volta a ficar disponível.
5. **Given** um pagamento recusado, **When** a recusa é registrada, **Then** o assento é liberado e nenhuma compra é confirmada.
6. **Given** o usuário seleciona vários ingressos e um deles está indisponível, **When** ele tenta reservar, **Then** nenhum ingresso é reservado e ele é informado de quais itens falharam.

---

### Edge Cases

- Dois usuários reservam o mesmo assento no **mesmo instante**.
- Pagamento aprovado **após** a reserva expirar (assento já liberado).
- Notificação de pagamento entregue **mais de uma vez** ou **fora de ordem**.
- Pico de **10 milhões de usuários** simultâneos no instante de abertura das vendas.
- Bots tentando reservar em massa (scalping).
- Evento **esgotado** (todos os assentos vendidos ou reservados).
- Esgotamento de área de pista/GA (as vendas da área não podem exceder a capacidade total).
- Cancelamento/estorno após a compra efetivada (fora do MVP — ver Assumptions).
- Atualização do catálogo após **falha na sincronização** da busca.
- Evento cancelado ou alterado enquanto há reservas ativas.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O usuário DEVE poder **visualizar** a listagem e o detalhe de eventos, incluindo mapa de assentos, preços e disponibilidade.
- **FR-002**: O usuário DEVE poder **pesquisar** eventos por palavra-chave com tolerância a erros de digitação e variações de palavra, com filtros por data, tipo de evento e local/cidade.
- **FR-003**: O usuário DEVE poder **comprar** ingressos: reserva temporária (atômica quando são vários itens), pagamento e efetivação.
- **FR-004**: O sistema DEVE garantir a **integridade da venda** — o mesmo assento numerado nunca é vendido mais de uma vez, a venda de pista/GA nunca excede a capacidade da área, e toda venda confirmada é registrada de forma **durável**.
- **FR-005**: O sistema DEVE refletir no catálogo e na busca as alterações de eventos e de disponibilidade em **tempo quase-real** (segundos).
- **FR-006**: O sistema DEVE fornecer uma **interface de integração estável e versionada** para clientes/parceiros, com contratos documentados.

### Non-Functional Requirements

- **RNF-001 (Escala)**: Suportar **10 milhões de usuários simultâneos** em dias de grandes eventos.
- **RNF-002 (Anti-bot / Equidade)**: **Mitigar compras automatizadas (bots)** com fila justa e limite de requisições por usuário; **não beneficiar** geolocalizações privilegiadas (fila global justa).
- **RNF-003 (Busca inteligente)**: Tolerar **erros de digitação** e **variações de palavra** na pesquisa.
- **RNF-004 (Consistência/Concorrência)**: Impedir que dois usuários reservem o **mesmo ingresso** (assento numerado ou vaga de pista/GA), sem exceder a capacidade.
- **RNF-005 (Latência)**: Busca com **<500ms** (percentil 95).
- **RNF-006 (Proporção de leitura)**: Suportar leitura:escrita **100:1**.
- **RNF-007 (Disponibilidade)**: Operar em **alta disponibilidade** (sem ponto único de falha).
- **RNF-008 (Observabilidade)**: Logs, métricas e rastreamento de requisições **estruturados e disponíveis** em todos os componentes.
- **RNF-009 (Segurança)**: Segredos/credenciais **fora do código-fonte**; dados de pagamento **não armazenados** pela plataforma; mutações **idempotentes**.

### Key Entities

- **User** — `id`, `name`, `birthdate`, `email`. Representa o comprador.
- **Event** — `id`, `description`, `date`, `type`, `artist`, `status`, `seatMap[]` (assentos numerados **e** áreas de pista/GA). O catálogo de eventos.
- **Venue** — `id`, `description`, `address`, `city`, `capacity`. O local físico do evento.
- **Ticket** — `id`, `event_id`, `seat` (obrigatório p/ assento numerado; vazio p/ pista/GA), `area`, `type`, `price`, `quantity` (p/ pista/GA), `status` (`available` | `unavailable` | `booked`). A unidade de venda.
- **Booking** — `id`, `ticket_id`, `user_id`. O pedido efetivado (venda).
- **Reservation** (transitória) — `ticket_id`, `user_id`, `expires_at` (expira em 7 minutos). Estado volátil com expiração automática, não persistido.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Usuários obtêm resultado de busca e detalhe de evento em **<500ms (percentil 95)** sob carga de leitura 100:1.
- **SC-002**: **Zero venda dupla** — nenhum assento é vendido/reservado duas vezes sob 1.000.000 de tentativas simultâneas.
- **SC-003**: A plataforma suporta **10 milhões de usuários simultâneos** na abertura de vendas, sem degradação perceptível da experiência dos compradores.
- **SC-004**: **≥90%** das buscas com erro de digitação ou variação de palavra retornam o evento esperado entre os primeiros resultados.
- **SC-005**: Disponibilidade agregada **≥99,9%**.

---

## Assumptions

- **Escopo do MVP**: cancelamento/estorno pós-compra e reembolso estão **fora do MVP** (podem ser feature posterior).
- **Pagamento**: usa um **processador de pagamento terceirizado** (cartão de crédito); a plataforma não armazena dados de cartão (conformidade PCI-DSS delegada ao provedor).
- **Autenticação**: existe um mecanismo de identidade na borda; o MVP assume um identificador de usuário já autenticado — detalhamento de SSO/provedor de identidade é incremental.
- **Borda / Anti-bot**: a proteção contra picos e bots usa um **serviço de fila virtual na borda** (provedor externo em produção; simulada em desenvolvimento). No MVP local, a mitigação de bot é **fila justa + limite de taxa**; a detecção sofisticada fica delegada à borda em produção.
- **Implantação alvo**: ambiente local containerizado (WSL Ubuntu + Docker), com configuração 12-factor (configuração via ambiente, processo stateless).
- **Dados**: catálogo populado via script de importação/seed; a criação/edição administrativa de eventos é feita por **API protegida** (sem interface de backoffice no MVP).
- **Localização**: conteúdo e busca em **português (pt-BR)**.
- **Entrega do ingresso**: confirmação em tela + consulta do pedido; **sem** envio por e-mail no MVP.
