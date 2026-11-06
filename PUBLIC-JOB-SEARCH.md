# Public discovery and manual-review application packages

## What is configured

| Source | Endpoint | Configuration/status |
| --- | --- | --- |
| Arbeitnow | `https://www.arbeitnow.com/api/job-board-api` | Enabled, no key; bounded pagination |
| RemoteOK | `https://remoteok.com/api` | Enabled, no key; preserves the source posting URL |
| Greenhouse | `https://boards-api.greenhouse.io/v1/boards/{board}/jobs` | Existing adapter; configure each employer board and actual company ID |
| Lever | `https://api.lever.co/v0/postings/{board}` | Existing adapter; configure each employer board and actual company ID |
| Ashby | `https://api.ashbyhq.com/posting-api/job-board/{board}` | Existing adapter; configure each employer board and actual company ID |

Public does not mean unrestricted. Review current source terms, attribution, rate limits and geographic coverage before deploying or redistributing listings. RemoteOK results link back to RemoteOK; do not strip source attribution. Feed URLs and schemas can change. Live availability is not guaranteed. These adapters do not bypass authentication, scrape protected pages, or submit applications.

USAJobs, Adzuna and Jooble are **not implemented in this update**. They require developer registration/credentials (USAJobs key + identifying user agent; Adzuna app ID + key; Jooble key). Indeed and ZipRecruiter are partner programs, not assumed free general-search APIs. The Muse, SmartRecruiters and employer-specific Workday integrations also remain outside this update. Do not configure their names as existing providers.

## Search behavior

Both **Search only** and **Search and prepare packages in background** use the same discovery handler:

1. Examine a bounded set of stored postings.
2. Attempt every configured ATS board and enabled public feed; the saved-match cap no longer stops later sources from being contacted.
3. Upsert jobs by provider/external ID and preserve real employer names instead of assigning aggregate-feed jobs to the development sample company.
4. Apply saved candidate criteria (title, location, work arrangement and keywords) locally. This is deterministic filtering, not AI ranking.
5. Save discovery history, matched jobs and source warnings.

This is not an exhaustive whole-internet crawler. API/compose configuration: 200 stored jobs, 300 jobs per source, 100 saved matches, 90 seconds total; ATS requests have a 15-second deadline. (The unconfigured `DiscoveryOptions` class still defaults to 100 per source.) Public feed requests have a 15-second deadline, at most 3 Arbeitnow pages, and an 8 MiB response limit. Failure/429/invalid feed data produces warnings and does not stop healthy sources. Cancellation or the overall deadline can prevent remaining sources from completing. Review warnings before interpreting zero matches.

All configured sources are contacted within these bounds even after the match cap, but matching results beyond the cap are not retained in that run. Source order can therefore affect the bounded result set. Feed locations can be free-form; missing country/work-arrangement metadata is not invented. Review location/unknown-location criteria if matches seem unexpectedly sparse.

API configuration in `JobHunterAI.Api/appsettings.json`:

```json
{
  "PublicFeeds": {
	"Arbeitnow": true,
	"RemoteOK": true,
	"MaxPages": 3,
	"RequestTimeoutSeconds": 15
  },
  "Discovery": {
	"StoredJobsLimit": 200,
	"JobsPerBoard": 300,
	"MaxMatches": 100,
	"TimeoutSeconds": 90,
	"SourceTimeoutSeconds": 15
  }
}
```

Keep this merged with existing settings, not as a replacement for the entire file. Disable a feed by setting its flag to `false`. Limits are validated on startup. No credentials are needed for these two feeds, and there is no need to find their URLs.

For ATS boards, create the actual employer company first, then add entries under `Background:Boards`:

```json
{
  "Provider": "Greenhouse",
  "Board": "actual-employer-board-token",
  "CompanyId": "existing-employer-company-guid"
}
```

Replace placeholders. An ATS endpoint requires an employer board; it cannot enumerate all employers automatically.

## Background pipeline

The primary button on **Find matching jobs** queues a durable PostgreSQL-backed Hangfire job. Closing the page does not cancel it. A queued search appears in discovery history once the worker begins execution; refresh history/results. The separate **Search only** button remains synchronous and does not call AI.

The worker searches, then uses the existing grounded AI package generator for each selected match. Package drafting requires:

- `Background:Enabled=true` and `Background:EnableAiJobs=true`;
- configured AI credentials (the existing integration uses Azure OpenAI);
- a source resume belonging to the candidate. With exactly one uploaded source it is selected automatically. With multiple sources, configure `Background:Candidates` with an explicit `ResumeId`;
- saved search criteria.

AI configuration must be supplied through environment variables/user secrets, not committed keys:

```text
AI__Endpoint=https://YOUR-RESOURCE.openai.azure.com/
AI__ApiKey=YOUR-SECRET
AI__DeploymentName=YOUR-CHAT-DEPLOYMENT
```

