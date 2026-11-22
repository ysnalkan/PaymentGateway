
using FluentValidation;

using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validators;

public class PostPaymentRequestValidator
    : AbstractValidator<PostPaymentRequest>
{
    public PostPaymentRequestValidator()
    {
        RuleFor(x => x.CardNumber)
            .NotEmpty()
            .WithMessage("Card number is required.")
            .Matches(@"^[0-9]{14,19}$")
            .When(
                x => !string.IsNullOrEmpty(x.CardNumber),
                ApplyConditionTo.CurrentValidator)
            .WithMessage("Card number must contain between 14 and 19 digits.");

        RuleFor(x => x.Cvv)
            .NotEmpty()
            .WithMessage("CVV is required.")
            .Matches(@"^[0-9]{3,4}$")
            .When(
                x => !string.IsNullOrEmpty(x.Cvv),
                ApplyConditionTo.CurrentValidator)
            .WithMessage("CVV must contain 3 or 4 digits.");

        RuleFor(x => x.ExpiryMonth)
            .InclusiveBetween(1, 12)
            .WithMessage("Expiry month must be between 1 and 12.");

        RuleFor(x => x)
            .Must(HaveFutureExpiryDate)
            .WithMessage("Expiry month and year must be in the future.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required.")
            .Must(currency =>
                string.IsNullOrEmpty(currency) ||
                currency is "GBP" or "USD" or "EUR")
            .WithMessage("Currency must be GBP, USD or EUR.");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero.");
    }

    private static bool HaveFutureExpiryDate(PostPaymentRequest request)
    {
        var today = DateTime.UtcNow;

        return request.ExpiryYear > today.Year ||
               (request.ExpiryYear == today.Year &&
                request.ExpiryMonth > today.Month);
    }
}
