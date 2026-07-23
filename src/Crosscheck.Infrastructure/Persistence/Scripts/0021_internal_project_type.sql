-- 'internal' project type (2026-07-23 addition to decision #22): random assigned internal
-- tasks. Never billed, no budget, no dollar amount — entries on an internal project are
-- non-billable and auto-approve without a rate. The budgets/budget_revisions type CHECKs
-- are left unchanged: setting a budget on an internal project is rejected in the service
-- layer, so an internal budget row can never exist.

ALTER TABLE projects DROP CONSTRAINT projects_project_type_check;
ALTER TABLE projects ADD CONSTRAINT projects_project_type_check
    CHECK (project_type IN ('hourly', 'fixed_rate', 'service_contract', 'internal'));
