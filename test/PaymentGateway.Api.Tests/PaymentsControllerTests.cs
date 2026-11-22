
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Tests.Infrastructure;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Interfaces;
using PaymentGateway.Domain.Models;
using FluentAssertions;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests : IAsyncDisposable
{
    private readonly PaymentGatewayApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly IPaymentRepository _repository;

    public PaymentsControllerTests()
    {
        _factory = new PaymentGatewayApplicationFactory();
        _client = _factory.CreateGatewayClient();

        _repository = _factory.Services
            .GetRequiredService<IPaymentRepository>();
    }

    [Fact]
    public async Task When_PaymentExists_Should_ReturnPayment()
    {
        // Arrange
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Authorized,
            CardNumberLastFour = "0012",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Amount = 1050,
            Currency = "GBP"
        };

        _repository.Add(payment, CancellationToken.None);

        // Act
        using var response = await _client.GetAsync($"/api/payments/{payment.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await response.Content.ReadFromJsonAsync<GetPaymentResponse>();
        retrieved.Should().NotBeNull();

        retrieved.Id.Should().Be(payment.Id);
        retrieved.Status.Should().Be(payment.Status);
        retrieved.CardNumberLastFour.Should().Be(payment.CardNumberLastFour);
        retrieved.ExpiryMonth.Should().Be(payment.ExpiryMonth);
        retrieved.ExpiryYear.Should().Be(payment.ExpiryYear);
        retrieved.Amount.Should().Be(payment.Amount);
        retrieved.Currency.Should().Be(payment.Currency);
    }

    [Fact]
    public async Task When_PaymentDoesNotExist_Should_Return404()
    {
        // Arrange
        var missingPaymentId = Guid.NewGuid();

        // Act
        using var response = await _client.GetAsync($"/api/payments/{missingPaymentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
