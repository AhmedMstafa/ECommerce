# Outbox, background jobs, and HTTP resilience

## Why the outbox has a state machine

The original outbox only had `ProcessedOnUtc`, `RetryCount`, and no lease. Two Hangfire workers could read the same row and send the same email twice, and a missing handler was still marked as processed.

`OutboxMessage` now has an explicit lifecycle:

```text
Pending -> Processing -> Processed
    ^          |
    |          +-- retry/dead-letter -> Pending or Failed
    +-- lease expired
```

- `Pending`: eligible now, or after `NextAttemptAtUtc`.
- `Processing`: owned by one worker until `LeaseExpiresAtUtc`.
- `Processed`: terminal success; `ProcessedOnUtc` is set.
- `Failed`: terminal dead letter; nothing is deleted, so an operator can inspect or replay it.

`Status` is stored as text (`Pending`, `Processing`, `Processed`, `Failed`) so the table is easy to query during an incident.

## Atomic claim and leases

`EfCoreOutboxMessageStore.ClaimAsync` performs two steps:

1. Select candidate IDs in oldest-first order.
2. Issue one conditional `ExecuteUpdateAsync` that only claims rows still in their original state.

Two workers can select the same IDs, but only one conditional update can succeed. The winning worker writes a unique `LeaseOwner` and `LeaseExpiresAtUtc`. If a process dies mid-send, the lease expires and another worker can claim the row.

This is SQL-side concurrency control, not an in-process lock, so it is correct across multiple API instances.

## Idempotency

Every message gets a deterministic SHA-256 `IdempotencyKey`, either from `type + content` or from an explicit key supplied by the caller. Password-reset messages include the user ID and generated token in the explicit key; each new reset request is distinct, while retrying the same request is not.

A unique index on `IdempotencyKey` prevents duplicate rows. The email handler also passes the key to `IEmailService` as a normalized MIME `Message-Id`, giving receiving mail systems a second chance to suppress an ambiguous duplicate after a crash.

The migration backfills existing rows with `legacy:<Id>` keys and derives their state from the old columns. The original `InitaialMigration` was not modified, and no existing column is dropped by the new migration.

## Retry, timeout, and cancellation

- Transient failures return the message to `Pending` with exponential backoff (`5s → 10s → 20s → ...`, capped by config).
- After `Outbox:MaxAttempts`, the message becomes `Failed` instead of disappearing.
- Each handler gets a per-message timeout (`Outbox:HandlerTimeoutSeconds`), shorter than the lease.
- Hangfire shutdown and explicit cancellation are linked into the handler token. On shutdown the lease is released and the job stops cooperatively.
- `[DisableConcurrentExecution]` and `[AutomaticRetry]` protect against overlapping recurring-job invocations and infrastructure-level failures.

Configuration lives in `appsettings.json` under `Outbox` and `Hangfire`. Values are validated at startup; invalid ranges fail fast.

## HTTP resilience scope

The only real outbound HTTP dependency is Google ID-token validation, which fetches signing certificates through `Google.Apis.Auth`. The unused generic `"ExternalServices"` HttpClient was removed.

`GoogleResilientHttpClientFactory` adapts the SDK's `Google.Apis.Http.IHttpClientFactory` to the named `GoogleTokenValidation` HttpClient with `AddStandardResilienceHandler()`. This keeps resilience tied to an actual integration instead of adding configuration for a client nobody calls.

SMTP is not an HTTP integration; it uses MailKit directly and reports failures through `Result` so the outbox can retry it.

## Local verification

Docker was unavailable in the development environment, so the migration and claim path were also verified against a throwaway SQL Server database using integrated authentication. The checks applied all migrations, inserted two messages, confirmed a competing worker could not claim leased rows, confirmed an expired lease was reclaimable, and confirmed the unique idempotency index rejected a duplicate. The temporary database was removed afterward.
