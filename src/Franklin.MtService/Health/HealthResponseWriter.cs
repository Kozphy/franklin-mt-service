using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Franklin.MtService.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(
            new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                durationMilliseconds = (long)report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(
                    entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString().ToLowerInvariant(),
                        description = entry.Value.Description
                    })
            });
    }
}
