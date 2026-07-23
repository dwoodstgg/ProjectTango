# Crosscheck — Claude Code Context

Time tracking, project budgeting, and invoicing app for The Geospatial Group.
**Read `design-doc.md` in the repo root before making architectural changes — it is the source of truth.**

## Stack (decided — do not substitute)
- .NET 10, ASP.NET Core (single host: MVC/Razor UI + `/api/v1` REST controllers)
- Bootstrap 5.3.x (latest) for all UI
- PostgreSQL via **Dapper + Npgsql — NO EF Core / no ORM**. Schema is versioned as plain SQL scripts run by DbUp (embedded resources in `Infrastructure/Persistence/Scripts/`, journaled in `schemaversions`). Apply with `dotnet run --project src/Crosscheck.Web -- migrate`; Development auto-migrates on startup (`Database:MigrateOnStartup`). Local dev DB: native PostgreSQL on 5432 (docker-compose fallback on 5433)
- Auth: Microsoft Entra ID, single tenant (thegeospatialgroup.com), Microsoft.Identity.Web (cookies for UI, JWT bearer for API)
- Excel import: ClosedXML. PDF generation: QuestPDF
- Hosting target: AWS (ECS Fargate, RDS PostgreSQL, S3, SQS)
- Tests: xUnit; integration tests use Testcontainers Postgres

## Solution layout
```
Crosscheck.slnx
├── src/Crosscheck.Domain          entities, enums, domain rules (no deps)
├── src/Crosscheck.Application     services, use cases, validation, interfaces
├── src/Crosscheck.Infrastructure  EF Core/Npgsql, migrations, S3, email, Excel import, PDF
├── src/Crosscheck.Web             ASP.NET Core host: Razor UI + /api/v1 + auth
└── tests/{UnitTests,IntegrationTests}
```
Local path: `C:\Users\dcwoo\source\repos\dwoodstgg\Crosscheck`
Remote: https://github.com/dwoodstgg/Crosscheck

