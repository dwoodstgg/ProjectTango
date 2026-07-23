-- Service contracts are budgeted as a single total contract amount (2026-07-23 revision of
-- decision #22): the budget form asks for the total directly instead of a monthly amount,
-- and any monthly breakdown is derived at reporting time (total / contract months). The
-- monthly_amount column is dropped; budgets.amount already holds the contract total for
-- every service contract saved under the old scheme, so no data movement is needed.

ALTER TABLE budgets DROP COLUMN monthly_amount;
