# JobHunterAI

JobHunterAI is a .NET 10 solution for a job search and application assistant. **Phases 1–16** include the solution foundation, domain and PostgreSQL persistence, CQRS handlers, job providers, resume ingestion, typed AI services, document generation, approval-gated browser assistance, a MudBlazor Server dashboard, opt-in Hangfire background processing, observability, and a measured regression suite. The pre-existing `JobHunter/` project is not included in the solution.

## Phase 16 testing and coverage

Run `powershell -NoProfile -File scripts/Test-Coverage.ps1` from the repository root. The script runs both xUnit projects using the existing Coverlet collector, merges hits by assembly/source file/line, verifies that all seven production assemblies are represented, and fails if aggregate line coverage is below 80%. Each run writes Cobertura reports and `summary.json` to a new ignored `artifacts/coverage/<run-id>` directory, preventing stale results from satisfying the gate. This is a line-coverage target, not a branch-coverage or per-assembly threshold. Only test assemblies, generated migration files, and generated `obj` C# files are excluded; handwritten Razor and browser automation code remain in scope.

Measured aggregate coverage: **80.78% (2761/3418 lines)**. Assembly breakdown: Domain 86.92%, Application 81.10%, Infrastructure 82.70%, AI 91.76%, Automation 87.38%, API 75.92%, Web 36.33%. The suite includes unit, API integration, provider HTTP-fake, repository/query, AI completion-mock, Web-client and server-rendered component tests. External service tests do not send real requests or incur AI costs. Repository query tests run Npgsql SQL translation with intercepted empty results and verify pagination/parameters/tracking; they do **not** validate PostgreSQL execution, migrations, constraints or concurrency against a live server. Blazor HtmlRenderer tests cover server-rendered pages and summary states, not interactive table callbacks or browser JavaScript. Real Chromium flows, live Hangfire storage and PostgreSQL-backed end-to-end testing remain separate validation work. No coverage percentage implies those integrations have been verified.

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

## Phase 15 observability

Both hosts write JSON console logs with structured properties. HTTP completion events record method, route template, status and elapsed milliseconds, not bodies or query strings. Requests accept `X-Correlation-ID` containing 1–64 ASCII letters, digits, hyphens or underscores; invalid/multiple values are replaced. The ID is returned in the response and included with trace/span identifiers in request logging scopes. API exception responses include `correlationId` and `traceId` without exception details. Standard .NET HTTP activities propagate W3C trace context on outgoing requests.

Both hosts expose `/health` and `/health/live` as independent liveness endpoints. `/health/ready` checks PostgreSQL in the API and API readiness in Web, with a five-second check timeout. Readiness returns generic Healthy/Unhealthy responses and HTTP 503 on failure, not connection details. Configure orchestrator probes to use readiness for traffic routing and liveness for process checks. These checks do not verify database migrations, AI credentials, or Hangfire worker health.

The built-in `JobHunterAI` meter publishes `jobhunter.http.requests`, `jobhunter.http.duration` (seconds), `jobhunter.operations`, and `jobhunter.operation.duration` (seconds). Operation categories are `cqrs`, `ai`, and `background`; tags use operation names/outcomes and route templates, not candidate IDs or raw URLs. AI operations measure each attempt (including retry backoff on failed attempts). CQRS success means the handler returned, not necessarily a successful business Result. The `JobHunterAI` ActivitySource provides CQRS, AI and background spans to attached listeners; ASP.NET Core supplies HTTP spans. Attach a .NET diagnostics collector/MeterListener to consume these instruments. No exporter, collector backend, public metrics endpoint, or additional telemetry package is installed.

AI logs contain operation, attempt, duration, outcome and safe failure type; prompts, replies, credentials and provider exception messages are excluded from these logs. Global unhandled-error logging likewise records exception type and route rather than exception content. Automation audit snapshots carry trace/correlation metadata; operational audit events omit free-text details that may contain employer field labels or filenames. Existing domain approval/status events and candidate AgentExecution rows remain the durable audit records. Automation review details remain scoped in-memory snapshots, not a new durable audit store; use a secured external log sink and retention policy for operational records. Existing framework/provider logs should still be reviewed and filtered by deployment logging policy before production use.

## Host configuration

The API requires `JobHunterAI:DisplayName` and `ConnectionStrings:JobHunterAI` (environment variable `ConnectionStrings__JobHunterAI`). The Web host requires an absolute HTTP(S) `Api:BaseUrl` (environment variable `Api__BaseUrl`); set it to the API's reachable address in non-development deployments. Invalid options fail startup. AutoMapper 15.1.1 is used for DTO mapping; obtain a valid license for production and supply its key via `AutoMapper__LicenseKey`, not source control. Store credentials in a managed secret store, not in source control. OpenAPI and Swagger UI are available only in Development. Invalid requests and concurrency conflicts return generic HTTP 400/409 responses; unexpected exceptions return a generic 500 response and are logged server-side. TLS termination and trusted proxy settings must be configured by the deployment platform before exposing either host publicly.

## Solution boundaries

`Domain` has no dependencies; `Application` references `Domain`; `Infrastructure` and `AI` reference `Application`; `Api` references `Application`, `Infrastructure`, and `AI`. `Web` uses the API through HTTP rather than referencing the executable API assembly. Candidate profiles own qualifications and experience, resumes own versions, and applications own status events. Repositories operate on aggregate roots; `IUnitOfWork` commits changes once, and PostgreSQL's native `xmin` detects conflicting updates. The Phase 4 MediatR handlers support paginated job/application queries, job and application changes, and document-generation orchestration. Generation deliberately fails with `NotSupportedException` until real drafting services are configured in a later AI phase; no fabricated content is returned. The application aggregate blocks submission without recorded user approval, but authenticated approval and submission endpoints are later-phase work. **Do not deploy for real candidate data or applications yet**.
