# Franklin Machine Translation Service

A production-oriented C#/.NET 8 REST service derived from the Franklin MT Service
project brief. The project includes a safe local translation provider for development
and an optional Azure AI Translator provider for production integration.

## What is included

- Versioned REST endpoints for creating and retrieving translations
- Input validation, request timeouts, retries, rate limiting, and request correlation IDs
- Thread-safe TTL cache behind a NoSQL-friendly storage abstraction
- Liveness and readiness health checks
- Lightweight service metrics and a checked-in OpenAPI contract
- Executable dependency-free specifications
- Docker, Docker Compose, Kubernetes, and Azure Pipelines assets
- No third-party NuGet dependencies

## Quick start

Prerequisites: .NET 8 SDK.

```powershell
dotnet build Franklin.MtService.sln -c Release
dotnet run --project tests/Franklin.MtService.Specs -c Release
dotnet run --project src/Franklin.MtService
```

The API listens on `http://localhost:8080` by default.

```powershell
$body = @{
  text = "Hello world"
  sourceLanguage = "en"
  targetLanguage = "es"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:8080/api/v1/translations" `
  -ContentType "application/json" `
  -Body $body
```

Useful endpoints:

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v1/translations` | Translate text |
| `GET /api/v1/translations/{id}` | Retrieve a non-expired translation |
| `GET /api/v1/languages` | List supported demo languages |
| `GET /api/v1/status` | View uptime, provider, cache, and counters |
| `GET /health/live` | Process liveness |
| `GET /health/ready` | Provider readiness |
| `GET /openapi/v1.json` | OpenAPI 3.0 contract |

## Translation providers

### Local

`Local` is the default. It is deterministic, offline, and intended for demos and
integration tests. It translates a small phrase/word catalog and clearly prefixes
unmapped input. It is not intended to represent production translation quality.

### Azure AI Translator

Set these values using environment variables, a secret store, or deployment
configuration. Never commit the key.

```powershell
$env:TRANSLATION__PROVIDER = "Azure"
$env:AZURETRANSLATOR__KEY = "<secret>"
$env:AZURETRANSLATOR__REGION = "<resource-region>"
$env:AZURETRANSLATOR__ENDPOINT = "https://api.cognitive.microsofttranslator.com"
dotnet run --project src/Franklin.MtService
```

The app fails its readiness check when Azure is selected but its configuration is
incomplete.

## Containers

```powershell
docker compose up --build
```

The image runs as a non-root user, exposes port 8080, and includes a container health
check. Kubernetes manifests are under `infra/k8s`.

## Configuration

All settings support standard ASP.NET Core environment-variable overrides using
double underscores.

| Setting | Default | Meaning |
| --- | ---: | --- |
| `Translation:Provider` | `Local` | `Local` or `Azure` |
| `Translation:MaxTextLength` | `5000` | Maximum request characters |
| `Translation:CacheTtlMinutes` | `60` | Translation retention |
| `Translation:MaxCacheEntries` | `10000` | In-memory cache limit |
| `Translation:RequestTimeoutSeconds` | `10` | Per-provider attempt timeout |
| `Translation:RetryCount` | `2` | Retry count after the first attempt |
| `RateLimit:PermitLimit` | `60` | Requests per fixed window and client |
| `RateLimit:WindowSeconds` | `60` | Fixed-window duration |

## Architecture and production evolution

The application keeps HTTP concerns, orchestration, providers, and persistence
separate. `ITranslationStore` is intentionally shaped like a key-value/NoSQL access
layer. The in-memory implementation is ideal for local use; production deployments
can replace it with Azure Cosmos DB or Azure Table Storage without changing endpoint
code. See [docs/architecture.md](docs/architecture.md) for the deployment path and
operational decisions.

## CI/CD

`azure-pipelines.yml` restores, builds, runs the executable specifications, publishes
the API, builds a container, and publishes Kubernetes manifests. Set the pipeline
variables `dockerRegistryServiceConnection` and `containerRepository` for your Azure
Container Registry setup.
