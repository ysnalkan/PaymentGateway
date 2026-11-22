# Payment Gateway

A .NET 8 payment gateway developed for the Checkout.com payment gateway challenge. The application validates incoming payment requests, forwards valid transactions to the supplied acquiring bank simulator, keeps completed payment decisions in memory, and provides an endpoint for retrieving payments by ID.

The implementation keeps the payment flow straightforward while incorporating structured logging, metrics, distributed tracing, correlation IDs, centralized error handling, and HTTP resilience.

## Getting Started

### Prerequisites

- .NET 8 SDK
- Docker with Docker Compose
- A trusted local HTTPS development certificate

### Start the application

Run the following commands from the repository root:

```bash
# Start the acquiring bank simulator
docker compose up -d

# Trust the HTTPS development certificate if required
dotnet dev-certs https --trust

# Run the API
dotnet run --project src/PaymentGateway.Api --launch-profile PaymentGateway.Api
```

### Application endpoints

| Component | Address |
|---|---|
| Gateway API | https://localhost:7092 |
| Swagger UI | https://localhost:7092/swagger |
| Bank simulator | http://localhost:8080/payments |
| Aspire Dashboard | http://localhost:18888 |

Docker Compose starts the bank simulator, while the API runs separately through the .NET CLI.

The Aspire Dashboard is an optional service and requires the additional Compose configuration:

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d
```

Swagger is enabled only in the Development environment.

## API

### Create a payment

`POST /api/payments`

Example request:

```bash
curl -i https://localhost:7092/api/payments \
  -H 'Content-Type: application/json' \
  -d '{
    "cardNumber": "2222405343240013",
    "expiryMonth": 12,
    "expiryYear": 2030,
    "currency": "GBP",
    "amount": 1050,
    "cvv": "123"
  }'
```

Use an expiry date later than the current UTC month. The `amount` property represents minor currency units, so `1050` corresponds to GBP 10.50.

When the bank returns a payment decision, the API creates a record and responds with `201 Created`. A declined transaction is also considered a completed bank decision and is stored in the same way as an authorized payment.

The response includes a `Location` header containing the URL of the newly created resource.

Example response (the ID is generated per payment):

```json
{
  "id": "3870f1b0-e274-4e90-8fb6-19f04faebbf1",
  "status": "Authorized",
  "cardNumberLastFour": "0013",
  "expiryMonth": 12,
  "expiryYear": 2030,
  "currency": "GBP",
  "amount": 1050
}
```

### Retrieve a payment

`GET /api/payments/{id}`

```bash
curl -i https://localhost:7092/api/payments/3870f1b0-e274-4e90-8fb6-19f04faebbf1
```

Use the identifier returned by the POST endpoint.

- `200 OK` – The requested payment exists and its details are returned.
- `404 Not Found` – No matching payment was found.

Retrieval reads directly from the in-memory repository. No additional request is sent to the acquiring bank.

### Input validation

The API validates payment details before initiating any bank communication. Requests that fail validation receive `400 Bad Request`.

| Field | Rule |
|---|---|
| `cardNumber` | Required string containing 14–19 ASCII digits |
| `expiryMonth` | Integer between 1 and 12 |
| `expiryYear` | Combined month and year must be later than the current UTC month |
| `currency` | Required, case-sensitive `GBP`, `USD`, or `EUR` |
| `amount` | Positive integer expressed in minor currency units |
| `cvv` | Required string containing 3 or 4 ASCII digits |

Card numbers and CVVs are represented as strings to preserve leading zeroes. The same applies to the last four digits included in the payment response.

The validator collects validation errors instead of throwing exceptions for invalid field values. Malformed JSON is handled separately by ASP.NET Core model binding.

### HTTP response codes

The following table describes the responses exposed by the API:

| Code | Meaning |
|---|---|
| `201` | Payment created with an `Authorized` or `Declined` decision |
| `200` | Stored payment retrieved successfully |
| `400` | Invalid request |
| `404` | Payment not found |
| `500` | Unexpected application error |
| `502` | Unexpected bank HTTP response or invalid bank JSON |
| `503` | Bank unavailable, connection failure, or open circuit |
| `504` | Bank HTTP operation timed out |

Validation failures and centrally handled exceptions use Problem Details JSON. Validation responses also include an `errors` collection.

Error responses contain a `correlationId`, which is also returned through the `X-Correlation-ID` response header.

Swagger documents the expected success and error response types for the API endpoints.

## Architecture

### Project structure

The solution is organised into the following projects and directories:

```text
src/
  PaymentGateway.Api/              HTTP endpoints, validation, API mapping, error handling
  PaymentGateway.Domain/           Payment flow, models, DTOs, interfaces, bank client factory
  PaymentGateway.Infrastructure/   In-memory repository and simulator HTTP client

