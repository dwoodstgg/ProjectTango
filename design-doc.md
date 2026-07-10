# Project Time & Budget Management System
## Design Document — v0.4 (Draft)

**Owner:** dwoods@thegeospatialgroup.com
**Date:** July 8, 2026
**Status:** Draft — open questions resolved 2026-07-08 (see §10)

---

## 1. Overview

A web-based application for a software company to track time spent on client projects, manage project budgets, and generate invoices and reports. The system is architected API-first so that native mobile and desktop clients can be added later without re-platforming.

### 1.1 Goals
- Track time by employee, role, project, and task
- Manage project budgets and monitor burn against them in real time
- Support per-project, per-role configurable hourly rates
- Support fixed-fee projects billed against project-level milestones, with out-of-scope work billed at its own hourly rate
- Track both W-2 employees and 1099 subcontractors (~10 employees + a few subs), with total-time reporting per person
- Manage clients with multiple projects, each billable at different rates
- Generate invoices and progress reports at any point during a project
- Import historical/current timesheets from the company's existing Excel workbook format
- Support an explicit project close-out process (never automatic — overruns are expected and must be visible, not blocking)
- Authenticate users through the company's Microsoft Entra ID (single tenant)

### 1.2 Non-Goals (initial release)
- Multi-tenancy / SaaS for external companies
- Payroll processing (rates here are billing rates, not compensation)
- Native mobile or desktop apps (architecture must support them, but v1 is web only)
- Payment collection (invoices are generated; payment happens outside the system)

---

## 2. Users & Roles

| Role | Description | Key Capabilities |
|---|---|---|
| **Developer** | Logs time against assigned projects/tasks | Enter/edit own time, view own timesheets, view assigned projects |
| **Project Manager** | Owns one or more projects | Everything a Developer can do, plus: manage project budgets, set project rates, assign team members, approve time, generate project reports and draft invoices |
| **Operations Manager** | Company-level oversight | Everything a PM can do across all projects, plus: manage clients, manage employees and role assignments, close/reopen timesheet period windows, finalize/send invoices, company-wide reporting, system configuration |
| **Admin** | Full system access | Everything, without restriction: all of the above plus user/role administration, system settings, audit log access, void/reopen invoices, and any resource-level override (e.g., edit any project regardless of PM assignment) |

Notes:
- An employee can hold **multiple company roles** simultaneously (e.g., PM + Developer, or Ops Manager + Admin). Effective permissions are the **union** of all held roles.
- An employee's **billing role is chosen per time entry** — the kind of work can change day to day, so each entry records the role it bills under (the project assignment carries an optional default that pre-selects in the UI). Rates resolve from (project, entry's billing role, entry date); company roles are permissions only. (See §5.)
- "Employees" includes both W-2 staff and 1099 subcontractors (`employment_type`). Both log time identically, authenticate through the tenant, and appear in per-person total-time reporting.
- Role model is designed to be extensible (e.g., adding QA, Designer later) — roles are data, not code. Admin is the only role with hardcoded "bypass all resource checks" semantics.

---

## 3. High-Level Architecture

```
┌─────────────┐   ┌──────────────┐   ┌───────────────┐
│  Web SPA     │   │ Mobile (future)│  │ Desktop (future)│
└──────┬──────┘   └──────┬───────┘   └───────┬───────┘
       │                 │                   │
       └────────── HTTPS / OIDC ─────────────┘
                        │
              ┌─────────▼──────────┐
              │  REST API (JSON)   │  ← single source of truth
              │  App Service Layer │
              └─────────┬──────────┘
                        │
        ┌───────────────┼────────────────┐
        │               │                │
┌───────▼──────┐ ┌──────▼───────┐ ┌──────▼────────┐
│ PostgreSQL   │ │ Background   │ │ Object Storage │
│ (RDS)        │ │ Jobs (invoices,│ │ (S3: PDFs,    │
│              │ │ reports)      │ │ exports)      │
└──────────────┘ └──────────────┘ └───────────────┘
```

### 3.1 Key Architectural Decisions

