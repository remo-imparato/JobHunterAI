# JobHunterAI

JobHunterAI is a .NET 10 solution for a job search and application assistant. **Phases 1–14** include the solution foundation, domain and PostgreSQL persistence, CQRS handlers, job providers, resume ingestion, typed AI services, document generation, approval-gated browser assistance, a MudBlazor Server dashboard, and opt-in Hangfire background processing. The pre-existing `JobHunter/` project is not included in the solution.

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

## Phase 13 dashboard

The Blazor Server dashboard uses MudBlazor and exposes Dashboard, Jobs, Applications, Resume Versions, Cover Letters, Automation Queue, Settings, and Analytics. Start both hosts using the local development commands above. In Settings, enter an existing candidate profile ID and choose a page size. Preferences last for the current browser circuit only. Jobs are shared discovery records; applications, document metadata, and execution records are filtered by the selected candidate. Lists search and filter on the server and fetch only the requested page.

Read-only endpoints are under `/api/dashboard/{resource}` and `/api/dashboard/summary`. Resource queries require `candidateId` and accept zero-based `page`, `pageSize` (1–100), `search`, and `filter`. The Automation Queue displays recorded candidate agent executions, not pending Hangfire jobs. This dashboard does not submit applications. Analytics shows current persisted counts, not historical trends. Document lists expose metadata rather than rendering untrusted Markdown or exposing file paths.

## Phase 14 background processing

Hangfire runs inside the API and defaults off (`Background:Enabled=false`). Enable it only after applying application migrations and configuring `ConnectionStrings__JobHunterAI`. On enablement, Hangfire creates its own `hangfire` PostgreSQL schema; the database role needs schema creation permissions. No Hangfire dashboard or enqueue HTTP endpoint is exposed. Disabling the feature stops this host's workers; persisted schedules remain in storage, so stop all other workers sharing that storage when disabling it globally.

All schedules use UTC: discovery every six hours, application monitoring hourly, log cleanup at 02:00, score refresh at 03:00, resume variants at 04:00, daily summary at 08:00. AI schedules are registered only with `Background:EnableAiJobs=true`; turning that flag off removes their recurring registrations. Configure AI credentials before opting in. Automatic failure retries are disabled; Hangfire still provides at-least-once processing on worker interruption, so AI calls may be repeated after a crash. Each method uses a distributed concurrency lock; the API uses one worker by default.

Configure `Background:Boards` entries with `Provider` (`Greenhouse`, `Lever`, or `Ashby`), `Board`, and an existing `CompanyId`. Configure `Background:Candidates` entries with an existing `CandidateId` and optional owned `ResumeId`. Candidate IDs must be unique. Resume generation requires a resume with an existing base version. `Background:BatchSize` (default 10, maximum 100) bounds newly saved jobs per board and AI work per candidate per run; there can be at most 20 boards and 20 candidates. Provider APIs may still return a full board before this local processing limit applies. Discovery skips existing provider identities; scoring skips results fresher than `ScoreRefreshHours` (default 24); resume generation skips jobs with existing variants. Candidate run results are recorded as `AgentExecution` rows. Discovery and cleanup report operational counts through structured logs.

Monitoring reports **recorded internal application statuses only**, without changing them or contacting employers. Daily summaries emit aggregate counts to structured logs; email/notification delivery is not configured. No background job approves or submits applications.

Log cleanup is a no-op until an absolute `Background:LogDirectory` is configured. Use a dedicated owned directory. It deletes only top-level `jobhunterai-*.log` files older than `LogRetentionDays` (default 30), bounded by `BatchSize`; symlinks/junctions and nested files are excluded. Existing console logs are not files and are not deleted. Screenshots, Hangfire history, and application audit records are not removed by this job. Live worker/storage validation requires PostgreSQL; automated tests cover configuration, schedule registration, disabled behavior and retention without a live database.

**Candidate selection is not authorization.** Authentication and ownership enforcement are not implemented. Keep both hosts on a trusted local/private network and do not expose these endpoints with real candidate data until access control is added. Database-backed operation requires the existing PostgreSQL migrations and API connection configuration. In non-development environments, configure `Api__BaseUrl`; missing configuration intentionally prevents Web startup.

## Host configuration

The API requires `JobHunterAI:DisplayName` and `ConnectionStrings:JobHunterAI` (environment variable `ConnectionStrings__JobHunterAI`). The Web host requires an absolute HTTP(S) `Api:BaseUrl` (environment variable `Api__BaseUrl`); set it to the API's reachable address in non-development deployments. Invalid options fail startup. AutoMapper 15.1.1 is used for DTO mapping; obtain a valid license for production and supply its key via `AutoMapper__LicenseKey`, not source control. Store credentials in a managed secret store, not in source control. OpenAPI and Swagger UI are available only in Development. Invalid requests and concurrency conflicts return generic HTTP 400/409 responses; unexpected exceptions return a generic 500 response and are logged server-side. TLS termination and trusted proxy settings must be configured by the deployment platform before exposing either host publicly.

## Solution boundaries

`Domain` has no dependencies; `Application` references `Domain`; `Infrastructure` and `AI` reference `Application`; `Api` references `Application`, `Infrastructure`, and `AI`. `Web` uses the API through HTTP rather than referencing the executable API assembly. Candidate profiles own qualifications and experience, resumes own versions, and applications own status events. Repositories operate on aggregate roots; `IUnitOfWork` commits changes once, and PostgreSQL's native `xmin` detects conflicting updates. The Phase 4 MediatR handlers support paginated job/application queries, job and application changes, and document-generation orchestration. Generation deliberately fails with `NotSupportedException` until real drafting services are configured in a later AI phase; no fabricated content is returned. The application aggregate blocks submission without recorded user approval, but authenticated approval and submission endpoints are later-phase work. **Do not deploy for real candidate data or applications yet**.
