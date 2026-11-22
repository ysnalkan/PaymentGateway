using PaymentGateway.Domain.Dtos;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Models;

namespace PaymentGateway.Domain.Extensions;

public static class PaymentMapperExtension
{
    public static Payment ToPayment(this BankPaymentResult bankResult, PostPaymentRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bankResult);

        return new Payment
        {
            Id = Guid.NewGuid(),
            Status = bankResult.Authorized
                ? PaymentStatus.Authorized
                : PaymentStatus.Declined,
            AuthorizationCode = bankResult.AuthorizationCode,
            CardNumberLastFour = request.CardNumber[^4..],
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount
        };
    }
    
    public static PostPaymentResponseDto ToResponseDto(this Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PostPaymentResponseDto
        {
            Id = payment.Id,
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };
    }
}