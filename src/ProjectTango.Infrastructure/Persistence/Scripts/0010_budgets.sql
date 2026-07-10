-- Project budgets (design-doc §5.2, §6.2). One budget per project; a dollar amount,
-- an hours cap, or both. alert_thresholds are % burn points that notify the PM.
-- Every change is captured in budget_revisions (who, when, old → new, reason) so the
-- money trail is auditable — budget rows are updated in place, history lives in revisions.

CREATE TABLE budgets (
    id               uuid PRIMARY KEY,
    project_id       uuid NOT NULL UNIQUE REFERENCES projects (id),
    type             text NOT NULL CHECK (type IN ('fixed_fee', 'time_and_materials_cap', 'hours_cap')),
    amount           numeric(12,2) CHECK (amount IS NULL OR amount >= 0),
    hours            numeric(9,2)  CHECK (hours IS NULL OR hours >= 0),
    alert_thresholds int[] NOT NULL DEFAULT '{50,75,90}',
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),

    -- A budget must constrain at least one dimension.
    CONSTRAINT ck_budgets_has_target CHECK (amount IS NOT NULL OR hours IS NOT NULL)
);

CREATE TABLE budget_revisions (
    id          uuid PRIMARY KEY,
    budget_id   uuid NOT NULL REFERENCES budgets (id),
    revised_by  uuid NOT NULL REFERENCES employees (id),
    revised_at  timestamptz NOT NULL DEFAULT now(),

    -- Snapshot of the values before this change (all null on the first revision).
    from_type   text CHECK (from_type IS NULL OR from_type IN ('fixed_fee', 'time_and_materials_cap', 'hours_cap')),
    from_amount numeric(12,2),
    from_hours  numeric(9,2),

    -- Snapshot of the values this revision set.
    to_type     text NOT NULL CHECK (to_type IN ('fixed_fee', 'time_and_materials_cap', 'hours_cap')),
    to_amount   numeric(12,2),
    to_hours    numeric(9,2),

    reason      text
);

CREATE INDEX ix_budget_revisions_budget ON budget_revisions (budget_id, revised_at DESC);
