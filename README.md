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
- `Redis__Configuration` — **required**; the Upstash Redis connection string in
  `rediss://default:PASSWORD@host:6379` form. The `rediss://` scheme enables TLS.
  Startup fails fast with a clear message if this is missing or still a
  placeholder; the value is never printed. See
  [docs/caching-pagination-validation.md](docs/caching-pagination-validation.md).
- `Redis__InstanceName` — optional; key prefix, defaults to `ECommerce:`.
- `GoogleAuth__ClientId` — optional; only a Web Client ID is needed, no client
  secret. Without it, Google endpoints return a clear "not configured" error.
- `EmailSettings__*` and `ClientApp__BaseUrl` — optional; needed only for
  password-reset emails. `ClientApp__BaseUrl` is used to build the reset link.
- `SeedAdmin__Email` / `SeedAdmin__Password` — optional; applied only when
  `ASPNETCORE_ENVIRONMENT=Development`.
- `SeedDemoUsers__Enabled` / `SeedDemoUsers__Password` — optional; Development-only
  demo customers and addresses. All demo users share the configured password.
  Without a password, existing users still get their demo addresses.
- `ECOMMERCE_TEST_SQL_CONNECTION` — optional; SQL Server `master` connection for
  integration tests.

Variable names map to standard configuration keys by replacing `:` with `__`,
for example `Jwt__SecretKey` → `Jwt:SecretKey`. The application code keeps using
the original key names.

The `.env` file is found by walking up from the current directory, so it works
when running from the repository root, from `ECommerce.API`, and from test
output folders. A missing `.env` file is not an error; injection via environment
variables still works.

## Development seed data

`DatabaseSeeder` runs only in `Development`, after `MigrateAsync()`. It is
idempotent: existing rows are never overwritten.

| Seeder | Data | Source |
| --- | --- | --- |
| `IdentitySeeder` | `Admin` role and optional admin account | `SeedAdmin__*` |
| `ProductTypeSeeder` | 5 types (Electronics, Apparel, Home & Kitchen, Books, Sports & Outdoors) | `src/ECommerce.Infrastructure/Seeding/Data/types.json` |
| `ProductBrandSeeder` | 5 brands (Nike, Adidas, Apple, Samsung, Sony) | `.../brands.json` |
| `DeliveryMethodSeeder` | Standard / Express / Overnight | `.../delivery-methods.json` |
| `ProductSeeder` | 11 demo products with stock | `.../products.json` |
| `DemoUserDataSeeder` | Two demo customers and three addresses | `.../demo-users.json` + `SeedDemoUsers__Password` |

Products resolve brands and types by name, so they also work when those tables
already contain operator-created rows. Demo logins are `demo.customer@example.com`
and `demo.shopper@example.com`; both use `SeedDemoUsers__Password`. Set
`SeedDemoUsers__Enabled=false` to skip demo users and addresses. Production is
never seeded automatically.

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
dotnet ef migrations add <Name> --project src/ECommerce.Infrastructure --startup-project src/ECommerce.API --output-dir Migrations
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
| `Redis:Configuration` | `Redis__Configuration` |
| `Redis:InstanceName` | `Redis__InstanceName` |
| `GoogleAuth:ClientId` | `GoogleAuth__ClientId` |
| `EmailSettings:Password` | `EmailSettings__Password` |
| `ClientApp:BaseUrl` | `ClientApp__BaseUrl` |
| `Swagger:Enabled` | `Swagger__Enabled` |
| `Cors:AllowedOrigins:0..n` | `Cors__AllowedOrigins__0..n` |

Missing `Jwt:SecretKey`, `ConnectionStrings:DefaultConnection`, or
`Redis:Configuration` produces a clear startup error that names the key but
never prints a value. Redis connection failures at runtime are logged as
warnings and degrade to SQL Server; they do not fail product queries.

Swagger UI is enabled in Production by default and served at
`/swagger/index.html`; the JSON document is at `/swagger/v1/swagger.json`.
Set `Swagger__Enabled=false` if you need to disable it. The app trusts Railway's
forwarded proto/prefix headers so the UI works behind HTTPS termination.

## Frontend checkout (Vercel)

A standalone React + Vite + TypeScript checkout lives in [`frontend/`](frontend/README.md).
It uses Stripe.js/Elements with the PaymentIntent `clientSecret` created by this
API; it never creates a PaymentIntent or Checkout Session and never calls the
webhook endpoint. See `frontend/README.md` for env vars, the demo login, the
`4242 4242 4242 4242` test card, and Vercel deployment notes.

CORS needs no code change: add each browser origin to the API environment as
indexed variables (the policy uses `AllowCredentials`, so wildcards are not
allowed):

```text
Cors__AllowedOrigins__0=https://localhost:5001
Cors__AllowedOrigins__1=http://localhost:5173
Cors__AllowedOrigins__2=https://<your-project>.vercel.app
```

