# Architecture

## Request flow

```text
Client
  -> correlation ID middleware
  -> fixed-window rate limiter
  -> translation endpoint and validation
  -> TranslationService
     -> TTL store/cache lookup
     -> resilient provider (timeout + retry)
        -> local provider or Azure AI Translator
     -> store result
  -> JSON response
```

## Main decisions

### Provider boundary

`ITranslationProvider` isolates the service from a translation vendor. The local
provider makes development and CI deterministic. `AzureTranslatorProvider` uses the
Azure REST contract and receives secrets only through configuration.

### Storage boundary

`ITranslationStore` provides lookup by resource ID and a deterministic content key.
The included store is bounded, thread-safe, and expires records. A production
implementation should use:

- Azure Cosmos DB when multi-region, low-latency reads and flexible documents matter.
- Azure Table Storage when the access pattern remains simple and cost is the priority.
- Redis as a short-lived L1 cache in front of either durable option at high scale.

A durable record should partition by tenant or a stable hash prefix, use the request
hash as an idempotency key, and use native TTL for deletion.

### Reliability

- Every provider attempt has a timeout.
- Transient HTTP, timeout, and upstream 429/5xx failures are retried with exponential
  backoff.
- Per-client fixed-window rate limiting protects the service.
- Readiness verifies that the selected provider is configured.
- Correlation IDs are returned to callers and added to structured log scopes.

At larger scale, add a circuit breaker, bulkhead limits per provider, distributed
tracing, OpenTelemetry export, and SLO-based alerting.

### Scalability

The API process is stateless except for its replaceable local cache. Kubernetes
manifests include resource requests/limits, rolling updates, disruption protection,
and CPU-based horizontal scaling. When durable storage is enabled, replicas can scale
independently.

## Suggested production topology

```text
Azure Front Door / API Management
  -> AKS or Azure Container Apps
     -> MT Service replicas
        -> Azure AI Translator
        -> Cosmos DB or Table Storage
        -> Application Insights / OpenTelemetry collector
```

Secrets should come from Azure Key Vault through workload identity. Images should be
scanned and promoted across environments by immutable digest.
