-- Per-user UI preferences that sync across devices (e.g., the last-used timesheet layout).
-- A small key/value store scoped to an employee; values are short UI strings, not audited
-- financial data. One row per (employee, key); writes upsert.
CREATE TABLE employee_preferences (
    employee_id uuid        NOT NULL REFERENCES employees (id),
    pref_key    text        NOT NULL,
    pref_value  text        NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (employee_id, pref_key)
);
