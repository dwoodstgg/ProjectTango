-- Per-role hour allocations under a project budget (e.g. Lead Developer 300h, PM 10h).
-- Allocations are in hours; the dollar value derives from the rate card. A project can carry
-- role allocations alongside a fixed-dollar budget — the two levels track independently.
-- The budget's overall `hours` defaults to the sum of these allocations when not set explicitly
-- (handled in the service), so ck_budgets_has_target still holds without change.

CREATE TABLE budget_role_allocations (
    id        uuid PRIMARY KEY,
    budget_id uuid NOT NULL REFERENCES budgets (id) ON DELETE CASCADE,
    role_id   uuid NOT NULL REFERENCES roles (id),
    hours     numeric(9,2) NOT NULL CHECK (hours >= 0),

    UNIQUE (budget_id, role_id)
);

CREATE INDEX ix_budget_role_allocations_budget ON budget_role_allocations (budget_id);