**API-first.** All functionality — including the web UI — goes through a versioned REST API (`/api/v1/...`). The web app is just the first client. Future mobile/desktop apps consume the same API with the same auth flow. No business logic lives in the frontend.

**Backend: .NET 10 (ASP.NET Core).** ✅ *Decided.* First-class Entra ID integration via Microsoft.Identity.Web. A single ASP.NET Core solution hosts both the `/api/v1` endpoints and the web UI.

**Data access: Dapper + Npgsql — no ORM.** ✅ *Decided (2026-07-08, revised from EF Core).* Repositories in Infrastructure use Dapper over Npgsql. The schema is plain SQL: numbered scripts embedded in the Infrastructure assembly, applied in order by **DbUp** and journaled in `schemaversions`. Run via `dotnet run --project src/ProjectTango.Web -- migrate` (CI/deploy) or automatically at startup in Development.

**Frontend: ASP.NET Core MVC (Razor) + Bootstrap 5.3.x (latest).** Server-rendered Razor views styled with Bootstrap, with targeted JavaScript/fetch against the API for the interactive pieces (timesheet grid, dashboards). All views call the same application services the API exposes, so nothing is UI-only. If we later want richer interactivity, Blazor components can be added incrementally without changing the backend.

**Database:** PostgreSQL on **Amazon RDS** (or Aurora PostgreSQL if we want easier scaling later). Schema managed by DbUp-versioned SQL scripts. Local dev uses the developer's native PostgreSQL install (browsable in pgAdmin); docker-compose provides a fallback on port 5433.

**Solution structure** (local path `C:\Users\dcwoo\source\repos\dwoodstgg\ProjectTango`, remote `https://github.com/dwoodstgg/ProjectTango`):

```
ProjectTango.slnx
├── src/
│   ├── ProjectTango.Domain/          Entities, enums, domain rules (no dependencies)
│   ├── ProjectTango.Application/     Services, use cases, validation, interfaces
│   ├── ProjectTango.Infrastructure/  Dapper repositories (Npgsql), DbUp SQL
│   │                                 migrations, S3, email, Excel import
│   │                                 (ClosedXML), PDF (QuestPDF)
│   └── ProjectTango.Web/             ASP.NET Core host: MVC UI (Bootstrap 5.3)
│                                     + /api/v1 controllers + auth
├── tests/
│   ├── ProjectTango.UnitTests/
│   └── ProjectTango.IntegrationTests/ (Testcontainers Postgres)
└── .github/workflows/                CI: build, test, migrate, deploy
```

### 3.2 AWS Deployment (v1)

| Concern | Service |
|---|---|
| App hosting (UI + API) | ECS Fargate (containerized ASP.NET Core app) behind an ALB |
| Static assets | Served by the app (Bootstrap bundled); CloudFront in front of the ALB for TLS/caching |
| Database | RDS PostgreSQL (Multi-AZ for prod) |
| Secrets | AWS Secrets Manager |
| File storage | S3 (invoice PDFs, report exports) |
| Background jobs | SQS + a worker task on ECS (invoice/report generation) |
| DNS/TLS | Route 53 + ACM |
| CI/CD | GitHub Actions → ECR → ECS deploy |
| Monitoring | CloudWatch (logs, alarms), optionally Sentry for app errors |

Environments: `dev`, `staging`, `prod`. Infrastructure as code with Terraform or AWS CDK.

---

## 4. Authentication & Authorization

### 4.1 Authentication — Microsoft Entra ID (single tenant)
- App registered in the **thegeospatialgroup.com** Entra ID tenant; only accounts in that tenant can sign in.
- **Web app:** OIDC Authorization Code flow with PKCE via **Microsoft.Identity.Web** (cookie session for the Razor UI).
- **API:** Also accepts Entra-issued JWT bearer tokens (issuer + audience checks) — this is the path future mobile/desktop clients use. Same app registration model; MSAL has libraries for iOS/Android/desktop, so no auth rework is needed later.

### 4.2 User Provisioning
- First sign-in creates a local `employee` record linked to the Entra `oid` (object ID) — email is stored but `oid` is the stable key.
- New users land with **no roles** until an Admin or Operations Manager assigns one or more (prevents anyone in the tenant from self-provisioning access). Exception: `dwoods@thegeospatialgroup.com` is seeded as the initial **Admin**.
- Optional later: sync role assignment from Entra security groups.

