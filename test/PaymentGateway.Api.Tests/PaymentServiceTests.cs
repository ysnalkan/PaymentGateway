
using Moq;
using FluentAssertions;
using PaymentGateway.Domain.Dtos;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Interfaces;
using PaymentGateway.Domain.Models;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentServiceTests
{
    private readonly Mock<IAcquiringBankClient> _bank = new();
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentMetrics> _paymentMetrics = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        var factory = new Mock<IAcquiringBankClientFactory>();
        factory
            .Setup(x => x.GetClient("simulator"))
            .Returns(_bank.Object);

        _service = new PaymentService(factory.Object, _repository.Object, _paymentMetrics.Object);
    }

    [Theory]
    [InlineData(true, PaymentStatus.Authorized)]
    [InlineData(false, PaymentStatus.Declined)]
    public async Task When_BankReturnsResult_Should_SavePaymentAndReturnResponse(
        bool authorized,
        PaymentStatus expectedStatus)
    {
        // Arrange
        var request = ValidRequest();
        var token = CancellationToken.None;
        var authorizationCode = "test_authorization_code";
        Payment? savedPayment = null;

        _bank
            .Setup(x => x.ProcessPaymentAsync(request, token))
            .ReturnsAsync(new BankPaymentResult(authorized, authorizationCode));

        _repository
            .Setup(x => x.Add(It.IsAny<Payment>(), token))
            .Callback<Payment, CancellationToken>(
                (payment, _) => savedPayment = payment);

        // Act
        var response = await _service.ProcessPaymentAsync(request, token);
        response.Should().NotBeNull();

        // Assert
        var saved = savedPayment;
        saved.Should().NotBeNull();

        saved.Id.Should().NotBe(Guid.Empty);
        saved.Status.Should().Be(expectedStatus);
        saved.AuthorizationCode.Should().Be(authorizationCode);
        saved.CardNumberLastFour.Should().Be("0013");
        saved.Amount.Should().Be(request.Amount);
        saved.Currency.Should().Be(request.Currency);
        saved.ExpiryMonth.Should().Be(request.ExpiryMonth);
        saved.ExpiryYear.Should().Be(request.ExpiryYear);

        response!.Id.Should().Be(saved.Id);
        response.Status.Should().Be(saved.Status);
        response.CardNumberLastFour.Should().Be(saved.CardNumberLastFour);
        response.Amount.Should().Be(saved.Amount);
        response.Currency.Should().Be(saved.Currency);
        response.ExpiryMonth.Should().Be(saved.ExpiryMonth);
        response.ExpiryYear.Should().Be(saved.ExpiryYear);

        _bank.Verify(
            x => x.ProcessPaymentAsync(request, token),
            Times.Once);

        _repository.Verify(
            x => x.Add(It.IsAny<Payment>(), token),
            Times.Once);
    }

    [Fact]
    public async Task When_BankFails_Should_NotSavePayment()
    {
        // Arrange
        var request = ValidRequest();
        var token = CancellationToken.None;
        var expectedError = new HttpRequestException("Bank is unavailable.");

        _bank
            .Setup(x => x.ProcessPaymentAsync(request, token))
            .ThrowsAsync(expectedError);

        // Act & Assert
        var act = async () =>
            await _service.ProcessPaymentAsync(request, token);

        var error = await act.Should().ThrowAsync<HttpRequestException>();

        error.Which.Should().BeSameAs(expectedError);

        _bank.Verify(
            x => x.ProcessPaymentAsync(request, token),
            Times.Once);

        _repository.Verify(
            x => x.Add(
                It.IsAny<Payment>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task When_SavingFails_Should_NotCallBankAgain()
    {
        // Arrange
        var request = ValidRequest();
        var token = CancellationToken.None;
        var expectedError = new InvalidOperationException("Saving failed.");

        _bank.Setup(x => x.ProcessPaymentAsync(request, token))
            .ReturnsAsync(new BankPaymentResult(true, null));

        _repository.Setup(x => x.Add(It.IsAny<Payment>(), token))
            .Throws(expectedError);

        // Act & Assert
        var act = async () =>
            await _service.ProcessPaymentAsync(request, token);

        var error = await act.Should().ThrowAsync<InvalidOperationException>();

        error.Which.Should().BeSameAs(expectedError);

        _bank.Verify(
            x => x.ProcessPaymentAsync(request, token),
            Times.Once);

        _repository.Verify(
            x => x.Add(It.IsAny<Payment>(), token),
            Times.Once);
    }

    private static PostPaymentRequestDto ValidRequest() => new()
    {
        CardNumber = "2222405343240013",
        ExpiryMonth = 12,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };
}
