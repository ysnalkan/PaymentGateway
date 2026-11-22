using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Api.Extensions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Validators;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(
    IPaymentService paymentService,
    IValidator<PostPaymentRequest> validator,
    ILogger<PaymentsController> logger) : Controller
{
    [HttpPost]
    [ProducesResponseType( typeof(PostPaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync(
        [FromBody] PostPaymentRequest request, 
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            logger.LogWarning("Payment request validation failed.");

            foreach (var error in validationResult.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);

            return ValidationProblem(ModelState);
        }

        var requestDto = request.ToPostPaymentRequestDto();
        
        logger.LogInformation(
            "Payment processing started. LastFour: {CardLastFour}, " +
            "Amount: {Amount}, Currency: {Currency}",
            requestDto.CardNumber[^4..],
            requestDto.Amount,
            requestDto.Currency);
        
        var paymentDto = 
            await paymentService.ProcessPaymentAsync(requestDto, cancellationToken)
            ?? throw new InvalidOperationException("Payment processing returned no result.");

        var payment = paymentDto.ToPostPaymentResponse();
        
        logger.LogInformation(
            "Payment processing completed. PaymentId: {PaymentId}, Status: {PaymentStatus}",
            payment.Id,
            payment.Status);
        
        return new CreatedAtActionResult(nameof(GetPayment), "Payments", new { id = payment.Id }, payment);
    }
    
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public ActionResult<GetPaymentResponse> GetPayment(
        Guid id,
        CancellationToken cancellationToken)
    {
        var payment = paymentService.GetPayment(id, cancellationToken);

        if (payment is null)
            return NotFound();

        return Ok(payment.ToGetPaymentResponse());
    }
}