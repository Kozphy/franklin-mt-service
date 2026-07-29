using System.Threading.RateLimiting;
using Franklin.MtService.Abstractions;
using Franklin.MtService.Configuration;
using Franklin.MtService.Domain;
using Franklin.MtService.Health;
using Franklin.MtService.Middleware;
using Franklin.MtService.Models;
using Franklin.MtService.Providers;
using Franklin.MtService.Services;
using Franklin.MtService.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(
    options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });

builder.Services
    .AddOptions<TranslationOptions>()
    .Bind(builder.Configuration.GetSection(TranslationOptions.SectionName))
    .Validate(
        options =>
            string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Provider, "Azure", StringComparison.OrdinalIgnoreCase),
        "Translation:Provider must be Local or Azure.")
    .Validate(
        options =>
            options.MaxTextLength is > 0 and <= 50_000 &&
            options.CacheTtlMinutes is > 0 and <= 10_080 &&
            options.MaxCacheEntries is > 0 and <= 1_000_000 &&
            options.RequestTimeoutSeconds is > 0 and <= 120 &&
            options.RetryCount is >= 0 and <= 5,
        "Translation numeric settings are outside their supported ranges.")
    .ValidateOnStart();

builder.Services
    .AddOptions<AzureTranslatorOptions>()
    .Bind(builder.Configuration.GetSection(AzureTranslatorOptions.SectionName));

builder.Services
    .AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .Validate(
        options =>
            options.PermitLimit > 0 &&
            options.WindowSeconds > 0 &&
            options.QueueLimit >= 0,
        "Rate-limit settings must be non-negative, and permit/window values must be positive.")
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddHttpClient(nameof(AzureTranslatorProvider));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<LocalDictionaryTranslationProvider>();
builder.Services.AddSingleton<AzureTranslatorProvider>();
builder.Services.AddSingleton<ITranslationProvider>(
    serviceProvider =>
    {
        var translationOptions = serviceProvider
            .GetRequiredService<IOptions<TranslationOptions>>()
            .Value;
        ITranslationProvider provider =
            string.Equals(
                translationOptions.Provider,
                "Azure",
                StringComparison.OrdinalIgnoreCase)
                ? serviceProvider.GetRequiredService<AzureTranslatorProvider>()
                : serviceProvider.GetRequiredService<LocalDictionaryTranslationProvider>();

        return new ResilientTranslationProvider(
            provider,
            translationOptions,
            serviceProvider.GetRequiredService<ILogger<ResilientTranslationProvider>>());
    });
builder.Services.AddSingleton<ITranslationStore>(
    serviceProvider =>
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<TranslationOptions>>()
            .Value;
        return new InMemoryTranslationStore(
            options.MaxCacheEntries,
            serviceProvider.GetRequiredService<TimeProvider>());
    });
builder.Services.AddSingleton<ServiceMetrics>();
builder.Services.AddSingleton<TranslationService>();
builder.Services
    .AddHealthChecks()
    .AddCheck<TranslationProviderHealthCheck>(
        "translation_provider",
        tags: ["ready"]);

builder.Services.AddRateLimiter(
    options =>
    {
        var configured = builder.Configuration
            .GetSection(RateLimitOptions.SectionName)
            .Get<RateLimitOptions>() ?? new RateLimitOptions();

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = configured.PermitLimit,
                        Window = TimeSpan.FromSeconds(configured.WindowSeconds),
                        QueueLimit = configured.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.ContentType = "application/problem+json";
            await context.HttpContext.Response.WriteAsJsonAsync(
                new
                {
                    type = "https://httpstatuses.com/429",
                    title = "Too many requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "The request rate limit was exceeded. Try again later.",
                    traceId = context.HttpContext.TraceIdentifier
                },
                cancellationToken);
        };
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();

app.MapGet(
        "/",
        () =>
            Results.Ok(
                new
                {
                    service = "Franklin Machine Translation Service",
                    version = "v1",
                    documentation = "/openapi/v1.json",
                    health = new
                    {
                        live = "/health/live",
                        ready = "/health/ready"
                    }
                }))
    .ExcludeFromDescription();

app.MapPost(
        "/api/v1/translations",
        async (
            TranslationRequest request,
            TranslationService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var translation = await service.TranslateAsync(
                    request,
                    cancellationToken);
                return Results.Created(
                    $"/api/v1/translations/{translation.Id}",
                    translation);
            }
            catch (TranslationValidationException exception)
            {
                return Results.ValidationProblem(
                    exception.Errors.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal),
                    title: "Invalid translation request");
            }
            catch (ProviderUnavailableException exception)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Translation provider unavailable",
                    detail: exception.Message);
            }
        })
    .WithName("CreateTranslation");

app.MapGet(
        "/api/v1/translations/{id:guid}",
        async (
            Guid id,
            TranslationService service,
            CancellationToken cancellationToken) =>
        {
            var translation = await service.GetAsync(id, cancellationToken);
            return translation is null
                ? Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Translation not found",
                    detail: "The translation does not exist or has expired.")
                : Results.Ok(translation);
        })
    .WithName("GetTranslation");

app.MapGet("/api/v1/languages", () => Results.Ok(LanguageCatalog.All))
    .WithName("ListLanguages");

app.MapGet(
        "/api/v1/status",
        (
            ServiceMetrics metrics,
            ITranslationProvider provider,
            ITranslationStore store) =>
            Results.Ok(metrics.Snapshot(provider, store)))
    .WithName("GetServiceStatus");

app.MapGet(
        "/openapi/v1.json",
        (IWebHostEnvironment environment) =>
            Results.File(
                Path.Combine(environment.ContentRootPath, "openapi.json"),
                "application/json"))
    .ExcludeFromDescription();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync
    });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = HealthResponseWriter.WriteAsync
    });

app.Run();
