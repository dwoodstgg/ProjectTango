-- Correcting a mistaken rate row. Rates stay append-only for real rate CHANGES
-- (design rule 2), but a data-entry error on a row that has not yet priced any INVOICED
-- time is not "re-pricing history" — it's fixing bad data. The service layer enforces the
-- "no invoiced time" guard and audits every correction/deletion.
--
-- Deletion is soft (design rule 11) so the audit trail survives. The no-overlap exclusion
-- is narrowed to live (non-deleted) rows so a corrected/removed row can be re-added in the
-- same window, and made DEFERRABLE so a multi-statement correction (shift a row's start
-- while re-closing its predecessor) can reach a valid final state within one transaction
-- without tripping on the intermediate step.

ALTER TABLE project_rate_cards ADD COLUMN deleted_at timestamptz;

ALTER TABLE project_rate_cards DROP CONSTRAINT ex_rate_cards_no_overlap;

ALTER TABLE project_rate_cards ADD CONSTRAINT ex_rate_cards_no_overlap EXCLUDE USING gist (
    project_id WITH =,
    role_id WITH =,
    daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&
) WHERE (deleted_at IS NULL) DEFERRABLE INITIALLY IMMEDIATE;
