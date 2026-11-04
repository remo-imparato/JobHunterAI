# JobHunterAI

JobHunterAI is a .NET 10 job-search and application assistant built with Clean Architecture, PostgreSQL, ASP.NET Core, and a Blazor Server dashboard. The solution contains the Domain, Application, Infrastructure, AI, Automation, API, and Web projects, plus unit and integration test projects. The pre-existing `JobHunter/` project is not part of the solution.

## Features

- Discovers and maps job postings from Greenhouse, Lever, and Ashby into a shared domain model.
- Persists candidate profiles, jobs, applications, resumes, documents, and audit events in PostgreSQL.
- Provides CQRS application workflows, resume and cover-letter drafting, job matching, and resume tailoring. AI-backed drafting requires Azure OpenAI-compatible endpoint credentials; without configuration, AI-dependent operations report that AI is unavailable.
- Offers browser-based application preparation that fills supported identity fields and uploads documents, then stops for human review. It does not submit applications.
- Includes a MudBlazor Blazor Server dashboard for job, application, resume, cover-letter, automation, settings, and analytics views.
- Supports optional Hangfire background processing for configured job discovery, monitoring, cleanup, scoring, resume variants, and summaries. Background processing and AI jobs are disabled by default.
- Emits structured JSON logs, health/readiness endpoints, correlation IDs, and .NET metrics/activities.

## Prerequisites

- .NET 10 SDK
- PostgreSQL 17, running locally or in a container
- Docker Engine/Desktop for container deployment
- Azure OpenAI-compatible endpoint credentials only if using AI-backed operations

## Local development setup

```powershell
dotnet restore JobHunterAI.slnx
dotnet build JobHunterAI.slnx
dotnet test JobHunterAI.slnx
```

The API development settings and Docker Compose use the same local database credentials: database `jobhunterai`, username `jobhunterai`, and password `jobhunterai`. This intentionally hardcoded password is for local development only; replace it with securely managed credentials before deploying outside your machine. Existing User Secrets or environment-variable overrides must use the same password or be removed so the development settings take effect.

```powershell
$env:ConnectionStrings__JobHunterAI = 'Host=localhost;Port=5432;Database=jobhunterai;Username=jobhunterai;Password=jobhunterai'
dotnet tool restore
dotnet tool run dotnet-ef database update --project JobHunterAI.Infrastructure --startup-project JobHunterAI.Api
dotnet run --project JobHunterAI.Api --launch-profile http
```

In a second terminal, start the Web host:

```powershell
dotnet run --project JobHunterAI.Web --launch-profile http
```

The API is available at `http://localhost:5283`; the Blazor dashboard is at `http://localhost:5137`. API health checks are at `/health`, `/health/live`, and `/health/ready`. Development OpenAPI and Swagger UI are available at `/openapi/v1.json` and `/swagger`. The Web development API address is configured in `JobHunterAI.Web/appsettings.Development.json`.

Entity Framework migrations are in `JobHunterAI.Infrastructure/Migrations`. Apply migrations explicitly before starting the API; the API does not auto-migrate. To add harmless example data on Development startup, set `Database__SeedDevelopmentData=true`; it is false by default.

AI-backed drafting uses the Azure OpenAI connector. Configure `AI__Endpoint`, `AI__ApiKey`, and `AI__DeploymentName` when AI features are needed. For production use, obtain a valid AutoMapper license and provide it as `AutoMapper__LicenseKey`; keep the key outside source control.

Run the aggregate line-coverage check with `powershell -NoProfile -File scripts/Test-Coverage.ps1`. It runs both test projects and enforces the configured 80% aggregate line-coverage threshold. Tests use mocked external services and do not replace live PostgreSQL, browser, or Hangfire deployment validation.

## Container deployment with Docker Compose

Install Docker Engine/Desktop with Docker Compose v2. The root `compose.yaml` defines all three services and uses the local password `jobhunterai` for both PostgreSQL and the API connection string. No `POSTGRES_PASSWORD` environment variable or `.env` file is required. Run the following commands from the repository root.

```powershell
docker compose up -d db
```

The database data is stored in the `postgres-data` named volume. If this volume was initialized with a different password, changing Compose does not change the stored password. After the database starts, update the existing role without deleting any data:

```powershell
docker compose exec -T db psql -U jobhunterai -d jobhunterai -v ON_ERROR_STOP=1 -c "ALTER ROLE jobhunterai WITH PASSWORD 'jobhunterai';"
```

This command uses the official PostgreSQL image's local Unix-socket authentication inside the database container. Once the database is healthy, apply the EF migrations from the host before starting the API. This requires the .NET 10 SDK and the repository's local `dotnet-ef` tool:

