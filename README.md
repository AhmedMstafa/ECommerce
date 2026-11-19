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

Secrets live in a root `.env` file, which is gitignored. `Program.cs` loads it
before building the configuration, using `DotNetEnv`. If the same variable
already exists in the process environment, that value wins — this is how CI
and the integration tests inject their own settings.

```powershell
Copy-Item .env.example .env
```

Then edit `.env`:

- `Jwt__SecretKey` — **required**; generate a long random value (at least 32
  bytes). The API refuses to start without it.
- `ConnectionStrings__DefaultConnection` — **required**; a local SQL Server with
  integrated authentication is the default. Use `User Id` / `Password` there if
  your server requires SQL authentication.
- `GoogleAuth__ClientId` — optional; only a Web Client ID is needed, no client
  secret. Without it, Google endpoints return a clear "not configured" error.
- `EmailSettings__*` and `ClientApp__BaseUrl` — optional; needed only for
  password-reset emails. `ClientApp__BaseUrl` is used to build the reset link.
- `SeedAdmin__Email` / `SeedAdmin__Password` — optional; applied only when
  `ASPNETCORE_ENVIRONMENT=Development`.
- `ECOMMERCE_TEST_SQL_CONNECTION` — optional; SQL Server `master` connection for
  integration tests.

Variable names map to standard configuration keys by replacing `:` with `__`,
for example `Jwt__SecretKey` → `Jwt:SecretKey`. The application code keeps using
the original key names.

The `.env` file is found by walking up from the current directory, so it works
when running from the repository root, from `ECommerce.API`, and from test
output folders. A missing `.env` file is not an error; injection via environment
variables still works.

## Build and test

```powershell
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
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
dotnet ef migrations add <Name> --project ECommerce.Infrastructure --startup-project ECommerce.API --output-dir Migrations
```

The design-time host loads the same root `.env`, so no manual secret injection
is needed. Do not regenerate or edit the initial migration.

## Production configuration

Do not deploy `.env`. Inject the same names as environment variables through
your secret store (Kubernetes Secrets, Azure App Configuration, CI variables,
etc.):

| Configuration key | Environment variable |
| --- | --- |
| `Jwt:SecretKey` | `Jwt__SecretKey` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `GoogleAuth:ClientId` | `GoogleAuth__ClientId` |
| `EmailSettings:Password` | `EmailSettings__Password` |
| `ClientApp:BaseUrl` | `ClientApp__BaseUrl` |

Missing `Jwt:SecretKey` or `ConnectionStrings:DefaultConnection` produces a
clear startup error that names the key but never prints a value.

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