## Design notes

- [docs/api-versioning.md](docs/api-versioning.md) — URL versioning and the
  real Swagger security document.
- [docs/outbox-and-resilience.md](docs/outbox-and-resilience.md) — outbox state
  machine, leases, exponential backoff, idempotency, and scoped resilience.
- [docs/caching-pagination-validation.md](docs/caching-pagination-validation.md)
  — Upstash Redis setup, distributed cache with single-flight reads,
  generation-based invalidation, explicit page validation, and the
  `PagedResponse<T>` value object.
- [docs/integration-testing.md](docs/integration-testing.md) — real SQL Server
  test lifecycle and why configuration is injected through environment
  variables.

Selected operational decisions:

- Hangfire's SQL Server storage is retained, but its dashboard requires an
  authenticated `Admin` in every environment.
- `IMemoryCache` is fully removed; product, brand, and type reads are cached in
  Upstash Redis through `IDistributedCache`.
- A Google identity is never auto-linked merely because its verified email
  matches an existing account. Linking is explicit and authenticated.
- Strong password rules apply to new registrations and password changes;
  existing users are not forced to change passwords.
- Password reset emails are HTML templates with encoded dynamic values and are
  committed to the outbox; SMTP failures remain retryable.
- Runtime health probes are `/health/live` and `/health/ready`. `/health/live`
  intentionally ignores external dependencies. `/health/ready` includes SQL
  Server and Hangfire recurring-job registration; if Hangfire storage is
  temporarily unavailable at startup, the API still starts and serves liveness
  while a hosted service retries recurring-job registration with exponential
  backoff.

## Docker (API only)

The Docker setup containerizes **only** the ASP.NET Core API (`src/ECommerce.API`). SQL Server, Redis, and Hangfire remain external dependencies/services as configured by the application.

### Prerequisites

- Docker Desktop / Docker Engine with Linux container support.
- Reachable external SQL Server and Redis instances.
- Required application configuration values (no secrets baked into the image).

### Build

Run from the repository root (next to `ECommerce.slnx`):

```powershell
docker build -t ecommerce-api:local -f Dockerfile .
```

### Run

The container listens on HTTP port `8080`. Provide required configuration through environment variables (or an env-file kept outside the image).

```powershell
docker run -d --name ecommerce-api `
  -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e "ConnectionStrings__DefaultConnection=<sql-connection-string>" `
  -e "Jwt__SecretKey=<long-random-secret>" `
  -e "Redis__Configuration=<redis-connection-string>" `
  -e "Stripe__SecretKey=<stripe-secret-key>" `
  -e "Stripe__WebhookSecret=<stripe-webhook-secret>" `
  ecommerce-api:local
```

Optional env-file approach (do **not** copy `.env` into the image):

```powershell
docker run -d --name ecommerce-api --env-file ./.env -p 8080:8080 ecommerce-api:local
```

### Required / important environment variables

| Variable | Required | Notes |
|---|---:|---|
| `ConnectionStrings__DefaultConnection` | Yes | SQL Server connection string. Startup validation fails if missing. |
| `Jwt__SecretKey` | Yes | JWT signing key; startup validation fails if missing. |
| `Redis__Configuration` | Yes | Redis/Upstash connection string; startup validation fails if missing/placeholder. |
| `Stripe__SecretKey` | Yes* | Required outside `Testing`/design-time. |
| `Stripe__WebhookSecret` | Yes* | Required outside `Testing`/design-time. |
| `ASPNETCORE_ENVIRONMENT` | Recommended | Dockerfile defaults to `Production`. |
| `ASPNETCORE_HTTP_PORTS` | Optional | Dockerfile sets `8080`. |
| `Swagger__Enabled` | Optional | Production defaults to `true`; set `false` to disable public Swagger. |

\*Stripe keys are validated by application startup outside Testing/design-time.

### External SQL Server / Redis from a Linux container

Inside a container, `localhost` refers to the container itself, not your host machine. If SQL Server or Redis run on the Docker host (Windows), configure the container to reach the host service:

- **Docker Desktop (Windows/macOS):** `host.docker.internal` is commonly used to reach host-published ports.
- **Remote/cloud SQL/Redis:** use the actual server hostname/IP and ensure network/firewall access.

Example shape only (replace placeholders; do not commit secrets):

```text
ConnectionStrings__DefaultConnection=Server=host.docker.internal,1433;Database=ECommerceDb;User Id=<user>;Password=<password>;TrustServerCertificate=True;MultipleActiveResultSets=True
Redis__Configuration=<redis-host>:6379,abortConnect=false
```

Verify the real endpoint, port, TLS, and credentials for your environment before use.

### Access, logs, and cleanup

```powershell
# Liveness probe
Invoke-WebRequest http://localhost:8080/health/live -UseBasicParsing

# Logs
docker logs ecommerce-api

# Stop and remove test container
docker rm -f ecommerce-api
```
