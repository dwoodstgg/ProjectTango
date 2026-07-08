-- Seed data (design-doc.md / CLAUDE.md):
-- four roles, the bootstrap Admin, the internal client, and the INT-LEAVE project.
-- GUIDs must match SeedData.cs.

INSERT INTO roles (id, name, is_billable, is_system_admin) VALUES
    ('a0000000-0000-0000-0000-000000000001', 'Developer',          true,  false),
    ('a0000000-0000-0000-0000-000000000002', 'Project Manager',    true,  false),
    ('a0000000-0000-0000-0000-000000000003', 'Operations Manager', true,  false),
    ('a0000000-0000-0000-0000-000000000004', 'Admin',              false, true);

-- Bootstrap Admin: entra_oid stays null until first sign-in; email is the bootstrap key.
INSERT INTO employees (id, entra_oid, email, display_name, employment_type) VALUES
    ('b0000000-0000-0000-0000-000000000001', NULL, 'dwoods@thegeospatialgroup.com', 'Don Woods', 'employee');

INSERT INTO employee_roles (employee_id, role_id, granted_by) VALUES
    ('b0000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000004', 'b0000000-0000-0000-0000-000000000001');

-- Internal client: owns non-billable internal projects; never invoiced.
INSERT INTO clients (id, name, payment_terms_days, is_internal) VALUES
    ('c0000000-0000-0000-0000-000000000001', 'The Geospatial Group', 0, true);

INSERT INTO projects (id, client_id, name, code, status, project_manager_id) VALUES
    ('d0000000-0000-0000-0000-000000000001',
     'c0000000-0000-0000-0000-000000000001',
     'Leave & Holidays',
     'INT-LEAVE',
     'active',
     'b0000000-0000-0000-0000-000000000001');
