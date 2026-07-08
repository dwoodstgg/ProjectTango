# Project Tango

Time tracking, project budgeting, and invoicing for The Geospatial Group.

See **[design-doc.md](design-doc.md)** for the full design (source of truth) and **[CLAUDE.md](CLAUDE.md)** for the condensed rules.

## Stack

.NET 10 (ASP.NET Core MVC + `/api/v1` REST), Bootstrap 5.3, PostgreSQL (EF Core + Npgsql), Microsoft Entra ID auth, xUnit + Testcontainers. Hosting target: AWS.

## Local development

Prereqs: .NET 10 SDK, Docker Desktop.

```powershell
docker compose up -d            # local Postgres on :5432 (tango/tango, db projecttango)
dotnet build
dotnet test                     # integration tests spin up their own Postgres via Testcontainers
dotnet run --project src/ProjectTango.Web
```

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
