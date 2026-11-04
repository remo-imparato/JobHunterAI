# JobHunterAI

JobHunterAI is a .NET 10 solution for a job search and application assistant. **Phases 1 and 2** are implemented: project boundaries, API and Blazor Server hosts, structured logging, configuration validation, error handling, health checks, development OpenAPI/Swagger UI, Docker support, and a validated domain model with unit tests. No job search, persistence, AI integration, or application submission endpoint exists yet. The pre-existing `JobHunter/` project is not included in the solution.

## Prerequisites

- .NET 10 SDK
- Docker with Compose (optional)

## Local development

```powershell
dotnet restore JobHunterAI.slnx
dotnet build JobHunterAI.slnx
dotnet test JobHunterAI.slnx
dotnet run --project JobHunterAI.Api --launch-profile http
# In a second terminal:
dotnet run --project JobHunterAI.Web --launch-profile http
```

API health: `http://localhost:5283/health`. Development OpenAPI: `http://localhost:5283/openapi/v1.json`; Swagger UI: `http://localhost:5283/swagger`. Web: `http://localhost:5137/`. The development Web API address is set in `JobHunterAI.Web/appsettings.Development.json`.

## Containers

```powershell
docker compose up --build
```

API: `http://localhost:8081/health`; Web: `http://localhost:8082/`. Containers communicate over the Compose network. No database or AI credentials are required for Phases 1 and 2.

## Configuration and security

The API requires `JobHunterAI:DisplayName` (environment variable `JobHunterAI__DisplayName`). The Web host requires an absolute HTTP(S) `Api:BaseUrl` (environment variable `Api__BaseUrl`); set it to the API's reachable address in non-development deployments. Invalid options fail startup. Store future credentials in user secrets or a managed secret store, not in source control. OpenAPI and Swagger UI are available only in Development. Unhandled API exceptions return a generic HTTP 500 response; details are logged server-side. TLS termination and trusted proxy settings must be configured by the deployment platform before exposing either host publicly.

## Solution boundaries

`Domain` has no dependencies; `Application` references `Domain`; `Infrastructure` and `AI` reference `Application`; `Api` references `Application`, `Infrastructure`, and `AI`. `Web` uses the API through HTTP rather than referencing the executable API assembly. Candidate profiles own qualifications and experience, resumes own versions, and applications own status events. The application aggregate blocks submission without recorded user approval, but authenticated approval and submission workflows are later-phase work. Persistence and provider integrations are also pending; **do not deploy for real candidate data or applications yet**.
