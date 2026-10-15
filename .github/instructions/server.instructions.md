---
applyTo: "src/YHAB/**"
---

# Server and persistence

These contracts govern server code and related persistence, migration, and health
configuration in other projects through the repository-wide instructions. Keep the
repository-wide dependency boundaries and cancellation requirements in force. For
server Razor, CSS/JavaScript, or rendering setup, read the
[UI guide](ui.instructions.md). For Identity, read the
[account guide](account.instructions.md). Before running migrations or operating
the application, read the [Aspire guide](aspire.instructions.md). Setup and runtime
procedures remain in the [root README](../../README.md).

## Feature and database boundaries

- Keep direct database access in feature `Services`/`Data`, shared `YHAB.Data`,
  migrations, and `Program` registration/readiness wiring. Pages, components, and
  HTTP endpoint handlers call application services. Identity's `UserManager` and
  `SignInManager` APIs remain allowed; no repository abstraction is required.
- Access another server feature only through its `Contracts` namespace. Introduce
  contracts for actual consumers, keep them independent of feature implementations
  (including their own), and keep server-only contracts on the server. Common
  infrastructure and startup composition remain outside the feature boundary.
- Keep dependencies between top-level `YHAB.Features.<Feature>` namespaces acyclic,
  including contracts. Internal namespaces within one feature may depend on each
  other. The checks discover new features automatically; see
  [architecture boundaries](../../tests/README.md#architecture-boundaries).

## PostgreSQL, Identity storage, and readiness

- PostgreSQL resource `postgres` hosts `yhabdb` (physical database `yhab`).
  Aspire injects `ConnectionStrings:yhabdb`. Do not restore SQLite.
- Use `IDbContextFactory<ApplicationDbContext>` for independent Blazor operations
  and dispose created contexts. Preserve Identity schema version 3 and passkeys.
- `/health` includes database readiness; `/alive` checks process liveness
  independently of database availability. Preserve the bounded database
  readiness check and Aspire's monitoring of `/health`. Both endpoints are
  enabled in Development or through `HealthChecks:ExposeEndpoints`, which the Azure
  deployment sets explicitly. Keep readiness and liveness separate.
- Author migrations through the built-in `yhab-migrations` resource using the
  web project's actual startup model. Keep migrations and snapshot under
  `src/YHAB/Data/Migrations`, namespace `YHAB.Migrations`. Rebuild after
  generating migrations. The web resource must wait for successful migration
  completion; do not add application-startup migration code or a custom worker.
