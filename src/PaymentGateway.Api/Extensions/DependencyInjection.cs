using FluentValidation;

using PaymentGateway.Api.Metrics;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Validators;
using PaymentGateway.Domain.Interfaces;

namespace PaymentGateway.Api.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services)
    {
        services.AddScoped<
            IValidator<PostPaymentRequest>,
            PostPaymentRequestValidator>();

        services.AddSingleton<IPaymentMetrics, PaymentMetrics>();

        return services;
    }
}