Only job search feeds are free/no-key; the AI service may incur charges. `Background:BatchSize` (default 10) bounds package attempts per candidate per run. Completed packages are reused rather than regenerated. Failed packages retain failure state and can be retried from the existing package page. If AI is absent/disabled or the resume selection is ambiguous, discovery still completes; the reason drafting was skipped is logged. A discovery run's Completed status describes search completion, **not** completion of every package.

Packages persist in PostgreSQL and can be opened from each matched job's **Prepare application package** action. Select the same source resume used by the worker to find that package. The existing ZIP download contains:

- `resume.md` — customized, grounded resume;
- `cover-letter.md` — customized cover letter;
- `job-description.txt` — job snapshot used during generation;
- `job-link.txt` — application/source URL.

No automatic applications, form submissions, employer messages, or browser application automation are triggered by this pipeline. Review every generated document and apply manually. Markdown/ZIP output is supported; this update does not add PDF/DOCX export or automatic files in a host output directory.

### Unattended recurring runs

Set `Background:EnableSearchPipeline=true` (enabled in the API settings) and configure an explicit candidate opt-in list:

```text
Background__Candidates__0__CandidateId=YOUR-CANDIDATE-GUID
Background__Candidates__0__ResumeId=YOUR-UPLOADED-SOURCE-RESUME-GUID
```

The `jobhunter-search-packages` schedule runs at minute 15 every six hours, UTC. No candidates are silently enrolled: an empty `Candidates` list means no recurring candidate searches. The UI queue action works without enrollment. When the pipeline schedule is enabled, the older standalone AI match-score/resume-generation schedules are removed to avoid duplicate AI work. Other maintenance schedules remain.

Jobs have no automatic exception retry, limiting unexpected AI costs. PostgreSQL persists queued work across container restarts. A re-delivered job with a completed search can resume drafting and reuse completed packages. A search interrupted while still Running is marked Cancelled on re-delivery; start a new search to refresh its sources. Search itself is not checkpointed. Package leases protect simultaneous generation and expire after three minutes.

## Run in containers

`compose.public-search.yaml` runs PostgreSQL, the API/Hangfire worker and the web app. The worker runs inside the API container, not as a detached `Task.Run` tied to a browser request.

No `.env` file is required. Both Compose files explicitly use PostgreSQL username `jobhunterai` and password `jobhunterai` for the database and API. The API's base/development settings use the same credentials for local connections. Database passwords are not interpolated from the shell or an `.env` file. These fixed credentials are for local development only.

Optional AI credentials can be supplied through the current PowerShell session before running Compose; keep actual API keys out of committed YAML:

```powershell
$env:AI_ENDPOINT = 'https://YOUR-RESOURCE.openai.azure.com/'
$env:AI_API_KEY = 'YOUR-SECRET'
$env:AI_DEPLOYMENT_NAME = 'YOUR-CHAT-DEPLOYMENT'
```

The AI variables are optional for discovery-only operation. For an existing database, changing Compose settings does not change the stored PostgreSQL role password; the existing role must already have the configured password. Do not delete the volume to fix credentials. Both container connection strings disable unused GSS encryption negotiation (`GSS Encryption Mode=Disable`) for these password-authenticated deployments; no Kerberos library is needed for that negotiation. This does not disable SCRAM authentication.

Then run:

```sh
docker compose -f compose.public-search.yaml up --build -d
```

Open `http://localhost:8080`, create/select your candidate, upload the source resume, save search criteria, and choose background search. For recurring runs, uncomment and fill the candidate environment settings on the API service, then recreate the API container.

The database named volume `jobhunter-data` stores jobs, resumes, cover letters, packages and Hangfire work. Do not use `docker compose down -v` unless you intend to delete that data. Back up PostgreSQL. `Database:MigrateOnStartup=true` is explicitly enabled for this single-API local deployment; production multi-replica systems should run migrations as a separate deployment step.

The web port is bound to loopback only; PostgreSQL and the API are not published to the host. This sample is for local/manual-review use. Configure authentication, authorization, TLS, backups and secrets management before remote/multi-user deployment. Existing candidate IDs are selectors, not authorization.

### Repair an existing public-search API restart loop

Keep the same Compose file and database volume as the existing deployment. With the database healthy and its role password set to `jobhunterai`, validate configuration and recreate just the API (no `.env` file required):

```powershell
docker compose -f compose.public-search.yaml config --quiet
if ($LASTEXITCODE -eq 0) {
	docker compose -f compose.public-search.yaml up -d --build --force-recreate --no-deps api
}
docker compose -f compose.public-search.yaml ps -a
docker compose -f compose.public-search.yaml logs --tail=100 -f api
```

A plain `docker compose restart api` does not update the container's environment. The full resolved `docker compose config` or a raw environment dump can reveal secrets; use `--quiet` for syntax/interpolation checks.

