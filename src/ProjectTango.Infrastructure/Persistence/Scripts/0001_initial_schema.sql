-- Phase 1 foundation: employees, roles, clients, projects.
-- Conventions (design-doc.md §5): snake_case, uuid PKs, timestamptz (UTC),
-- money as numeric (later scripts), enums as text + CHECK, soft deletes only.

CREATE EXTENSION IF NOT EXISTS citext;

CREATE TABLE roles (
    id              uuid PRIMARY KEY,
    name            text NOT NULL UNIQUE,
    is_billable     boolean NOT NULL DEFAULT true,
    is_system_admin boolean NOT NULL DEFAULT false
);

CREATE TABLE employees (
    id              uuid PRIMARY KEY,
    entra_oid       text UNIQUE,          -- null until first Entra sign-in (import/manual provisioning)
    email           citext NOT NULL UNIQUE,
    display_name    text NOT NULL,
    employment_type text NOT NULL DEFAULT 'employee'
                    CHECK (employment_type IN ('employee', 'subcontractor')),
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE employee_roles (
    employee_id uuid NOT NULL REFERENCES employees (id),
    role_id     uuid NOT NULL REFERENCES roles (id),
    granted_by  uuid NOT NULL REFERENCES employees (id),
    granted_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (employee_id, role_id)
);

CREATE TABLE clients (
    id                    uuid PRIMARY KEY,
    name                  text NOT NULL,
    billing_contact_name  text,
    billing_contact_email text,
    billing_address       jsonb,
    payment_terms_days    int NOT NULL DEFAULT 30,
    is_internal           boolean NOT NULL DEFAULT false, -- The Geospatial Group itself; never invoiced
    is_active             boolean NOT NULL DEFAULT true
);

CREATE TABLE projects (
    id                 uuid PRIMARY KEY,
    client_id          uuid NOT NULL REFERENCES clients (id),
    name               text NOT NULL,
    code               text NOT NULL UNIQUE,
    status             text NOT NULL DEFAULT 'draft'
                       CHECK (status IN ('draft', 'active', 'on_hold', 'closed', 'archived')),
    closed_at          timestamptz,
    closed_by          uuid REFERENCES employees (id),
    project_manager_id uuid NOT NULL REFERENCES employees (id),
    start_date         date,
    end_date           date,
    currency           char(3) NOT NULL DEFAULT 'USD'
);

CREATE INDEX ix_projects_client_id ON projects (client_id);
CREATE INDEX ix_projects_project_manager_id ON projects (project_manager_id);
