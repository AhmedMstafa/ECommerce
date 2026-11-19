# ECommerce API

ASP.NET Core 10 Clean Architecture Web API with products, JWT/Identity auth,
Google account linking, health checks, API versioning, Swagger, caching, a
transactional outbox, and Hangfire processing.

Payment functionality is intentionally out of scope.

## Projects

| Project | Responsibility |
| --- | --- |
| `ECommerce.Domain` | Entities, value rules, errors, repository abstractions. No framework dependencies. |
| `ECommerce.UseCases` | MediatR commands/queries, FluentValidation, application contracts, pagination value object. |
| `ECommerce.Infrastructure` | EF Core/SQL Server, Identity, JWT, email/SMTP, caching, outbox, Hangfire, seeding. |
| `ECommerce.API` | Controllers, versioning, Swagger, rate limiting, health checks, middleware, composition root. |
| `ECommerce.UnitTests` | Fast tests for rules, handlers, caching, validation, outbox state machine. |
| `ECommerce.ArchitectureTests` | Enforces dependency direction and specification behavior. |
| `ECommerce.IntegrationTests` | Real host + real SQL Server integration tests. See [docs/integration-testing.md](docs/integration-testing.md). |

Dependencies point inward: `API → UseCases → Domain`, and
`Infrastructure → UseCases/Domain`. The API composes Infrastructure at startup;
Domain never references outer layers.

## Required local configuration

Secrets are not committed. Use user-secrets for local development and
environment variables in CI/production.

```powershell
dotnet user-secrets set "Jwt:SecretKey" "<a-long-random-secret>" --project ECommerce.API
```

Useful environment variables:

- `Jwt__SecretKey` — required for the API to start.
- `ECOMMERCE_TEST_SQL_CONNECTION` — optional SQL Server `master` connection for
  integration tests.
- `SeedAdmin__Email` / `SeedAdmin__Password` — optional administrator seeding
  in Development.
- `GoogleAuth__ClientId`, `EmailSettings__*` — supplied externally when those
  integrations are used.

The committed `ConnectionStrings:DefaultConnection` expects a local SQL Server
with integrated authentication. Change it through configuration, not source.

## Build and test

```powershell
$env:Jwt__SecretKey='local-development-only-key-change-me-0123456789'
dotnet build ECommerce.slnx
dotnet test ECommerce.slnx --nologo -v q
```

The integration suite applies all migrations to a throwaway SQL Server database
and drops it afterward. Docker is preferred; a documented local SQL Server
fallback is used when Docker is unavailable. EF InMemory is never used.

## Database migrations

The original `20261108235836_InitaialMigration` is preserved. Later changes are
additive migrations only:

- `20261119043630_StabilizeSchema`
- `20261119050055_AddOutboxStateMachine`

Create new migrations with:

```powershell
$env:Jwt__SecretKey='<local-secret>'
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
dotnet ef migrations add <Name> --project ECommerce.Infrastructure --startup-project ECommerce.API --output-dir Migrations
```

Do not regenerate or edit the initial migration.

## Design notes

- [docs/api-versioning.md](docs/api-versioning.md) — URL versioning and the
  real Swagger security document.
- [docs/outbox-and-resilience.md](docs/outbox-and-resilience.md) — outbox state
  machine, leases, exponential backoff, idempotency, and scoped resilience.
- [docs/caching-pagination-validation.md](docs/caching-pagination-validation.md)
  — single-flight memory cache, generation-aware invalidation, explicit page
  validation, and the `PagedResponse<T>` value object.
- [docs/integration-testing.md](docs/integration-testing.md) — real SQL Server
  test lifecycle and why configuration is injected through environment
  variables.

Selected operational decisions:

- Hangfire's SQL Server storage is retained, but its dashboard requires an
  authenticated `Admin` in every environment.
- `IMemoryCache` is retained; there is no Redis dependency.
- A Google identity is never auto-linked merely because its verified email
  matches an existing account. Linking is explicit and authenticated.
- Strong password rules apply to new registrations and password changes;
  existing users are not forced to change passwords.
- Password reset emails are HTML templates with encoded dynamic values and are
  committed to the outbox; SMTP failures remain retryable.
- Runtime health probes are `/health/live` and `/health/ready`.
