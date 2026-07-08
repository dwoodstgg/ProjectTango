-- Roles get an editable display label, decoupled from `name`.
-- `name` stays the STABLE authorization key (auth claims, RoleNames constants,
-- [Authorize(Roles=...)]) and is never edited. `display_name` is what the UI shows
-- and what Admins may rename — so renaming a role can never break permissions.

ALTER TABLE roles ADD COLUMN display_name text;
UPDATE roles SET display_name = name;
ALTER TABLE roles ALTER COLUMN display_name SET NOT NULL;