### 4.3 Authorization
- Role-based access control enforced in the API layer (middleware/guards), never in the client.
- Users can hold multiple roles; a request is authorized if **any** held role grants the permission (union semantics). The JWT/user context carries the full role set.
- Resource-level checks on top of roles: e.g., a PM can only manage projects where they are the assigned PM; developers can only edit their own time entries. **Admin bypasses all resource-level checks** — but every Admin override is written to the audit log.
- Guard rails: at least one active Admin must exist at all times (an Admin cannot remove their own Admin role if they are the last one).

---

## 5. Data Model

### 5.1 Entity Relationship Overview

```
Client 1──* Project 1──* ProjectRateCard (role → rate)
                 │
                 ├──* ProjectAssignment (employee + optional default billing role)
                 │
                 ├──* Milestone (fixed-fee billing checkpoints)
                 │
                 ├──* Task (optional breakdown)
                 │
                 └──* TimeEntry *──1 Employee
                 │            *──1 Role (billing role, chosen per entry)
                 │            *──1 Milestone (optional, fixed-fee in-scope work)
                 │
                 ├──1 Budget (+ BudgetRevision history)
                 │
                 └──* Invoice 1──* InvoiceLine *──* TimeEntry

Employee *──* Role   (via employee_roles — company roles, permissions)
```

### 5.2 Core Tables

**employees**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| entra_oid | text unique null | Entra object ID (stable identity key); null until first sign-in for import-created records |
| email | citext unique | Tenant address — also the bootstrap key linking import-created records to Entra on first sign-in |
| display_name | text | |
| employment_type | enum | employee, subcontractor (1099) — both log time identically; drives per-person reporting |
| is_active | boolean | Soft-deactivate on offboarding |
| created_at / updated_at | timestamptz | |

**roles**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| name | text unique | 'Developer', 'Project Manager', 'Operations Manager', 'Admin' — extensible |
| is_billable | boolean | Admin: false (system role, not a billing role) |
| is_system_admin | boolean | True only for Admin — grants bypass semantics |

**employee_roles** — many-to-many; an employee can hold multiple company roles
| Column | Type | Notes |
|---|---|---|
| employee_id | uuid FK → employees | |
| role_id | uuid FK → roles | |
| granted_by | uuid FK → employees | |
| granted_at | timestamptz | |
| PK (employee_id, role_id) | | Grants/revocations also written to audit_log |

**clients**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| name | text | |
| billing_contact_name / email | text null | **Default** billing contact — a project may override (see below) |
| billing_address | jsonb null | Default billing address; project may override |
| payment_terms_days | int null | Default terms, e.g., 30 for Net-30; project may override |
| is_active | boolean | |

> One seeded internal client — **The Geospatial Group** — owns internal non-billable projects (leave, admin time). Never invoiced; keeps daily totals reconcilable and feeds utilization reporting.

> **Billing is per project, with the client as a fallback default.** A client works with multiple departments/contacts (e.g., three concurrent MDWFP projects, three different contacts), so the authoritative billing contact, address, and payment terms live on the **project** (all nullable). A null project field inherits the client's value; the effective value for invoicing resolves **field-by-field as project → client → default** (payment terms default to 30 when neither is set). Client-level fields are pure defaults, not requirements.

**projects**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| client_id | uuid FK | |
| name | text | |
| code | text unique | Short code for invoices/reports, e.g., GEO-014 |
| status | enum | draft, active, on_hold, **closed**, archived — status changes are always explicit user actions (see §6.5) |
| closed_at / closed_by | timestamptz / uuid FK | Set only by the close-out action |
| project_manager_id | uuid FK → employees | |
| start_date / end_date | date | |
| currency | char(3) | USD default |
| billing_contact_name / email | text null | Overrides the client's default contact when set |
| billing_address | jsonb null | Overrides the client's default address when set |
| payment_terms_days | int null | Overrides the client's default terms when set |