## Domain rules that must never be violated
1. Money is `numeric`/`decimal`, never float. Timestamps UTC. USD only. Tax column exists but is always 0 in v1.
2. Rates live on `project_rate_cards` (project + billing role) and are fixed for the life of the project — one live row per (project, billing role), set from the contract (partial `UNIQUE (project_id, role_id) WHERE deleted_at IS NULL`). Rates are not effective-dated: editing a rate is for fixing a mistaken entry, and is locked once the rate has priced invoiced time. Invoice lines snapshot the rate permanently (independent of later rate-card edits). No overtime multipliers.
3. An employee's company roles (permissions) are many-to-many via `employee_roles`; permissions are the UNION of held roles. The **billing role is chosen per time entry** (`time_entries.billing_role_id`); `project_assignments.default_billing_role_id` is a UI default only. Rate resolution: (module, entry's role) override → module-wide override (`project_module_rates.role_id NULL`) → (project, entry's billing role) rate card.
4. Admin role bypasses resource-level checks; every Admin override is audit-logged. Last remaining Admin cannot remove their own Admin role.
5. Time entries: open → approved → invoiced (NO submission step). **Entries auto-approve on save** (small-shop default): a billable entry approves as soon as a rate covers it (module override else project rate card) — or unconditionally on a fixed-price module (billing comes from the agreed amount, not rates); otherwise it stays `open` until a rate is added; non-billable time always approves. The owner edits freely — including back-dating forgotten days — until the semi-monthly `timesheet_periods` window is closed or the entry is invoiced (approval no longer locks owner edits). Ops/Admin closing the window locks owner edits (close/reopen audited). The manual approval path (ApprovalService — adjust `hours_billed`, un-approve) stays available for when it's wanted. Invoiced is never editable — void the invoice instead (only `issued` invoices can be voided, never `paid`; voiding returns entries to `approved`).
6. Entries carry `hours_worked` and `hours_billed`. `hours_billed` defaults to worked and may be adjusted ONLY by an approver (approval is a billing decision — worked 8, bill 6); with auto-approval that adjustment means an Ops/PM un-approve + re-approve via ApprovalService. `hours_worked` is only ever set by the owner.
7. A time entry requires an active (not-removed) project assignment for that employee. Assignments are not date-ranged — an employee stays active for the life of the project; removing sets `end_date` (soft-deactivate) when time has been logged, or hard-deletes when none has.
8. Every project has a **`project_type`** (design decision #22) — `hourly` (billed for hours worked), `fixed_rate` (agreed price), `service_contract` (set timeframe, billed monthly), or `internal` (assigned internal tasks — never billed, no budget, no dollar amount; added 2026-07-23, migration 0021). The type drives what the budget form asks for: hourly = optional dollar/hours caps; fixed rate = the contract amount; service contract = the **total contract amount** for the whole engagement, stored directly in `budgets.amount` (revised 2026-07-23 — the old `monthly_amount` column was dropped by migration 0020) so burn/alerts measure the whole engagement and heavy/light months average out; a monthly breakdown (total / `BudgetService.ContractMonths`) is derived at reporting time only; internal = nothing (`SetBudgetAsync` rejects internal projects, and the UI hides dates, billing overrides, and the budget block). Entries on an internal project are non-billable (auto-approve without a rate, description optional but still available). The budget row's `type` mirrors the project type at save time. **"T&M" terminology is retired — never use it in UI, code, or docs; say "hourly".**
9. Projects ARE work orders, and a work order's budget breaks into **`project_modules`** (design decision #21) — called "modules" or "milestones" per project (`projects.breakdown_label`, display only — switched instantly from the Modules/Milestones card, not the budget form; milestones show their sort number). Each module has an hour budget (flat `hours` OR Σ per-role `project_module_allocations`), optional rate overrides, and an optional agreed `amount`: set = fixed-price (bills exactly that; hours are internal budget; entries approve without a rate), null = hourly as incurred (how supplemental/out-of-scope hours work). Once a project has live modules, new entries MUST pick one (`time_entries.module_id`, one entry per employee+project+module+day, NULLS NOT DISTINCT); pre-module entries are a read-only "unassigned" bucket. Modules soft-delete only. Burn + threshold alerts run overall, per role, AND per module (`module:<id>:*` keys). The old standalone fixed-fee `milestones` table design is absorbed here (ready_to_bill workflow arrives with invoicing).
10. Projects are NEVER auto-closed by budget exhaustion. Overrun is allowed and flagged. Close-out is an explicit audited action; closed projects block new time but remain reportable and invoiceable (WIP). Ops/Admin can reopen.
11. Invoice numbers (INV-YYYY-NNNN) are never reused; NNNN runs continuously across years (no annual reset); voided invoices keep their number.
12. Nothing is hard-deleted: soft deletes / status changes only. Audit log on financial and permission mutations.
13. Authorization is enforced in the API/service layer, never only in views.
14. Employees include W-2 staff and 1099 subcontractors (`employees.employment_type`); both log time identically and appear in per-person reporting. Employee records may exist before Entra sign-in (`entra_oid` null, matched by tenant email).

## Seed data
- `dwoods@thegeospatialgroup.com` seeded as initial Admin (matched by Entra `oid` after first sign-in; email as bootstrap key).
- Roles: Developer, Project Manager, Operations Manager, Admin (Admin: is_billable=false, is_system_admin=true).
- New tenant users get NO roles until granted.
- Internal client **The Geospatial Group** — never invoiced; reserved for internal non-billable projects if ever needed. No seeded projects (the old `INT-LEAVE` leave project was retired by migration 0018).
- `company_holidays` table: admin-managed holiday calendar. A weekend holiday is **observed** on the nearest weekday (federal rule: Sat → preceding Fri, Sun → following Mon; computed by `WorkCalendar.ObservedDate`, never stored) — the observed day is what greys out and auto-credits. Years roll forward via an audited "copy from previous year" that recomputes floating federal holidays by nth-weekday rule (`HolidayRecurrence`); already-present dates are skipped. **Leave is never logged as time** — the timesheet derives it: an untouched (observed) holiday auto-credits 8h of Holiday leave, and Personal leave counts down from the month's expected hours (workdays × 8) as time is entered; hours beyond that are Extended Work Week.

## Time entry cadence
Monthly timesheet grid (projects × days, mirrors the Excel workbook). No submission: employees record time as they go and edit until Ops/Admin closes the semi-monthly window (`timesheet_periods`: 1st–15th, 16th–EOM). Billing role selected per entry. A project with modules/milestones shows **one grid row per section** (flow: project → module → hours → description; two sections can be logged the same day) and new entries must pick one.

## Excel timesheet import (Phase 2)
Company workbook format (see design-doc.md §6.6; sample at `Samples/2026 Don Woods timesheet.xlsx`): `Yearly Info` sheet (employee, year, job list, holidays); two sheets per month — the calendar sheet JAN–DEC where hours are entered (project rows × day columns, leave rows, totals) and the paired `*-DESC` sheet that receives those hours rolled up and holds the typed per-project-per-day work descriptions. Hours are authoritative on the calendar, descriptions on `-DESC` (join by project+date, warn on mismatch). Extra client-specific calendars (e.g., `'JAN '` vs `'JAN'`, "For MDEQ") are derived and skipped. Parser must skip #REF!/#VALUE! artifacts and zero rows; dedupe on employee+date+project; map spreadsheet labels to projects with saved mappings; importer supplies each person's tenant email (creates employee record if missing, entra_oid linked on first sign-in); workbook leave rows are SKIPPED (leave is derived on the timesheet, never stored as entries); commit as approved entries with import_id; support rollback until invoiced.

## Roadmap
Phase 1 (now): scaffold solution, Entra auth, employees/roles, clients, projects, rate cards, assignments, time entry + approval, project dashboard.
Phase 2: budgets + alerts, invoicing + PDF, WIP report, Excel import, close-out/reopen.
Phase 3: reporting suite, exports, utilization, forecasting.
Phase 4: mobile/desktop clients against /api/v1, accounting integration.

## Conventions
- API: versioned `/api/v1`, cursor pagination, RFC 7807 problem+json errors, idempotency keys on invoice issuance, OpenAPI generated from code.
- Database: snake_case tables/columns, uuid PKs, enums as text + CHECK constraints. Schema changes are a NEW numbered DbUp script — never edit a script that has shipped.
- Dapper: `DefaultTypeMap.MatchNamesWithUnderscores = true` (set in AddInfrastructure). Email lookups must cast the parameter (`email = @email::citext`) — a text-typed parameter degrades citext equality to case-sensitive. Repositories live in Infrastructure and implement Application interfaces.
- Seeded well-known ids (roles, bootstrap Admin, internal client) live in `Infrastructure/Persistence/SeedData.cs` and must match `0002_seed_phase1.sql`.
- Every feature: service-layer logic + unit tests; controllers thin.
- Bootstrap components only — no other CSS frameworks.