test/
  PaymentGateway.Api.Tests/        Unit and HTTP integration tests

imposters/                          Supplied bank simulator configuration
```

### Project dependencies

The API project references both Domain and Infrastructure.

Infrastructure implements the interfaces declared by Domain, while Domain remains independent of the Infrastructure layer.

The API startup process registers the relevant implementations through dependency injection service extensions.

### Payment processing flow

A payment moves through the following stages:

1. The incoming request is validated.
2. Validated request data is mapped to the service DTO.
3. The bank client factory resolves the configured acquiring bank client.
4. The payment is submitted to the bank and its response is awaited.
5. The bank decision is mapped to a payment model, retaining only the last four card digits.
6. The payment is saved in the repository and the API response is returned.

### Persistence

The payment repository is registered as a singleton and uses `ConcurrentDictionary` for in-memory storage.

It supports adding new payments and retrieving existing ones by ID. Duplicate identifiers are rejected, and an existing record is never overwritten by a duplicate entry.

### Acquiring bank integration

Bank communication is implemented with a named `HttpClient` and keyed dependency injection.

A dedicated factory is responsible for resolving the correct client using its registration key.

Currently, the payment service uses the `simulator` client. Supporting other acquiring banks would require additional client registrations and a bank-selection policy.

Dynamic bank selection and routing are not currently implemented.

### Resilience

The outgoing HTTP pipeline uses Polly through `Microsoft.Extensions.Http.Resilience`.

The configuration includes:

- **Timeout:** Each bank HTTP operation has a 10-second timeout. The default `HttpClient.Timeout` is disabled to avoid overlapping timeout configurations.
- **Circuit breaker:** Uses a 50% failure ratio, a minimum throughput of 5 requests, a 30-second sampling window, and a 30-second break duration.

The circuit breaker considers the following conditions as failures:

- HTTP 5xx responses
- Connection exceptions
- Polly timeout exceptions

A normal declined payment is treated as a valid business result and does not contribute to the circuit breaker's failure count.

Automatic retries are intentionally not configured. If a bank processes a payment but the response is lost or delayed, retrying the request could potentially submit the same payment again.

Technical failures during bank communication do not produce an `Authorized` or `Declined` payment record.

## Observability

The API uses OpenTelemetry for centralized observability, with the .NET Aspire Dashboard as the telemetry viewer.

### Correlation ID

The API generates a fresh correlation ID for every incoming request. It does not reuse an incoming correlation header.

- The correlation ID is returned in the `X-Correlation-ID` response header.
- It is included in the structured logging scope.
- It is forwarded to the Bank Simulator using the same header.

### Logging

Structured logs are exported to the .NET Aspire Dashboard using the OpenTelemetry Protocol (OTLP).

Console logging is also enabled using the JSON Console provider. This allows logs to be inspected locally in the application console or through Docker container logs.

Logs include payment outcomes, bank HTTP status codes, and request context. The logging scope also contains the directly connected client's IP address.

The application explicitly logs only the last four card digits. It does not log the full request body, full card number, CVV, or authorization code.

### Metrics

OpenTelemetry collects application and HTTP metrics, including:

- `http.server.request.duration` – incoming HTTP request duration.
-  HTTP client metrics – outgoing HTTP request performance.
- .NET Runtime metrics – runtime and resource-related measurements.
- `payment.http.errors` – HTTP 5xx error count.
- `payment.authorized.count` – authorized payment count.
- `payment.declined.count` – declined payment count.

### Distributed Tracing

OpenTelemetry tracing is enabled for ASP.NET Core requests and outgoing HTTP client calls.

This allows request execution to be followed across the Payment API and Bank Simulator, including dependency calls and their durations.

### Aspire Dashboard

The .NET Aspire Dashboard provides a centralized view of:

- Structured logs
- Metrics
- Distributed traces

The dashboard and OTLP endpoints are configured through Docker Compose.

To start the simulator and Dashboard together:

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d
```

