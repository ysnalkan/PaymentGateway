
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Tests.Infrastructure;
using PaymentGateway.Domain.Enums;
using FluentAssertions;

namespace PaymentGateway.Api.Tests;

public class PaymentApiIntegrationTests : IDisposable
{
    private readonly FakeBankHandler _bank;
    private readonly PaymentGatewayApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaymentApiIntegrationTests()
    {
        _bank = new FakeBankHandler();
        _factory = new PaymentGatewayApplicationFactory(_bank);
        _client = _factory.CreateGatewayClient();
    }

    [Fact]
    public async Task When_BankAuthorizesPayment_Should_CreateRetrievablePayment()
    {
        // Arrange
        _bank.Authorized = true;
        _bank.Status = HttpStatusCode.OK;

        var request = ValidRequest();

        // Act
        using var post = await _client.PostAsJsonAsync("/api/payments", request);

        // Assert
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var location = post.Headers.Location;
        location.Should().NotBeNull();

        var created = await post.Content.ReadFromJsonAsync<PostPaymentResponse>();
        created.Should().NotBeNull();

        created!.Id.Should().NotBe(Guid.Empty);
        created.Status.Should().Be(PaymentStatus.Authorized);
        created.CardNumberLastFour.Should().Be("0013");
        created.Amount.Should().Be(request.Amount);
        created.Currency.Should().Be(request.Currency);
        created.ExpiryMonth.Should().Be(request.ExpiryMonth);
        created.ExpiryYear.Should().Be(request.ExpiryYear);

        var correlationId = post.Headers.GetValues("X-Correlation-ID").Single();

        correlationId.Should().NotBeNullOrWhiteSpace();
        _bank.LastCorrelationId.Should().Be(correlationId);
        _bank.LastPath.Should().Be("/payments");

        using var get = await _client.GetAsync(location);

        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await get.Content.ReadFromJsonAsync<GetPaymentResponse>();
        retrieved.Should().NotBeNull();

        retrieved!.Id.Should().Be(created.Id);
        retrieved.Status.Should().Be(created.Status);
        retrieved.CardNumberLastFour.Should().Be(created.CardNumberLastFour);
        retrieved.ExpiryMonth.Should().Be(created.ExpiryMonth);
        retrieved.ExpiryYear.Should().Be(created.ExpiryYear);
        retrieved.Currency.Should().Be(created.Currency);
        retrieved.Amount.Should().Be(created.Amount);

        _bank.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_BankIsUnavailable_Should_Return503WithCorrelationId()
    {
        // Arrange
        _bank.Status = HttpStatusCode.ServiceUnavailable;

        // Act
        using var response = await _client.PostAsJsonAsync("/api/payments", ValidRequest());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        correlationId.Should().NotBeNullOrWhiteSpace();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(correlationId);

        _bank.LastCorrelationId.Should().Be(correlationId);
        _bank.CallCount.Should().Be(1);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task When_BankTimesOut_Should_Return504WithCorrelationId()
    {
        // Arrange
        _bank.DelayResponse = true;

        // Act
        using var response = await _client.PostAsJsonAsync("/api/payments", ValidRequest());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        correlationId.Should().NotBeNullOrWhiteSpace();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetInt32().Should().Be(504);
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(correlationId);

        _bank.LastCorrelationId.Should().Be(correlationId);
        _bank.CallCount.Should().Be(1);
        _bank.WasCancelled.Should().BeTrue();
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task When_BankDeclinesPayment_Should_CreateRetrievablePayment()
    {
        // Arrange
        _bank.Authorized = false;

        // Act
        using var post = await _client.PostAsJsonAsync("/api/payments", ValidRequest());

        // Assert
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var location = post.Headers.Location;
        location.Should().NotBeNull();

        var created = await post.Content.ReadFromJsonAsync<PostPaymentResponse>();
        created.Should().NotBeNull();

        created!.Status.Should().Be(PaymentStatus.Declined);
        created.CardNumberLastFour.Should().Be("0013");

        using var get = await _client.GetAsync(location);

        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await get.Content.ReadFromJsonAsync<GetPaymentResponse>();
        retrieved.Should().NotBeNull();

        retrieved!.Id.Should().Be(created.Id);
        retrieved.Status.Should().Be(PaymentStatus.Declined);

        _bank.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_CircuitIsOpen_Should_StopCallingBank()
    {
        // Arrange
        _bank.Status = HttpStatusCode.ServiceUnavailable;

        // Act & Assert
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await _client.PostAsJsonAsync("/api/payments", ValidRequest());
            failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        _bank.CallCount.Should().Be(5);

        using var blocked = await _client.PostAsJsonAsync("/api/payments", ValidRequest());

        blocked.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _bank.CallCount.Should().Be(5);
        blocked.Headers.Location.Should().BeNull();

        var correlationId = blocked.Headers.GetValues("X-Correlation-ID").Single();
        correlationId.Should().NotBeNullOrWhiteSpace();

        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(correlationId);
    }

    [Fact]
    public async Task When_CardNumberIsMissing_Should_Return400WithoutCallingBank()
    {
        // Arrange
        _bank.Authorized = true;

        var request = ValidRequest();
        request.CardNumber = null;

        // Act
        using var response = await _client.PostAsJsonAsync("/api/payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        _bank.CallCount.Should().Be(0);

        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        correlationId.Should().NotBeNullOrWhiteSpace();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(correlationId);
        body.RootElement.TryGetProperty("traceId", out _).Should().BeFalse();

        var errors = body.RootElement.GetProperty("errors");

        errors.GetProperty("CardNumber")
            .GetArrayLength()
            .Should().Be(1);

        errors.GetProperty("CardNumber")[0]
            .GetString()
            .Should().Be("Card number is required.");
    }

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343240013",
        ExpiryMonth = 12,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
