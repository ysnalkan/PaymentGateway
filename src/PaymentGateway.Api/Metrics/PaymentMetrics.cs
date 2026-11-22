using System.Diagnostics.Metrics;

using PaymentGateway.Domain.Interfaces;

namespace PaymentGateway.Api.Metrics;

public class PaymentMetrics : IPaymentMetrics
{
    private static readonly Meter Meter =
        new("PaymentGateway.Api");

    public static readonly Counter<long> HttpErrors =
        Meter.CreateCounter<long>("payment.http.errors");

    private static readonly Counter<long> AuthorizedPayments =
        Meter.CreateCounter<long>("payment.authorized.count");

    private static readonly Counter<long> DeclinedPayments =
        Meter.CreateCounter<long>("payment.declined.count");

    public void RecordHttpError(int statusCode)
    {
        HttpErrors.Add(1,
            new KeyValuePair<string, object?>(
                "http.response.status_code", statusCode));
    }

    public void RecordAuthorizedPayment()
    {
        AuthorizedPayments.Add(1);
    }

    public void RecordDeclinedPayment()
    {
        DeclinedPayments.Add(1);
    }
}