```powershell
$env:ConnectionStrings__JobHunterAI = 'Host=localhost;Port=5432;Database=jobhunterai;Username=jobhunterai;Password=jobhunterai'
dotnet tool restore
dotnet tool run dotnet-ef database update --project JobHunterAI.Infrastructure --startup-project JobHunterAI.Api
docker compose up --build -d
```

Compose builds and starts the API, Blazor Web, and PostgreSQL services. The dashboard is at `http://localhost:8082` and the API at `http://localhost:8081`; database readiness is at `http://localhost:8081/health/ready`, and Web readiness is at `http://localhost:8082/health/ready`. The host ports are bound to loopback. `docker compose down` stops and removes the containers but preserves the database volume; `docker compose down -v` also deletes the database data.

For pgAdmin or other tools running on Windows, connect with host `localhost`, port `5432`, maintenance database `jobhunterai`, username `jobhunterai`, and password `jobhunterai`. The API running in Visual Studio uses the same host connection. Containers on the Compose network use host `db` instead of `localhost`. A pgAdmin container must join that network to use `db`.

AI and production AutoMapper settings are optional and can be supplied as environment variables before `docker compose up`: `AI_ENDPOINT`, `AI_API_KEY`, `AI_DEPLOYMENT_NAME`, and `AUTOMAPPER_LICENSE_KEY`. Background processing and AI background jobs remain disabled by default; opt in with `BACKGROUND_ENABLED=true` and, when needed, `BACKGROUND_ENABLE_AI_JOBS=true`. Keep credentials out of source control and clear temporary PowerShell environment variables after use.

## Dashboard

The Blazor Server dashboard uses MudBlazor and exposes Dashboard, Jobs, Applications, Resume Versions, Cover Letters, Automation Queue, Settings, and Analytics. Start both hosts using the local development commands above. In Settings, enter an existing candidate profile ID and choose a page size. Preferences last for the current browser circuit only. Jobs are shared discovery records; applications, document metadata, and execution records are filtered by the selected candidate. Lists search and filter on the server and fetch only the requested page.

Read-only endpoints are under `/api/dashboard/{resource}` and `/api/dashboard/summary`. Resource queries require `candidateId` and accept zero-based `page`, `pageSize` (1–100), `search`, and `filter`. The Automation Queue displays recorded candidate agent executions, not pending Hangfire jobs. This dashboard does not submit applications. Analytics shows current persisted counts, not historical trends. Document lists expose metadata rather than rendering untrusted Markdown or exposing file paths.

## Background processing

Hangfire runs inside the API and defaults off (`Background:Enabled=false`). Enable it only after applying application migrations and configuring `ConnectionStrings__JobHunterAI`. On enablement, Hangfire creates its own `hangfire` PostgreSQL schema; the database role needs schema creation permissions. No Hangfire dashboard or enqueue HTTP endpoint is exposed. Disabling the feature stops this host's workers; persisted schedules remain in storage, so stop all other workers sharing that storage when disabling it globally.

All schedules use UTC: discovery every six hours, application monitoring hourly, log cleanup at 02:00, score refresh at 03:00, resume variants at 04:00, daily summary at 08:00. AI schedules are registered only with `Background:EnableAiJobs=true`; turning that flag off removes their recurring registrations. Configure AI credentials before opting in. Automatic failure retries are disabled; Hangfire still provides at-least-once processing on worker interruption, so AI calls may be repeated after a crash. Each method uses a distributed concurrency lock; the API uses one worker by default.

Configure `Background:Boards` entries with `Provider` (`Greenhouse`, `Lever`, or `Ashby`), `Board`, and an existing `CompanyId`. Configure `Background:Candidates` entries with an existing `CandidateId` and optional owned `ResumeId`. Candidate IDs must be unique. Resume generation requires a resume with an existing base version. `Background:BatchSize` (default 10, maximum 100) bounds newly saved jobs per board and AI work per candidate per run; there can be at most 20 boards and 20 candidates. Provider APIs may still return a full board before this local processing limit applies. Discovery skips existing provider identities; scoring skips results fresher than `ScoreRefreshHours` (default 24); resume generation skips jobs with existing variants. Candidate run results are recorded as `AgentExecution` rows. Discovery and cleanup report operational counts through structured logs.

Monitoring reports **recorded internal application statuses only**, without changing them or contacting employers. Daily summaries emit aggregate counts to structured logs; email/notification delivery is not configured. No background job approves or submits applications.

