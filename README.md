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

Entity Framework migrations are in `JobHunterAI.Infrastructure/Migrations`. Apply migrations explicitly before starting the API. When Development seeding is enabled (`Database__SeedDevelopmentData=true`), the seeder also applies pending migrations before adding sample data. The checked-in Development settings enable seeding; set `Database__SeedDevelopmentData=false` to disable both startup seeding and its migration step. Non-development environments do not run the development seeder.

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

The Blazor Server dashboard uses MudBlazor and exposes candidate selection, Dashboard, Jobs, Applications, Resume Versions, Cover Letters, Automation Queue, Settings, and Analytics. Start both hosts using the local development commands above. Opening the dashboard without a selection directs you to `/candidates` (`/login` is an alias). Search by full name or username, then select a result, or choose **Create candidate** to enter a full name, username, email, and headline. Creation selects the new candidate and opens the dashboard. Duplicate full names are allowed; use the username to distinguish profiles.

Usernames are trimmed and stored in lowercase, with 1–64 ASCII letters, numbers, dots, underscores or hyphens. PostgreSQL enforces normalized values and uniqueness. Apply the `AddCandidateUsernames` migration using the database-update command above before using the candidate APIs. It preserves existing data and assigns existing profiles `candidate-{id-without-hyphens}` usernames; search by full name and use **Manage** to choose a friendlier username.

Use **Select candidate** in navigation to switch profiles, **Manage** beside a search result to edit or delete one, and **Settings** to edit the active profile, clear the selection, or choose a page size. Deletion requires confirmation and is blocked when resumes, cover letters, applications, execution history, or match scores reference the candidate. A candidate's profile-owned skills, work experience, education, and certifications are removed with an otherwise deletable profile. Preferences and selection last for the current browser circuit only; a full browser refresh requires selecting a candidate again. Switching candidates resets routed page state. Jobs remain shared discovery records; applications, document metadata, and execution records are filtered by the selected candidate. Lists search and filter on the server and fetch only the requested page.

Candidate APIs are `GET /api/candidates?search=...&page=0&pageSize=10`, `GET /api/candidates/{id}`, `POST /api/candidates`, `PUT /api/candidates/{id}`, and `DELETE /api/candidates/{id}`. Create/update bodies contain `fullName`, `username`, `email`, and `headline`. Search matches a case-insensitive substring of full name or username, accepts zero-based pages (0–100000) and page sizes (1–100), and returns distinct profiles with stable name/username/ID ordering. Validation errors return 400, missing profiles return 404, and username/deletion/concurrency conflicts return 409. No password, authentication, or API authorization is provided.

Source-resume onboarding, saved criteria, candidate-triggered discovery and Markdown application ZIP downloads are available as described below. Saving preferences does not start a search. Automated tests use mocked API dependencies and intercepted PostgreSQL SQL generation; they do not replace live database-migration or browser validation. Discovery and package history also block candidate deletion.

Read-only endpoints are under `/api/dashboard/{resource}` and `/api/dashboard/summary`. Resource queries require `candidateId` and accept zero-based `page`, `pageSize` (1–100), `search`, and `filter`. The Automation Queue displays recorded candidate agent executions, not pending Hangfire jobs. This dashboard does not submit applications. Analytics shows current persisted counts, not historical trends. Document lists expose metadata rather than rendering untrusted Markdown or exposing file paths.

## Resume onboarding and search criteria (Phase 2)

Apply the `AddResumeSourcesAndSearchCriteria` migration using the database-update command above before using these pages. It adds source metadata and candidate search preferences without replacing existing resume text. Each legacy resume's earliest non-job-specific version becomes its pinned source; a resume containing only tailored versions remains without a trusted source and requires a new upload. Migration application was not performed as part of implementation. Development startup seeding, when enabled, applies pending migrations as noted above.

After selecting a candidate, open **Source resumes** (`/resumes`):

1. Choose a text-based PDF or DOCX, at most 10 MB. Extraction accepts at most 50 PDF pages, 20 MB of DOCX document XML, and 500,000 extracted characters. Unsupported, unreadable, scanned/empty, or over-limit files are rejected rather than silently truncated. OCR is not provided.
2. The upload saves the complete extracted text, a pinned source version, the filename without its supplied path, and a SHA-256 hash. Original file bytes are not retained. Uploading the same bytes again for the same candidate reuses the saved source; another candidate's upload stays separate. Sources are immutable; upload a new file to create a different source. Drafting uses the pinned original, never the latest tailored version.
3. Use **Review source** to inspect encoded plain text and proposed skills, work history, education, and certification additions. Extraction is heuristic and may omit incomplete or unrecognized structured entries; those facts remain in the full source text. Reviewing a source selects it for this page only, not as a global candidate default.
4. Check the approval box and choose **Import approved additions** only after reviewing the proposed fields. Upload and preview do not alter the profile. Import preserves contact details, skips existing entries, and can be repeated without duplicates. Oversized structured fields are rejected before any import changes; the saved source remains available.