The dashboard is available locally at:

`http://localhost:18888`

## Tests and Coverage

### Running the tests

Build the solution and execute the tests using:

```bash
dotnet build PaymentGateway.sln
dotnet test PaymentGateway.sln

# Run tests and collect coverage.
dotnet test PaymentGateway.sln --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

### Test implementation

The test suite uses:

- xUnit for the testing framework.
- FluentAssertions for assertions.
- Moq for mocking dependencies.
- `WebApplicationFactory` for HTTP integration testing.

Integration tests use a configurable fake bank HTTP handler. The gateway's actual HTTP client configuration, resilience pipeline, and exception handling remain active during these tests.

Automated tests do not require Docker or the Aspire Dashboard.

### Test coverage

The tests cover the following areas:

- Request validation, including missing and incorrectly formatted values.
- Payment service mapping and persistence.
- Repository insertion and retrieval.
- Duplicate payment ID rejection without overwriting existing records.
- Authorized and declined payment creation.
- Retrieving existing and non-existing payments.
- Bank failures and the prevention of payment persistence after a bank error.
- Repository save failures and verification that the bank is not called again.
- Bank unavailability and corresponding error responses.
- HTTP timeout and cancellation handling.
- Correlation ID propagation between the gateway and bank simulator.
- Circuit breaker behaviour and blocking subsequent bank requests.

The timeout integration test uses the configured 10-second timeout.

The coverage collector generates `coverage.cobertura.xml` inside a generated subdirectory under `TestResults`. The test output prints the report location.

Coverage results represent the executed test run. They are not an enforced coverage threshold or a guarantee of application correctness.

### Manual simulator scenarios

The supplied simulator determines the payment outcome based on the final digit of the card number.

Keep all other request fields valid and change only the last digit:

| Final digit | Simulator decision | Expected gateway response |
|---|---|---|
| `1`, `3`, `5`, `7`, `9` | Authorized | `201`, `Authorized`; GET returns the stored decision |
| `2`, `4`, `6`, `8` | Declined | `201`, `Declined`; GET returns the stored decision |
| `0` | HTTP 503 | `503` Problem Details; no payment record |

Validation can also be tested by leaving the card number empty, providing an expired card, or submitting `TRY` as the currency.

These requests should return `400 Bad Request` without contacting the bank.

Timeout and circuit breaker behaviour are verified through automated integration tests without modifying the supplied simulator configuration.

## Assumptions and Limitations

The following points describe the current scope and design decisions:

- **Challenge scope:** Authentication, merchant ownership checks, and production deployment hardening are outside the scope of this implementation.
- **Storage:** Payment records are kept in memory and are lost when the application restarts. Multiple gateway instances do not share the same repository.
- **Invalid requests:** Invalid input returns `400` rather than creating a `Rejected` payment record. Only completed bank decisions are persisted.
- **Amount and expiry:** Amounts must be greater than zero, and card expiry must be strictly later than the current UTC month.
- **Idempotency:** There is no idempotency contract. Repeating a POST request can result in another payment submission to the bank.
- **Uncertain bank outcomes:** A timeout does not necessarily mean the bank failed to process the payment. The gateway does not automatically retry, reconcile, or reverse such transactions.
- **Bank and repository consistency:** Bank authorization and local persistence are not part of a single atomic transaction. If repository persistence fails after authorization, the API returns a technical error without rolling back the bank decision.


## Challenge Reference

This implementation is based on the [Checkout.com payment gateway challenge](https://github.com/cko-recruitment) and its [.NET starter repository](https://github.com/cko-recruitment/payment-gateway-challenge-dotnet).

The starter allows restructuring the solution and using alternative test libraries. The supplied `imposters/` configuration and `.editorconfig` should remain unchanged.