Log cleanup is a no-op until an absolute `Background:LogDirectory` is configured. Use a dedicated owned directory. It deletes only top-level `jobhunterai-*.log` files older than `LogRetentionDays` (default 30), bounded by `BatchSize`; symlinks/junctions and nested files are excluded. Existing console logs are not files and are not deleted. Screenshots, Hangfire history, and application audit records are not removed by this job. Live worker/storage validation requires PostgreSQL; automated tests cover configuration, schedule registration, disabled behavior and retention without a live database.

**Candidate selection is not authorization.** Authentication and ownership enforcement are not implemented. Keep both hosts on a trusted local/private network and do not expose these endpoints with real candidate data until access control is added. Database-backed operation requires the existing PostgreSQL migrations and API connection configuration. In non-development environments, configure `Api__BaseUrl`; missing configuration intentionally prevents Web startup.

## Observability

Both hosts write JSON console logs with structured properties. HTTP completion events record method, route template, status and elapsed milliseconds, not bodies or query strings. Requests accept `X-Correlation-ID` containing 1–64 ASCII letters, digits, hyphens or underscores; invalid/multiple values are replaced. The ID is returned in the response and included with trace/span identifiers in request logging scopes. API exception responses include `correlationId` and `traceId` without exception details. Standard .NET HTTP activities propagate W3C trace context on outgoing requests.

Both hosts expose `/health` and `/health/live` as independent liveness endpoints. `/health/ready` checks PostgreSQL in the API and API readiness in Web, with a five-second check timeout. Readiness returns generic Healthy/Unhealthy responses and HTTP 503 on failure, not connection details. Configure orchestrator probes to use readiness for traffic routing and liveness for process checks. These checks do not verify database migrations, AI credentials, or Hangfire worker health.

The built-in `JobHunterAI` meter publishes `jobhunter.http.requests`, `jobhunter.http.duration` (seconds), `jobhunter.operations`, and `jobhunter.operation.duration` (seconds). Operation categories are `cqrs`, `ai`, and `background`; tags use operation names/outcomes and route templates, not candidate IDs or raw URLs. AI operations measure each attempt (including retry backoff on failed attempts). CQRS success means the handler returned, not necessarily a successful business Result. The `JobHunterAI` ActivitySource provides CQRS, AI and background spans to attached listeners; ASP.NET Core supplies HTTP spans. Attach a .NET diagnostics collector/MeterListener to consume these instruments. No exporter, collector backend, public metrics endpoint, or additional telemetry package is installed.

AI logs contain operation, attempt, duration, outcome and safe failure type; prompts, replies, credentials and provider exception messages are excluded from these logs. Global unhandled-error logging likewise records exception type and route rather than exception content. Automation audit snapshots carry trace/correlation metadata; operational audit events omit free-text details that may contain employer field labels or filenames. Existing domain approval/status events and candidate AgentExecution rows remain the durable audit records. Automation review details remain scoped in-memory snapshots, not a new durable audit store; use a secured external log sink and retention policy for operational records. Existing framework/provider logs should still be reviewed and filtered by deployment logging policy before production use.

## Host configuration

The API requires `JobHunterAI:DisplayName` and `ConnectionStrings:JobHunterAI` (environment variable `ConnectionStrings__JobHunterAI`). The Web host requires an absolute HTTP(S) `Api:BaseUrl` (environment variable `Api__BaseUrl`); set it to the API's reachable address in non-development deployments. Invalid options fail startup. AutoMapper 15.1.1 is used for DTO mapping; obtain a valid license for production and supply its key via `AutoMapper__LicenseKey`, not source control. Store credentials in a managed secret store, not in source control. OpenAPI and Swagger UI are available only in Development. Invalid requests and concurrency conflicts return generic HTTP 400/409 responses; unexpected exceptions return a generic 500 response and are logged server-side. TLS termination and trusted proxy settings must be configured by the deployment platform before exposing either host publicly.

## Solution boundaries

`Domain` has no dependencies; `Application` references `Domain`; `Infrastructure` and `AI` reference `Application`; `Api` references `Application`, `Infrastructure`, `AI`, and `Automation`. `Web` uses the API through HTTP rather than referencing the executable API assembly. Candidate profiles own qualifications and experience, resumes own versions, and applications own status events. Repositories operate on aggregate roots; `IUnitOfWork` commits changes once, and PostgreSQL's native `xmin` detects conflicting updates. MediatR handlers support paginated job/application queries, job and application changes, and document-generation workflows. Resume and cover-letter drafting requires configured AI services; without them, AI-dependent operations fail rather than returning fabricated content. Application preparation stops for human review and does not submit. Authentication, authorization, and authenticated approval/submission endpoints are not implemented. **Do not expose the hosts publicly or use real candidate data until access control is added.**