Resume routes are `GET/POST /api/candidates/{candidateId}/resumes`, `GET /api/candidates/{candidateId}/resumes/{resumeId}`, and `POST /api/candidates/{candidateId}/resumes/{resumeId}/import`. Uploads use one multipart field named `file`; list requests accept zero-based `page` (0–100000) and `pageSize` (1–100). Resume/candidate associations are checked for preview and import, but these checks are not user authentication or authorization. Invalid content returns 400, unsupported request media returns 415, missing/mismatched resources return 404, and missing-source or concurrent-save conflicts return 409. Oversized requests or extraction limits return 413; malformed/over-limit multipart parsing can return 400. Retry a concurrent identical upload to retrieve the existing source.

Open **Search criteria** (`/search-criteria`) to save preferences for the selected candidate. Enter one value per line: at least one job title, optional locations, required keywords, and excluded keywords. Each list permits at most 20 entries of 1–200 characters, trims whitespace, and removes case-insensitive duplicates. Select one or more remote/hybrid/on-site arrangements, plus whether to include jobs with unknown arrangements. Unknown is never inferred to mean on-site or hybrid.

Matching semantics are case-insensitive substrings: any title, any configured city/country location, all required keywords in the title/description, and no excluded keywords. These groups are combined with AND. Blank locations allow any location; a configured location still applies to remote postings and does not match missing location metadata. Work arrangement is checked separately, including the unknown preference. A keyword cannot be both required and excluded. Candidate discovery applies these saved rules; scheduled AI processing requires completed candidate discovery matches and rechecks the current rules.

Criteria routes are `GET/PUT /api/candidates/{candidateId}/search-criteria`. The body contains `jobTitles`, `locations`, `arrangements` (flags: Remote=1, Hybrid=2, OnSite=4; add values to combine), `includeUnknownArrangement`, `requiredKeywords`, and `excludedKeywords`. GET returns 404 when no criteria are saved. Criteria persist across browser circuits, unlike candidate selection. They are profile-owned preferences and are removed with an otherwise deletable candidate.

## Candidate discovery (Phase 3)

Apply the additive `AddCandidateDiscovery` migration before using **Find matching jobs** (`/discovery`). Implementation did not apply it to your database. It adds candidate search history/results and nullable posting work arrangements. Existing postings remain **unknown**, even if their old remote flag is true: earlier imports could have inferred that flag from missing geography. Live provider refreshes populate confirmed arrangements. Missing/partial geography is preserved, not invented; a false remote flag alone does not establish on-site status.

Select a candidate, save **Search criteria**, then choose **Start search**. A run examines recent stored postings first and then configured Greenhouse, Lever and Ashby boards until its limits are reached. It records a criteria snapshot, final status, examined count, unique candidate-associated matches and sanitized source warnings. Jobs remain shared and are updated/deduplicated by provider name plus board-scoped external ID; history points to current shared posting details, not immutable posting snapshots. No AI calls or document generation occur during discovery. Existing optional scoring is reused only by explicitly enabled scheduled AI work, not as a prerequisite for deterministic matching.

Configure `Background:Boards` even when `Background:Enabled=false`. Each entry needs `Provider` (`Greenhouse`, `Lever`, or `Ashby`), the provider's public board slug in `Board`, and `CompanyId` referencing an existing company. Board slugs support ASCII letters, digits, hyphens and underscores; invalid source input appears as a run warning. For example, environment variables `Background__Boards__0__Provider=Lever`, `Background__Boards__0__Board=your-board-slug`, and `Background__Boards__0__CompanyId=<existing-company-guid>` configure one source. These settings are shared with optional recurring ingestion; they do not enable Hangfire. No boards means stored-only discovery with an explanatory warning. These providers cover configured public employer feeds, **not the whole internet**; they remain pluggable through `IJobProvider`.

Limits under `Discovery` (environment-variable equivalents use `Discovery__...`):

| Setting | Default | Allowed range |
| --- | ---: | ---: |
| `StoredJobsLimit` | 200 | 1–1000 |
| `JobsPerBoard` | 100 | 1–500 |
| `MaxMatches` | 100 | 1–100 |
| `TimeoutSeconds` | 90 | 1–120 |

At most 20 configured boards are examined; the shared board configuration also limits entries to 20. Providers currently fetch/parse a full board feed before the per-board examination limit is applied, so these are processing/result bounds, not a streaming download-size cap. Provider HTTP timeouts and the overall linked run deadline bound waiting. Partial provider failures continue with warnings. Once a match limit is reached, remaining sources may not be contacted. Narrow or refresh criteria and rerun when limits omit older opportunities.

**Cancel search** aborts the HTTP request best-effort. The server attempts to persist `Cancelled` and partial matches with an independent short cleanup timeout. Refresh the latest run or history after cancellation; finalization may still be in progress. This is request-bound processing, not a durable worker: host termination or database failure can leave a run `Running`; refresh/retry with a new run rather than assuming it completed. Only completed/completed-with-warnings runs qualify for scheduled AI processing; cancelled/failed partial results are review-only. Candidate switching disposes the page and requests cancellation. Selection and candidate-scoped lookups remain convenience/data-association checks, **not authorization**; use only in a trusted local/private environment.

