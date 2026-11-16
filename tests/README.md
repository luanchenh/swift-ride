# SwiftRide automated tests

The test project covers the application-service layer of Trip Service,
Matching & Pricing Service, and Payment Service. Infrastructure dependencies
such as SQL Server, PostgreSQL, MongoDB, OSRM, and RabbitMQ are replaced with
mocks or deterministic HTTP handlers so the suite remains fast and repeatable.

## Run all tests

From the repository root:

```powershell
dotnet test
```

Run only the SwiftRide test project:

```powershell
dotnet test tests\SwiftRide.Tests\SwiftRide.Tests.csproj
```

## Generate coverage

```powershell
dotnet test tests\SwiftRide.Tests\SwiftRide.Tests.csproj `
  --collect:"XPlat Code Coverage" `
  --results-directory TestResults
```

The generated Cobertura XML file is placed under `TestResults`. This directory
is ignored by Git.

## Test organization

- `MatchingPricing`: pricing strategies, quote calculation, driver matching,
  driver locations, and OSRM HTTP behavior.
- `Trip`: authentication, registration, password changes, JWT generation,
  trip creation, password hashing, and real-time driver notifications.
- `Payment`: payment mapping and SePay IPN processing, including idempotency,
  invalid transfers, refunds, and settlement events.
- `Support`: deterministic test doubles shared by multiple modules.
