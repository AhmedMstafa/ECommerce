# Integration testing

The `ECommerce.IntegrationTests` project exercises the real ASP.NET Core host,
real Identity, real EF Core mappings, real SQL Server, and the committed
migrations. It deliberately does **not** use EF Core InMemory: InMemory cannot
prove that a LINQ query translates to SQL, that a migration applies, or that a
unique index behaves correctly.

## Test database lifecycle

`SqlServerTestDatabase` picks a provider in this order:

1. `ECOMMERCE_TEST_SQL_CONNECTION`, if set, must point at a SQL Server `master`
   database. The suite creates a uniquely named throwaway database there.
2. Testcontainers starts `mcr.microsoft.com/mssql/server:2022-latest` when a
   Docker daemon is available.
3. If Docker is unavailable, the suite falls back to a local SQL Server at
   `Server=.` using integrated authentication.

The fixture creates the database, `WebApplicationFactory` starts the API,
`Database.MigrateAsync()` applies all migrations, and the fixture seeds one
brand/type, three products, an `Admin` role, and an admin user. After the run,
connections are cleared, the database is set to `SINGLE_USER WITH ROLLBACK
IMMEDIATE`, and it is dropped. No development or production database is ever
migrated or seeded by the test suite.

## Why environment variables are used in the factory

`Program.cs` calls `AddPresentation` and `AddInfrastructure` with
`builder.Configuration` before `WebApplicationFactory` can apply
`ConfigureAppConfiguration` callbacks. Those extension methods copy values such
as `ConnectionStrings:DefaultConnection`, `Jwt:SecretKey`, and rate-limit
settings into locals during registration. The factory therefore sets the
equivalent `ConnectionStrings__DefaultConnection`-style environment variables
**before** the host is created and restores the previous values on disposal.
This ensures the API really uses the throwaway database.

## Isolated external effects

- `IEmailService` is replaced with a recorder so no SMTP is contacted.
- `IGoogleTokenValidator` is replaced with a deterministic stub for valid,
  mismatched, and invalid tokens.
- The Hangfire `IHostedService` is removed from the test host, so no background
  worker processes the outbox or touches the test database.
- Swagger is explicitly enabled and rate limits are raised so tests are stable.

The collection fixture shares one database and host across all integration
tests; xUnit runs that collection serially.

## What is covered

- Paged product response envelope and metadata (`pageIndex`, `pageSize`,
  `totalCount`, `totalPages`).
- `400` validation problems for invalid page/sort values and `404` for missing
  products.
- `401` for anonymous product creation and `201` plus persistence for an
  authenticated admin.
- Register/login with a strong password and rejection of weak passwords.
- Password reset queues an outbox row without sending email directly; unknown
  accounts return the same generic response and queue nothing.
- Google login rejects invalid tokens, creates a new user for a verified token,
  refuses to auto-link an existing email owner, and explicit linking rejects
  mismatched emails.
- Liveness/readiness health responses and the versioned Swagger document.

## Running

With Docker available, no extra setup is needed:

```powershell
$env:Jwt__SecretKey='local-development-only-key-change-me-0123456789'
dotnet test ECommerce.slnx --nologo -v q
```

With a local SQL Server and no Docker:

```powershell
$env:Jwt__SecretKey='local-development-only-key-change-me-0123456789'
dotnet test ECommerce.IntegrationTests/ECommerce.IntegrationTests.csproj --nologo
```

To point at a specific SQL Server explicitly:

```powershell
$env:ECOMMERCE_TEST_SQL_CONNECTION='Server=localhost,1433;Database=master;User Id=sa;Password=<from-secret-store>;TrustServerCertificate=True'
dotnet test ECommerce.IntegrationTests/ECommerce.IntegrationTests.csproj --nologo
```

Never commit that connection string. Use a secret store or an environment
variable supplied by the CI/CD system.