The two Compose files are not interchangeable: `compose.public-search.yaml` uses `jobhunter-data`, web port 8080, and no host PostgreSQL port; `compose.yaml` uses `postgres-data`, web port 8082, and publishes PostgreSQL on loopback port 5432. Switching files may select a different existing database volume. You can identify a container's creation configuration without revealing credentials:

```powershell
docker inspect jobhunterai-api-1 --format '{{ index .Config.Labels "com.docker.compose.project.config_files" }}'
```

The public-search deployment's absent host PostgreSQL mapping also explains why a separately launched local API cannot reach `localhost:5432`. Add an explicit loopback database port override to that deployment if local debugging is needed; do not change the container connection hostname from `db` to `localhost`.

## Criteria, zero matches, results and logs

The current adapters retrieve feed/board listings, not server-side candidate searches. Arbeitnow receives a `page` parameter; RemoteOK uses `/api`; ATS requests select the configured employer board (Greenhouse also requests description content). No generic keyword or location parameters are invented. The full saved criteria are evaluated locally for every examined posting. An API adapter supporting documented search parameters should explicitly translate the relevant criteria when added.

In **Find matching jobs → Search history → View results**, expand **Saved criteria used for this run** to inspect the actual persisted criteria snapshot, not merely the currently edited form. All filter groups must pass: any title phrase, any configured location, a permitted work arrangement, **all** required keywords, and **none** of the excluded keywords. Matching is case-insensitive substring matching, not AI/synonym matching. A remote posting still has to satisfy any specified location filter; missing country data is not inferred. Unknown arrangement is excluded unless explicitly allowed.

New zero-match runs include rejection counts by title, location, arrangement, required keywords and excluded keywords. A posting can fail multiple filters, so these counts need not sum to the examined count. Counts use the last examined metadata for each unique job. These diagnostics do not relax any saved criteria. Old run warnings remain unchanged; rebuild and start a new run to obtain diagnostics.

The Arbeitnow warning now distinguishes a unique-posting limit from a pagination limit and includes the actual limit/page information. Increasing `PublicFeeds:MaxPages` alone cannot bypass a lower `Discovery:JobsPerBoard` cap. Compose exposes `DISCOVERY_JOBS_PER_SOURCE` (default 300, maximum 500) and `ARBEITNOW_MAX_PAGES` (default 3, maximum 10) through environment/.env overrides. Larger scans can still hit the total time limit.

Background search results appear in the same **Search history**. For a match, choose **Prepare application package**, select the source resume used by the worker, then **Refresh status** to view the generated resume, cover letter, failure code and ZIP download. Discovery Completed describes the search phase, not completion of document drafting. **Zero matches means no documents are generated.** Generated artifacts are in PostgreSQL, not the log directory. A public Hangfire dashboard is not enabled.

### Console logs (available before this update as well)

```sh
docker compose -f compose.public-search.yaml logs --tail=200 -f api
```

Before recreating an existing container, preserve old console logs if needed:

```sh
docker compose -f compose.public-search.yaml logs --no-color api > prior-api.log
```

### Persistent rolling API log files (after rebuild)

Compose configures `Background__LogDirectory=/app/logs` and mounts the named volume `jobhunter-logs`. Files are JSON lines, named `jobhunterai-YYYYMMDD.log` (with size-roll suffixes as needed), rotated daily/at 10 MiB, retaining up to 30 files. Background log cleanup also applies `LogRetentionDays`. The Docker console log driver is separately bounded to three 10 MiB files. The API image creates a writable log directory for its non-root user.

```sh
docker compose -f compose.public-search.yaml up --build -d
docker compose -f compose.public-search.yaml exec api sh -c 'ls -lh /app/logs'
docker compose -f compose.public-search.yaml cp api:/app/logs ./api-logs
```

Logs correlate events with `RunId` and `CandidateId`. Look for `Discovery started`, each `Discovery source`, `Discovery finished` (including filter-rejection counts), `Background discovery status`, drafting/skipped-drafting messages, completed package IDs and `Background pipeline completed` totals (attempted/completed/reused/needs-review/deferred). No resume/cover-letter bodies or full criteria values are deliberately logged by these diagnostics. AI unavailable, ambiguous source resume and batch limits are visible separately from a successful search. Treat all application logs as private.

Outside Docker, logs remain console-only when `Background:LogDirectory` is null. To enable files on Windows before starting the API:

```powershell
$env:Background__LogDirectory = Join-Path $env:LOCALAPPDATA 'JobHunterAI\logs'
dotnet run --project JobHunterAI.Api
```

## Verification

The update adds fixture-based public-feed mapping, pagination, identity, malformed-data and HTTP-failure tests; all-source/match-cap/failure-isolation tests; and scheduling/disabled-worker tests. The test tools in this editing session do not execute builds, containers or live requests. Run locally:

```sh
dotnet test JobHunterAI.UnitTests
dotnet test JobHunterAI.IntegrationTests
docker compose -f compose.public-search.yaml logs -f api
```

Verify live feed access and source terms in your deployment environment before scheduling unattended searches.
