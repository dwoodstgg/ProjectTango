-- Rate cards: effective-dated hourly rates per (project, billing role).
-- Rate changes are NEW rows (design rule 2) — the previous row gets closed,
-- never updated in place. Invoice lines snapshot the rate permanently later.

CREATE EXTENSION IF NOT EXISTS btree_gist; -- uuid equality inside the exclusion constraint

CREATE TABLE project_rate_cards (
    id             uuid PRIMARY KEY,
    project_id     uuid NOT NULL REFERENCES projects (id),
    role_id        uuid NOT NULL REFERENCES roles (id),
    hourly_rate    numeric(10,2) NOT NULL CHECK (hourly_rate >= 0),
    effective_from date NOT NULL,
    effective_to   date CHECK (effective_to IS NULL OR effective_to >= effective_from),

    -- No two rates for the same (project, role) may overlap in time.
    CONSTRAINT ex_rate_cards_no_overlap EXCLUDE USING gist (
        project_id WITH =,
        role_id WITH =,
        daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&
    )
);

CREATE INDEX ix_rate_cards_project_role ON project_rate_cards (project_id, role_id, effective_from);

-- Assignments: project membership. One row per person per project; ending an
-- assignment sets end_date (soft), re-adding the person reopens the same row.
-- default_billing_role_id is a UI pre-selection only — the authoritative billing
-- role lives on each time entry.

CREATE TABLE project_assignments (
    id                      uuid PRIMARY KEY,
    project_id              uuid NOT NULL REFERENCES projects (id),
    employee_id             uuid NOT NULL REFERENCES employees (id),
    default_billing_role_id uuid REFERENCES roles (id),
    start_date              date,
    end_date                date CHECK (end_date IS NULL OR start_date IS NULL OR end_date >= start_date),
    UNIQUE (project_id, employee_id)
);

CREATE INDEX ix_assignments_employee ON project_assignments (employee_id);
