using PaymentGateway.Domain.Dtos;
using PaymentGateway.Domain.Extensions;
using PaymentGateway.Domain.Interfaces;
using PaymentGateway.Domain.Models;

namespace PaymentGateway.Domain.Services;

public class PaymentService(
    IAcquiringBankClientFactory bankClientFactory,
    IPaymentRepository paymentRepository,
    IPaymentMetrics paymentMetrics) : IPaymentService
{

    public async Task<PostPaymentResponseDto?> ProcessPaymentAsync(PostPaymentRequestDto request, CancellationToken cancellationToken)
    {
        var bankResult = await SendToBankAsync(request, "simulator", cancellationToken);
        RecordBusinessMetrics(bankResult);
        var payment = bankResult.ToPayment(request);
        paymentRepository.Add(payment, cancellationToken);
        
        return payment.ToResponseDto();
    }
    
    public PostPaymentResponseDto? GetPayment( Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payment = paymentRepository.GetById(id, cancellationToken);

        return payment is null
            ? null
            : payment.ToResponseDto();
    }
    
    private async Task<BankPaymentResult> SendToBankAsync(
        PostPaymentRequestDto request,
        string bankKey,
        CancellationToken cancellationToken)
    {
        var bankClient = bankClientFactory.GetClient(bankKey);

        return await bankClient.ProcessPaymentAsync(
            request,
            cancellationToken);
    }

    private void RecordBusinessMetrics(BankPaymentResult bankResult)
    {
        if (bankResult.Authorized)
        {
            paymentMetrics.RecordAuthorizedPayment();
        }
        else
        {
            paymentMetrics.RecordDeclinedPayment();
        }
    }
}