API routes: `POST /api/candidates/{candidateId}/discovery` with `{ "runId": "<new-guid>" }`, `GET` on the same path with zero-based `page` (0–100000) and `pageSize` (1–100), and `GET /{runId}` for candidate-scoped details. Use a fresh run ID for each attempt. Invalid input returns 400, missing candidate/run 404, and missing saved criteria or concurrent/duplicate persistence conflicts 409. Employer links are restricted to credential-free absolute HTTP(S); descriptions and snapshots render as encoded text.

For manual review after applying migrations: select a candidate, save criteria, run stored-only discovery, then configure a real employer board/company and verify results/warnings, limits, cancellation refresh, and candidate switching. Confirm an unmatched candidate does not see another candidate's history. Live PostgreSQL migration execution and real provider/browser checks are not replaced by the mocked automated tests. Pause here for Phase 3 review before implementing Phase 4 grounded cover letters and ZIP packages.

## Application packages (Phase 4)

Apply all pending migrations including `AddApplicationPackages`, `ExplicitResumeVersionKeys` and `CorrectResumeVersionKeyMapping` using the documented database-update command. The latter two align client-assigned key metadata; the final mapping explicitly treats new resume versions as inserts. Implementation did not apply migrations to your database. The package table retains immutable source/job/company snapshots, exact output copies and document references, generation status and an atomic retry lease. Restrictive relationships prevent silent document/history deletion.

End-to-end workflow:

1. Create or select a candidate at `/candidates` (selection is not authentication).
2. Upload a text-based PDF/DOCX at **Source resumes** and review any optional profile imports.
3. Save **Search criteria**, configure employer boards/company IDs, and start **Find matching jobs**.
4. Expand a matching result and choose **Prepare application package**. Select an explicitly uploaded source resume; legacy versions without uploaded source text cannot be used for packages.
5. Choose **Generate / retry package**. Configure `AI__Endpoint`, `AI__ApiKey`, and `AI__DeploymentName` on the API first. The complete extracted source text and saved posting context are sent to the configured external AI service; consider privacy and provider data policies before generating. Both prompts treat uploaded/candidate/job fields as untrusted data and prohibit invented qualifications. These rules are not a guarantee: inspect every claim, date and metric.
6. Review the encoded tailored resume and cover letter, check the review box, then **Download Markdown ZIP**. The archive contains exactly `resume.md`, `cover-letter.md`, `job-description.txt` and `job-link.txt`, with a safe bounded `{company}-{job title}.zip` attachment name. PDF/DOCX export is not implemented. Nothing is automatically submitted.

Generation is bounded to 120 seconds; the 3-minute database lease blocks overlapping retries and permits recovery after cancellation/host failure. Refresh status after cancelling or losing a request. Retry the same candidate/job/source selection to resume: already committed documents are reused, only missing documents are drafted, and completion requires both. A completed package is reused without additional AI calls. Retries retain the initial posting/company/source snapshots, even if a shared posting later changes; choosing another uploaded source creates a separate package. Profile facts come from the current candidate at each drafting attempt. A failed save discards uncommitted outputs, so an interrupted AI call may incur another call/cost on retry. Missing configuration appears as `AiUnavailable`; other failures use safe `GenerationFailed`/`Cancelled` codes without exposing prompts or content in logs.

Package inputs allow up to 500,000 characters each for source/job descriptions; each generated document allows 100,000 characters, and archives are capped at 4 MB. Fixed ZIP entry names do not use server paths. Snapshot/output content is stored in PostgreSQL and returned only in candidate-scoped lookups, but these are **not authorization**: all users able to reach this trusted-private application can request candidate IDs. The review checkbox is UI guidance, not an API security restriction. Downloads use no-store caching and a bounded same-origin Web proxy.

API: `POST /api/candidates/{candidateId}/packages` with `jobId` and `resumeId`; `GET /selection?jobId=...&resumeId=...`; `GET /{packageId}`; and `GET /{packageId}/download`. Validation returns 400, missing/wrong-owner resources or jobs without candidate discovery associations return 404, and active leases/missing uploaded source/incomplete downloads return 409. Generation failures return a successful status response with `status=Failed` and a sanitized `failureCode`, so clients can display/retry the saved package. The Web download route is `/packages/{candidateId}/{packageId}/download`.

Automated tests verify grounded prompt inputs, immutable source selection, partial retry reuse, exact ZIP contents, filename safety, candidate isolation, API responses, compiled Blazor review gating, proxy bounds, and EF SQL/model/tracking. They do not invoke real AI or apply migrations. For final manual review, perform the six steps above against migrated PostgreSQL and a real configured board/AI endpoint; test cancellation/retry, inspect the extracted ZIP, and switch candidates to confirm page state resets. Phase 4 is paused for final user review.

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
