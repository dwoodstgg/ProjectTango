-- Dedupe store for budget threshold alerts (design §6.2). One row per (budget, alert_key)
-- records that we've already emailed for a crossing, so a threshold notifies once — not on
-- every subsequent time-entry save. alert_key is 'pct:<n>' for a configured threshold or
-- 'overrun' for going over budget. Rows are cleared when the budget is revised (re-arm), so
-- a raised budget can fire its thresholds again.

CREATE TABLE budget_alerts (
    id           uuid PRIMARY KEY,
    budget_id    uuid NOT NULL REFERENCES budgets (id),
    alert_key    text NOT NULL,
    burn_percent numeric(6,2) NOT NULL,
    notified_at  timestamptz NOT NULL DEFAULT now(),

    UNIQUE (budget_id, alert_key)
);
