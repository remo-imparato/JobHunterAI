# JobHunterAI

JobHunterAI is a .NET 10 solution for a job search and application assistant. **Phases 1–4** are implemented: API and Blazor Server hosts, a validated domain model, PostgreSQL persistence, and CQRS application handlers with validation and typed responses. Job discovery providers, AI integration, and application submission endpoints do not exist yet. The pre-existing `JobHunter/` project is not included in the solution.

## Prerequisites

- .NET 10 SDK
- PostgreSQL 17 (or Docker with Compose)

## Local development

```powershell
dotnet restore JobHunterAI.slnx
dotnet tool restore
dotnet build JobHunterAI.slnx
dotnet test JobHunterAI.slnx
if (-not $env:ConnectionStrings__JobHunterAI) { throw 'Set ConnectionStrings__JobHunterAI to your PostgreSQL connection string first.' }
dotnet tool run dotnet-ef database update --project JobHunterAI.Infrastructure --startup-project JobHunterAI.Api
dotnet run --project JobHunterAI.Api --launch-profile http
# In a second terminal:
dotnet run --project JobHunterAI.Web --launch-profile http
```

API health: `http://localhost:5283/health`. Development OpenAPI: `http://localhost:5283/openapi/v1.json`; Swagger UI: `http://localhost:5283/swagger`. Web: `http://localhost:5137/`. The development Web API address is set in `JobHunterAI.Web/appsettings.Development.json`.

Provide `ConnectionStrings__JobHunterAI` through an environment variable or secure development secret store before running the API; no connection string/password is committed. The initial migration is in `JobHunterAI.Infrastructure/Migrations`; its idempotent SQL script is `InitialCreate.sql`. For later changes, generate and review a new migration and SQL script before applying them. The API never auto-migrates in Production. To migrate and seed harmless example data on Development startup, set `Database__SeedDevelopmentData=true` explicitly; leave it unset or false otherwise.

## Containers

```powershell
$env:POSTGRES_PASSWORD = [System.Net.NetworkCredential]::new('', (Read-Host 'Local PostgreSQL password' -AsSecureString)).Password
docker compose up -d db
$env:ConnectionStrings__JobHunterAI = 'Host=localhost;Port=5432;Database=jobhunterai;Username=jobhunterai;Password=' + $env:POSTGRES_PASSWORD
dotnet tool restore
dotnet tool run dotnet-ef database update --project JobHunterAI.Infrastructure --startup-project JobHunterAI.Api
docker compose up --build
```

API: `http://localhost:8081/health`; Web: `http://localhost:8082/`. PostgreSQL listens only on `127.0.0.1:5432` on the host. Compose requires `POSTGRES_PASSWORD`; keep it out of source control and use a strong local password. Containers communicate over the Compose network. The development seed is disabled in Production Compose. Clear session environment variables after use.

## Configuration and security

The API requires `JobHunterAI:DisplayName` and `ConnectionStrings:JobHunterAI` (environment variable `ConnectionStrings__JobHunterAI`). The Web host requires an absolute HTTP(S) `Api:BaseUrl` (environment variable `Api__BaseUrl`); set it to the API's reachable address in non-development deployments. Invalid options fail startup. AutoMapper 15.1.1 is used for DTO mapping; obtain a valid license for production and supply its key via `AutoMapper__LicenseKey`, not source control. Store credentials in a managed secret store, not in source control. OpenAPI and Swagger UI are available only in Development. Invalid requests and concurrency conflicts return generic HTTP 400/409 responses; unexpected exceptions return a generic 500 response and are logged server-side. TLS termination and trusted proxy settings must be configured by the deployment platform before exposing either host publicly.

## Solution boundaries

`Domain` has no dependencies; `Application` references `Domain`; `Infrastructure` and `AI` reference `Application`; `Api` references `Application`, `Infrastructure`, and `AI`. `Web` uses the API through HTTP rather than referencing the executable API assembly. Candidate profiles own qualifications and experience, resumes own versions, and applications own status events. Repositories operate on aggregate roots; `IUnitOfWork` commits changes once, and PostgreSQL's native `xmin` detects conflicting updates. The Phase 4 MediatR handlers support paginated job/application queries, job and application changes, and document-generation orchestration. Generation deliberately fails with `NotSupportedException` until real drafting services are configured in a later AI phase; no fabricated content is returned. The application aggregate blocks submission without recorded user approval, but authenticated approval and submission endpoints are later-phase work. **Do not deploy for real candidate data or applications yet**.