**project_rate_cards** — the heart of "rates configurable per role at the project level"
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| project_id | uuid FK | |
| role_id | uuid FK | |
| hourly_rate | numeric(10,2) | |
| effective_from | date | Rate changes mid-project are new rows, not updates |
| effective_to | date null | Open-ended until superseded |

> Rate resolution: a time entry bills at the rate for (project, the **entry's** billing role) effective on the entry's date. Historical entries are never re-priced when rates change — and invoiced entries lock their rate permanently (denormalized onto the invoice line). No overtime multipliers: "Extended Work Week" hours bill at the same stored rate.

**project_assignments** — who is on the project (membership, not billing role)
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| project_id | uuid FK | |
| employee_id | uuid FK | |
| default_billing_role_id | uuid FK → roles null | UI pre-selection only — the authoritative billing role lives on each time entry |
| start_date / end_date | date | |
| unique (project_id, employee_id) | | One membership per person per project |

**budgets**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| project_id | uuid FK unique | |
| type | enum | fixed_fee, time_and_materials_cap, hours_cap |
| amount | numeric(12,2) null | Dollar budget |
| hours | numeric(9,2) null | Hours budget (either/both allowed) |
| alert_thresholds | int[] | e.g., {50, 75, 90} → notify PM at % burn |

**budget_revisions** — audit trail of every budget change (who, when, old → new, reason).

**milestones** — fixed-fee billing checkpoints, defined per project
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| project_id | uuid FK | |
| name | text | |
| amount | numeric(12,2) | Portion of the fixed fee billed when this milestone is invoiced |
| target_date | date null | |
| status | enum | planned, ready_to_bill, billed |
| sort_order | int | |

> On fixed-fee projects, employees attach in-scope entries to a milestone; entries left unattached are **out-of-scope** and bill hourly at their own rate. (Exact out-of-scope rate modeling — single project rate vs. per-role rate-card rows — is still open, see §10.)

**company_holidays** — admin-managed holiday calendar (date, name). Drives holiday rows on timesheets and reconciles imported leave.

**tasks** (optional per project — lightweight breakdown for reporting granularity)
| id, project_id, name, is_billable, status |

**time_entries**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| project_id | uuid FK | |
| task_id | uuid FK null | |
| milestone_id | uuid FK null | Fixed-fee projects: set = in-scope milestone work; null = out-of-scope, bills hourly |
| employee_id | uuid FK | |
| billing_role_id | uuid FK → roles | Chosen per entry — the type of work can change with each entry |
| entry_date | date | |
| hours_worked | numeric(5,2) | Quarter-hour increments enforced in app layer; never altered by approval |
| hours_billed | numeric(5,2) | Defaults to hours_worked; approver may adjust at approval (e.g., worked 8, bill 6) |
| notes | text | Appears on invoice detail |
| is_billable | boolean default true | Seeded from the task's flag when a task is picked; the entry's flag is authoritative |
| status | enum | open, approved, invoiced — no submission step; owner edits freely (incl. back-dating) while `open` and the covering period window is open |
| approved_by / approved_at | | Approval before invoicing (also the hours_billed decision) |
| invoice_line_id | uuid FK null | Set when locked onto an invoice |

**timesheet_periods** — semi-monthly edit windows (1st–15th, 16th–EOM)
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| period_start / period_end | date | |
| status | enum | open, closed |
| closed_by / closed_at | uuid FK / timestamptz | Closing locks owner edits for entries in the window; close and reopen are explicit Ops/Admin actions, both audited |

**invoices**
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| invoice_number | text unique | INV-YYYY-NNNN; NNNN runs continuously across years (never resets), YYYY reflects issue date |
| client_id / project_id | uuid FK | v1: one project per invoice (consolidated client invoices later) |
| period_start / period_end | date | |
| status | enum | draft, issued, paid, void — only **issued** invoices can be voided (never paid); voiding returns time entries to `approved` |
| subtotal / tax / total | numeric(12,2) | Tax column kept but always 0 in v1 |
| issued_at / due_at | timestamptz | Due = issued + client payment terms |
| pdf_s3_key | text | |

**invoice_lines** — grouped by role or by task (configurable): description, quantity (hours), **rate snapshot**, amount, and links back to the constituent time entries.

**audit_log** — append-only record of sensitive mutations (rate changes, budget changes, invoice issuance, role assignments, project close-outs, imports).

**timesheet_imports** — one row per uploaded workbook
| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| employee_id | uuid FK | Employee the timesheet belongs to (parsed from workbook, confirmed by importer) |
| uploaded_by | uuid FK | |
| source_filename | text | e.g., 2026_Don_Woods_timesheet.xlsx |
| s3_key | text | Original file retained for audit |
| year | int | |
| status | enum | uploaded, parsed, mapped, previewed, committed, failed |
| summary | jsonb | Counts, warnings, skipped rows |

**timesheet_import_mappings** — remembers "spreadsheet project label → system project" (e.g., `RICS Dashboard` → project GEO-007) so subsequent imports auto-map.

Imported time entries carry `import_id` (nullable FK on time_entries) so any import can be reviewed or rolled back before entries are approved.

### 5.3 Key Integrity Rules
1. A time entry cannot be created unless the employee has an active assignment on the project.
2. Owners can create, edit, and back-date `open` entries while the covering timesheet period is open; a closed period locks owner edits (Ops/Admin can reopen, audited). `approved` entries require un-approval to change; `invoiced` entries can never be edited — void the (issued) invoice instead, which returns its entries to `approved`.
3. Every (project, billing role) pair used by an hourly-billed time entry must have a rate card row before the entry can be approved. (Milestone-attached entries on fixed-fee projects bill by milestone amount, not hours.)
4. `hours_billed` may only be adjusted by an approver, and only until invoiced; `hours_worked` is never altered by anyone but the entry's owner.
5. Invoice numbers are gapless-enough and never reused; voided invoices retain their number.
6. Money stored as `numeric`, never float. All timestamps in UTC.

---

## 6. Core Workflows

### 6.1 Time Entry

**No submission step.** Employees record time as they go and can edit freely — including back-dating entries they forgot — until the admin closes the period window or the entry is approved.

1. Employee opens the **monthly** timesheet grid (projects × days, matching the current workbook layout), enters hours + notes, and picks the **billing role per entry** (pre-filled from their assignment default). On fixed-fee projects they can optionally attach the entry to a milestone; unattached entries are out-of-scope and bill hourly.
2. Entries **auto-approve on save** (small-shop default, decided 2026-07-09 — see §10 #19): a billable entry moves straight to `approved` as soon as a rate card covers its (project, billing role, date); if none does yet it stays `open` and shows in the approval queue until a rate is added. Non-billable (leave/internal) time always auto-approves. Entries remain owner-editable — auto-approval does not lock them. Periods are **semi-monthly** (1st–15th, 16th–EOM); Ops/Admin **closes the window** after each period ends (`timesheet_periods`), which locks employee edits for those dates. Closing/reopening a window is audited; reopening is how a straggler fixes a missed day after close.
3. The manual approval path stays available (not required by default): an approver can un-approve and re-approve to make the billing decision — `hours_billed` can be adjusted (worked 8, bill 6); `hours_worked` is never changed. Retained so the review step can be re-enabled per policy later.
4. An approver can return an entry for correction with a comment (back to `open`, audited).

### 6.2 Budget Monitoring
- Project dashboard shows: budget, hours/dollars burned (approved/invoiced, with `open` entries shown as "pending"), remaining, burn rate, projected completion vs. budget.
- Automated alerts (email) to PM at configured thresholds; Ops Manager alerted at 90%+ and overrun.

### 6.3 Invoicing
1. PM (or Ops) selects project + date range → system pulls approved, un-invoiced billable entries (quantities use `hours_billed`).
2. Draft invoice generated; lines grouped by role (or task), rates snapshotted.
3. **Fixed-fee projects:** lines come from milestones marked `ready_to_bill` (billed at the milestone amount, not hours); out-of-scope entries add T&M lines at the out-of-scope hourly rate.
4. PM can exclude entries or add manual line items (e.g., expenses — v1.1) before finalizing.
5. Ops Manager issues the invoice → number assigned, PDF rendered (background job), stored in S3, time entries locked to `invoiced`.
6. Status tracked manually (`issued` → `paid`). Only issued invoices can be voided; voiding returns entries to `approved`. Accounting-system integration (QuickBooks etc.) is a future consideration.

### 6.4 Reporting (v1 set)
- **Project status report:** budget vs. actual, hours by role/person, period-over-period burn — exportable to PDF for clients.
- **Utilization report:** billable vs. total hours per person per period — covers W-2 employees and 1099 subcontractors alike, and reports both hours worked and hours billed.
- **Client summary:** all projects for a client, financial rollup.
- **WIP report:** approved-but-uninvoiced value across all projects (Ops).
- All reports exportable to CSV; client-facing ones to PDF.

### 6.5 Project Close-Out (explicit, never automatic)

- Hitting or exceeding a budget **never** changes project status. Overrun is a normal, expected condition: the dashboard flags it (negative remaining, red state, alerts fire), but time entry, approval, and invoicing all continue.
- Close-out is a deliberate action by the PM or Ops Manager (Admin can always):
  1. **Pre-close checklist** shown to the user: unapproved time entries, approved-but-uninvoiced hours (WIP), draft invoices, final budget vs. actual (including overrun amount).
  2. User can proceed anyway — the checklist informs, it doesn't block — but each outstanding item is recorded on the close-out record.
  3. On close: status → `closed`, `closed_at`/`closed_by` set, audit log entry written, a final **project close-out report** is generated (total hours by role/person, budget vs. actual, overrun, invoiced vs. unbilled).
- After close: no new time entries or assignments; existing data remains fully reportable and invoiceable (you can still bill remaining WIP on a closed project).
- **Reopen** is available to Ops/Admin (audited) — because run-over and late work are realities.
- `archived` is a later, separate step that hides the project from default lists; nothing is ever deleted.

### 6.6 Timesheet Import (Excel)

Based on the current company workbook format (e.g., `2026_Don_Woods_timesheet.xlsx`):

**Source format understood as:**
- **Yearly Info** sheet: employee name, year, holiday calendar, and the master job/project list that feeds every monthly sheet.
- **Two sheets per month.** The **calendar sheet** (`JAN`…`DEC`) is where hours are entered: a matrix of project rows × day-of-month columns (quarter-hour granularity), with per-project monthly totals, daily totals, leave rows (Holiday, Personal), Extended Work Week, and weekly totals. The paired **description sheet** (`JAN-DESC`…`DEC-DESC`) receives those hours **rolled up** per project per day, and is where the free-text description of the work is typed.
- **Import roles:** hours are authoritative on the calendar sheet; descriptions are authoritative on the `-DESC` sheet; the parser joins them by (project, date) and warns when the rolled-up hours on `-DESC` disagree with the calendar.
- Workbooks may contain an extra duplicate calendar per month (the sample has both `'JAN '` and `'JAN'`, the latter holding a client-specific "MONTHLY TIME RECORD For MDEQ" block). **The top/first calendar is the employee-entered source of truth; extra client-specific calendars are derived by the current system and are skipped on import.**

**Import pipeline:**
1. **Upload** — Ops/Admin (or PM for their own projects) uploads the .xlsx; original stored in S3.
2. **Parse** — server-side parse with **ClosedXML**. Reads employee + year from Yearly Info, iterates each month's calendar sheet for (project label, date, hours), then joins each cell to its description from the matching `-DESC` sheet by (project, date), warning on hour mismatches between the two. Parser is defensive: skips `#REF!`/`#VALUE!` artifacts, zero-hour rows, blank project slots, and duplicate/auxiliary calendar sheets (dedupes by employee + date + project).
3. **Map** — spreadsheet project labels are matched to system projects: saved mappings first, then fuzzy suggestion, then manual pick (or "create new project") in a mapping UI. Employee matched by **tenant email address**: the importer is prompted for the person's email; if no employee record exists one is created (email-only, `entra_oid` null) and linked to Entra automatically on that person's first sign-in. Works for departed staff who will never sign in. Mappings are remembered for future imports.
4. **Preview** — importer sees a per-month/per-project summary (hours, entry counts, descriptions attached, warnings: days exceeding 24h, entries on a closed project, overlap with already-imported data) before committing.
5. **Commit** — time entries created in `approved` status (✅ decided: historical data is treated as already approved; configurable to `draft` if review is ever wanted), tagged with `import_id`. Duplicate protection: an entry with the same employee + project + date as an existing entry is flagged, never silently doubled.
6. **Rollback** — an entire import can be reversed while none of its entries are invoiced.

**Leave handling:** ✅ decided — Holiday/Personal leave rows import into internal non-billable projects (e.g., `INT-LEAVE`) under the internal client **The Geospatial Group**, so daily totals reconcile with the workbook without polluting client billing. Holidays themselves are admin-configurable (`company_holidays`).

---

## 7. API Design (Representative)

Versioned REST, JSON, JWT bearer auth. Examples:

```
GET    /api/v1/clients                      List clients (Ops)
POST   /api/v1/projects                     Create project (Ops/PM)
GET    /api/v1/projects/:id/dashboard       Budget burn, team, recent activity
PUT    /api/v1/projects/:id/rate-cards      Set role rates (PM/Ops)
POST   /api/v1/projects/:id/assignments     Assign employee (+ optional default billing role)
POST   /api/v1/projects/:id/milestones      Define fixed-fee milestones (PM/Ops)
POST   /api/v1/time-entries                 Create entry (self; billing role per entry; back-dating allowed while window open)
POST   /api/v1/timesheet-periods/:id/close  Close semi-monthly window, lock owner edits (Ops/Admin)
POST   /api/v1/timesheet-periods/:id/reopen Reopen a closed window (Ops/Admin, audited)
POST   /api/v1/projects/:id/approvals       Bulk approve (PM)
POST   /api/v1/projects/:id/invoices        Generate draft invoice
POST   /api/v1/invoices/:id/issue           Finalize + render PDF (Ops)
POST   /api/v1/projects/:id/close           Close out project (PM/Ops/Admin)
POST   /api/v1/projects/:id/reopen          Reopen closed project (Ops/Admin)
POST   /api/v1/imports/timesheets           Upload workbook → parse
GET    /api/v1/imports/timesheets/:id       Parse results + mapping state
POST   /api/v1/imports/timesheets/:id/commit
POST   /api/v1/imports/timesheets/:id/rollback
GET    /api/v1/reports/utilization?from=&to=
```

Conventions: cursor pagination, RFC 7807 problem+json errors, idempotency keys on invoice issuance, OpenAPI spec generated from code (this becomes the contract for mobile/desktop clients).

---

## 8. Non-Functional Requirements

| Area | Target |
|---|---|
| Availability | 99.5% (business-hours critical) |
| Performance | API p95 < 300ms; dashboard < 1s |
| Scale (v1) | ~10–100 employees, hundreds of projects — modest; design for correctness over premature scale |
| Security | TLS everywhere, tenant-restricted auth, least-privilege IAM, encrypted at rest (RDS/S3 default), audit log on financial mutations |
| Backups | RDS automated backups, 30-day retention, point-in-time recovery |
| Data retention | Financial records retained indefinitely; soft deletes only |

---

## 9. Roadmap

**Phase 1 — Foundation (MVP)**
Solution scaffold at `C:\Users\dcwoo\source\repos\dwoodstgg\ProjectTango` pushed to `github.com/dwoodstgg/ProjectTango`. Auth (Entra ID), employees & roles (multi-role + Admin), clients, projects, rate cards, assignments, time entry + approval, basic project dashboard.

**Phase 2 — Money & Migration**
Budgets + alerts, invoice generation + PDF, invoice lifecycle, WIP report, **Excel timesheet import** (needed early so historical 2026 data is in the system before first invoices), **project close-out & reopen**.

**Phase 3 — Insight**
Full reporting suite, CSV/PDF exports, utilization analytics, budget forecasting, close-out reports.

**Phase 4 — Reach (future)**
Mobile app (.NET MAUI or React Native — time entry + approvals on the go), desktop app if still needed, accounting integration, consolidated client invoices, expenses.

---

## 10. Decisions & Open Questions

### Resolved (2026-07-08)

1. **Backend stack** ✅ .NET 10 (ASP.NET Core), Bootstrap 5.3.x frontend. Data access originally EF Core, revised — see #17.
2. **Overtime** ✅ None. "Extended Work Week" hours bill at the normal stored (project, role) rate — no multipliers, no overtime flag on entries.
3. **Leave** ✅ Imports into internal non-billable projects under the internal client **The Geospatial Group**. Holidays are admin-configurable (`company_holidays`).
4. **Import approval status** ✅ Committed as `approved`.
5. **Duplicate month sheets** ✅ The top/first calendar is the employee-entered source of truth; the lower/duplicate client-specific sheets (e.g., "For MDEQ") are produced by the current system and are skipped on import.
6. **Tax** ✅ Column kept, always 0 in v1.
7. **Cadence** ✅ Monthly timesheet grid (matches the workbook); **no submission step** — employees record time as they go (back-dating allowed) and edit freely until Ops/Admin closes the semi-monthly window (1st–15th, 16th–EOM) or the entry is approved.
8. **Fixed-fee projects** ✅ Real. Billed against project-level **milestones**; employees optionally attach entries to a milestone; out-of-scope work bills at its own hourly rate.
9. **Currency** ✅ USD only.
10. **Billing role** ✅ Chosen per time entry (assignment carries only a default) — the type of work can change with each entry.
11. **Approval semantics** ✅ Approval is a billing decision: approver sets `hours_billed` (may differ from `hours_worked`). Owners can edit entries until the period window is closed or the entry is approved, whichever comes first.
12. **Workforce** ✅ ~10 W-2 employees plus a few 1099 subcontractors; all have tenant accounts; per-person total-time reporting required for both.
13. **Import provisioning** ✅ Importer supplies the person's tenant email; employee records may exist before (or without) an Entra sign-in.
14. **Void** ✅ Only `issued` invoices can be voided (never `paid`); entries revert to `approved`.
15. **Invoice numbering** ✅ Runs continuously across years; YYYY segment reflects issue date, NNNN never resets.
16. **Workbook sheet roles** ✅ Per month: hours are entered on the calendar sheet; the `-DESC` sheet receives rolled-up hours and holds the typed work descriptions. Import takes hours from the calendar, descriptions from `-DESC`; extra client-specific calendars are skipped.
17. **Data access** ✅ **Dapper + Npgsql, no ORM** (revised from EF Core). Schema lives in numbered plain-SQL scripts run by DbUp; enums stored as text with CHECK constraints; snake_case naming per §5.
18. **Billing contact & terms location** ✅ (revised 2026-07-08) Per **project**, not client. Billing contact, address, and payment terms live on the project (all nullable); the client keeps the same fields as **defaults**. Effective value resolves field-by-field project → client → default (terms default 30). Motivated by one client having several concurrent projects with different departmental contacts.
19. **Approval step** ✅ (2026-07-09) **Auto-approve on save.** We're a small shop — a manual approval gate is more overhead than it's worth, so entries approve automatically when saved: billable entries approve once a rate card covers them (otherwise they stay `open` until one is added), non-billable time always approves. The `approved` status and the full approval machinery (ApprovalService, un-approve, `hours_billed` adjustment, the approvals screen) are **retained**, so a manual review step can be turned back on later without rework. Consequence: the "worked 8, bill 6" adjustment is now an explicit Ops/PM un-approve + re-approve rather than a step every entry passes through.

### Still open

1. **Invoice line grouping:** by role, by task, by person, or configurable per client? *(Asked twice — still unanswered.)*
2. **Out-of-scope rate on fixed-fee projects:** a single per-project out-of-scope hourly rate, or per-role rows on the rate card?
3. **Milestone billing details:** who flips a milestone to `ready_to_bill` (PM or Ops)? Is partial milestone billing ever needed?
4. **PM self-approval:** on a small team the PM logs time on their own project — can they approve their own entries, or must another PM/Ops do it?
5. **AWS deployment specifics** (account layout, Terraform vs. CDK): deferred until Phase 1 local development is underway.
