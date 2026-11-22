using PaymentGateway.Api.Metrics;

namespace PaymentGateway.Api.Middleware;

public class ErrorMetricsMiddleware
{
    private readonly RequestDelegate _next;

    public ErrorMetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (context.Response.StatusCode >= 500)
        {
            PaymentMetrics.HttpErrors.Add(
                1,
                new KeyValuePair<string, object?>(
                    "http.response.status_code",
                    context.Response.StatusCode));
        }
    }
}