# Translation Provider Resilience

The translation provider is wrapped by a dependency-free resilience layer designed to bound latency and prevent repeated calls to an unhealthy downstream provider.

## Policy

- Each provider attempt has its own timeout.
- The complete operation has an overall deadline across attempts and retry delays.
- Transient timeouts, `TransientProviderException`, and `HttpRequestException` are retried.
- Client cancellation is never converted into a retry.
- Retry delays use bounded exponential backoff with random jitter.
- Consecutive failed operations open an in-process circuit breaker.
- Requests fail fast while the circuit is open.
- After the break duration, one subsequent request probes the provider and closes the circuit on success.

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `Translation:RequestTimeoutSeconds` | `10` | Maximum duration of one provider attempt |
| `Translation:OverallTimeoutSeconds` | `30` | Maximum duration of the complete provider operation |
| `Translation:RetryCount` | `2` | Retries after the initial attempt |
| `Translation:RetryBaseDelayMilliseconds` | `100` | Initial exponential-backoff delay |
| `Translation:RetryMaxDelayMilliseconds` | `2000` | Upper bound for one retry delay |
| `Translation:CircuitBreakerFailureThreshold` | `5` | Consecutive failed operations before opening the circuit |
| `Translation:CircuitBreakerBreakSeconds` | `20` | Duration for which requests fail fast |

All settings support standard ASP.NET Core environment variable overrides, for example:

```powershell
$env:TRANSLATION__OVERALLTIMEOUTSECONDS = "20"
$env:TRANSLATION__CIRCUITBREAKERFAILURETHRESHOLD = "8"
```

## Operational notes

The circuit breaker is intentionally local to each service process. This avoids introducing a distributed coordination dependency into the request path. In a multi-replica deployment, each replica independently assesses downstream health. Provider-side throttling should still be handled using shared tenant quotas and provider response metadata in a future distributed-governance layer.

The service does not log source or translated text in resilience events. Logs contain provider name, attempt number, delay, failure details, and circuit transitions.

## Remaining production evolution

The next upgrades should add:

1. Explicit provider error classification, including `429` and `Retry-After` handling.
2. OpenTelemetry spans and metrics for attempts, retries, timeouts, and circuit state.
3. A deterministic fault-injection provider for executable resilience specifications.
4. Distributed cache and durable persistence implementations.
5. Load and failure tests that publish measured P50, P95, and P99 latency.
