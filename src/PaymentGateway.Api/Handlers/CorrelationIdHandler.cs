using System.Diagnostics;

namespace PaymentGateway.Api.Handlers;

public sealed class CorrelationIdHandler(IHttpContextAccessor contextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken)
    {

        var correlationId =Activity.Current?.TraceId.ToString()
                        ?? contextAccessor.HttpContext?.TraceIdentifier;

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.Remove("X-Correlation-ID");
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}