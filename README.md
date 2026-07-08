# Project Tango

Time tracking, project budgeting, and invoicing for The Geospatial Group.

See **[design-doc.md](design-doc.md)** for the full design (source of truth) and **[CLAUDE.md](CLAUDE.md)** for the condensed rules.

## Stack

.NET 10 (ASP.NET Core MVC + `/api/v1` REST), Bootstrap 5.3, PostgreSQL via Dapper + Npgsql (schema in DbUp-run SQL scripts — no ORM), Microsoft Entra ID auth, xUnit + Testcontainers. Hosting target: AWS.

## Local development

Prereqs: .NET 10 SDK, Docker Desktop (for Testcontainers), PostgreSQL (native install or Docker).

The app connects to `localhost:5432`, database `projecttango`, user `tango` (password in `appsettings.json`). On the primary dev machine that's the native PostgreSQL 18 install (data dir `C:\Program Files\PostgreSQL\18\data`, browsable in pgAdmin). One-time setup — run as the `postgres` superuser (pgAdmin Query Tool or psql):

```sql
CREATE ROLE tango LOGIN PASSWORD 'tango@123';
CREATE DATABASE projecttango OWNER tango;
```

No native Postgres? `docker compose up -d` starts one on port **5433** (it avoids clashing with a native install); change `Port` in the connection string to match.

```powershell
dotnet build
dotnet test                                          # integration tests spin up their own throwaway Postgres via Testcontainers
dotnet run --project src/ProjectTango.Web -- migrate # apply pending SQL scripts (also runs automatically on dev startup)
dotnet run --project src/ProjectTango.Web
```

Schema lives in [src/ProjectTango.Infrastructure/Persistence/Scripts](src/ProjectTango.Infrastructure/Persistence/Scripts) as numbered SQL files; DbUp applies pending ones in order and records them in the `schemaversions` table. Never edit a script that has already shipped — add a new one.

Entra ID: `AzureAd:TenantId` and `AzureAd:ClientId` in `src/ProjectTango.Web/appsettings.json` are placeholders until the app registration exists in the thegeospatialgroup.com tenant. The app runs without them; sign-in won't work until they're real.

## Solution layout

```
src/ProjectTango.Domain          entities, enums, domain rules (no dependencies)
src/ProjectTango.Application     services, use cases, validation, interfaces
src/ProjectTango.Infrastructure  EF Core/Npgsql, migrations, S3, email, Excel import, PDF
src/ProjectTango.Web             ASP.NET Core host: Razor UI + /api/v1 + auth
tests/ProjectTango.UnitTests
tests/ProjectTango.IntegrationTests   (Testcontainers Postgres)
```
