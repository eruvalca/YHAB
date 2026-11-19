# Local development with Aspire

Aspire is the default entry point for running and debugging YHAB. It starts
PostgreSQL, applies migrations, supplies configuration, and starts the web
project. The hosted WebAssembly client runs through that web project.

Prerequisites, the first run, and the shorter run commands are in the
[root README](../README.md#run-it). This guide owns the deeper lifecycle,
debugging, database, and agent-tooling procedures.

## Run through Aspire

For interactive development, run from the repository root:

```powershell
aspire run
```

The CLI builds the AppHost and its projects before starting them. For a background
run (including agent validation), use:

```powershell
aspire start --launch-profile https --non-interactive
aspire wait yhab --timeout 120 --non-interactive
aspire describe --non-interactive
```

`aspire start` prints the dashboard login URL. The dashboard and `aspire describe`
show the current application endpoints. Open the HTTPS endpoint for
`yhab.dev.localhost`; all ports are allocated dynamically and can change on
restart. Chromium browsers resolve `.localhost` names locally. Command-line
clients that do not resolve subdomains can use the internal `https://localhost`
endpoint shown by Aspire. Do not copy ports or dashboard login tokens into source.

The background command explicitly selects `https`; it is also the AppHost's first
and default launch profile. `AddDotnetProject` selects the
matching web profile and derives its endpoints from `applicationUrl`; there is no
AppHost override for the web profile or its ports. The web profiles retain
`yhab.dev.localhost:0`: the hostname is our local convention, and port `0` asks
Aspire to allocate an available port instead of using the template's fixed ports.
The other web launch settings preserve the template's environment and Blazor debugging.

The AppHost's `https` profile omits dashboard, OTLP, and resource-service addresses:
Aspire 17 supplies secure, dynamically allocated local endpoints. The optional
AppHost `http` profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`, which opts into
HTTP endpoints with allocated ports. This opt-in applies only when that profile
is selected; the default `https` profile keeps Aspire's secure defaults.

Use the web resource's **Rebuild** command to apply compiled application changes.
`AddDotnetProject` supports coordinated initial builds, but Aspire 17 does not yet
provide the legacy service/AppHost watch workflow for this resource model; do not
replace the Rebuild procedure with `aspire run --watch`.
For AppHost changes or a full solution build, stop first to release Windows file locks:

```powershell
aspire stop --non-interactive
dotnet build YHAB.slnx
aspire start --launch-profile https --non-interactive
```

Stop the application with `aspire stop --non-interactive` (or Ctrl+C for a foreground
run). PostgreSQL and any started pgAdmin container stop with Aspire. The database
volume survives. Aspire retains its generated local PostgreSQL password in AppHost
user secrets; preserve those secrets along with the volume when reusing local data.
No database password or connection string needs to be committed.

## Application identity and local data

If this solution was generated, `.template-provenance.json` records its template
version and source commit. It is an independent snapshot: template updates do not
update this application. Review SDK/package/skill updates in this repository. Git
initialization is optional and separate.

Use a unique application name for projects you run side by side. Different ports
do not isolate browser cookies on the same hostname. A generated secrets ID is
unique even when the application name is reused, but the development hostname is
name-based. Aspire's default data-volume name depends on the AppHost path; moving
or renaming a checkout can select a different volume. Keep the original secrets
and volume together if preserving development data.

## Resource graph and database

```text
postgres (PostgreSQL 18.6, managed data volume)
  ├─ yhabdb (physical database: yhab)
  │    └─ yhab-migrations (EF database update)
  │         └─ yhab (Blazor server + hosted WebAssembly)
  └─ pgadmin (explicit start)
```

After a normal startup, these dashboard states are expected:

| Resource | Expected state | Meaning |
| --- | --- | --- |
| `postgres`, `yhabdb` | Running / Healthy | PostgreSQL and the application database are ready. |
| `yhab-migrations` | Finished | The one-shot migration command completed successfully. It is not a long-running service. |
| `yhab` | Running / Healthy | The web application is ready and its database readiness check passes. |
| `pgadmin` | Not started | Optional database UI; start it when needed. |

With hidden resources displayed, `yhab-rebuilder` can also be **Not started**
until the web project's Rebuild command is used, and the EF tool resource can be
**Finished**. These helper states do not indicate an application startup failure.
An **Exited**, **Failed**, or **Unhealthy** application/database resource, or web
startup blocked by failed migrations, does require investigation through its logs.

The PostgreSQL image version is the built-in default of
`Aspire.Hosting.PostgreSQL` 17.0.0: PostgreSQL 18.6 with pgAdmin 9.19.0.
Container lifetime remains Aspire's default
session lifetime. Data uses an Aspire-managed named volume rather than a repository
file. This is a fresh PostgreSQL schema; the original SQLite scaffold is removed.

Aspire supplies `ConnectionStrings:yhabdb` to both the application and migration
tool. The application uses Npgsql EF Core 10.0.3, EF Core 10.0.14, and Aspire's
Azure Npgsql EF integration 13.6.5 (password authentication locally, managed identity
in Azure). Its non-pooled `IDbContextFactory<ApplicationDbContext>`
supports a context per Blazor operation; Identity can still resolve the scoped
context. Dispose factory-created contexts with `await using`.

## Database inspection and pgAdmin

For quick SQL inspection, select **REPL** on the running `postgres` resource. The
dashboard opens the container's bundled `psql` with its existing credentials, so
no local client or pgAdmin startup is required. Run `\connect yhab` to switch
from the initial `postgres` database to the application database. Exit with `\q`
before closing the tab; closing the viewer alone can leave the client running.
This shell has normal write permissions, not read-only access. Database reset or
drop still requires explicit intent to delete local data. `WithRepl()` uses our
existing PostgreSQL package without an experimental-warning suppression, while
the dashboard terminal infrastructure remains preview. See
[PostgreSQL REPLs](https://aspire.dev/integrations/databases/postgres/postgres-host/#open-an-interactive-repl).

pgAdmin is available only in run mode. Start `pgadmin` from its dashboard action or:

```powershell
aspire resource pgadmin start --non-interactive
aspire wait pgadmin --timeout 120 --non-interactive
```

Open its generated HTTP endpoint, expand **Servers → postgres → Databases → yhab**.
Aspire configures the connection and credentials. Stop it from the dashboard when done.

## Debug through Aspire

In VS Code, install the recommended Aspire and C# extensions and open the repository
root. Select **Aspire: YHAB** in Run and Debug, then press **F5**. The checked-in
`.vscode/launch.json` uses the Aspire debugger and discovers the AppHost through
`aspire.config.json`; no fixed ports or connection strings are needed. The extension
starts the resource graph and attaches the .NET debugger to the web project.
Use **Ctrl+F5** with the same configuration to run without debugging.

Stop any CLI-started instance with `aspire stop --non-interactive` before starting
an IDE debug session. Use the IDE's **Stop Debugging** action to end its session.
Open the dashboard through **Aspire: Open Dashboard** or the Aspire view; editor
settings control whether it opens automatically. This workspace sets
`aspire.dashboardBrowser` to `openExternalBrowser`, so the dashboard opens in the
configured external browser for both F5 and Ctrl+F5. This overrides a user-level
`debugEdge`/`debugChrome` preference; C# application debugging remains enabled with F5.

If VS Code reports **Unable to launch browser: "Unable to attach to browser"**,
check **Output → Aspire Extension**. An entry such as **Failed to start debug
browser (pwa-msedge), falling back to default browser** identifies the dashboard's
browser-debugger session. That session is separate from the AppHost and web C#
debuggers and is not needed to use the dashboard. Keep the workspace's
`openExternalBrowser` setting and remove any `dashboardBrowser: "debugEdge"` or
`"debugChrome"` override in the selected `launch.json` configuration, which takes
precedence over workspace settings. Then stop and restart the Aspire session.

Set server breakpoints in the web project's code-behind, such as the registration
handler in `src/YHAB/Features/Account/Pages/Register.razor.cs`, then exercise the
page through the HTTPS endpoint shown by Aspire. Inspect variables and the call
stack in the debugger, and use Aspire's logs and traces for the corresponding
HTTP and database activity. A breakpoint can temporarily interrupt health checks;
resume execution before diagnosing the paused resource as unhealthy.

In Visual Studio, set **YHAB.AppHost** as the startup project, select its
**https** profile, and use F5 (debug) or Ctrl+F5 (run). Launching `YHAB` or
`YHAB.Client` alone does not start the database/migration dependency graph.

The existing unit and component tests remain headless and can run/debug through
Test Explorer without Aspire.

See the [Aspire VS Code extension guide](https://aspire.dev/get-started/aspire-vscode-extension/).

## EF migrations

`Aspire.Hosting.EntityFrameworkCore` **17.0.0-preview.1.26567.5** manages
`yhab-migrations` and its **dotnet-ef 10.0.14** tool. It does not alter the machine's
global EF tool. On every start, migrations wait for PostgreSQL and the database;
the web application starts only after migration success. Failure blocks web startup.
Migrations use the actual web startup registration, keeping Identity's design-time
and runtime models aligned.

For a schema change:

1. Stop Aspire, change the model, and build the solution.
2. Start Aspire. If EF detects pending model changes, migration startup can fail and
   block the web resource; the migration authoring commands remain available.
3. On `yhab-migrations` in the dashboard, run **Add Migration...** and supply a
   descriptive name. Review the generated files in `src/YHAB/Data/Migrations`
   with namespace `YHAB.Migrations`.
4. Apply the repository's conventions to the handwritten migration class (file-scoped
   namespace and `internal sealed partial class`); leave generated designer code alone.
   Keep the model snapshot in the same migrations directory. EF can choose a directory
   from the legacy namespace when regenerating a missing snapshot, so check its location.
5. Stop Aspire, rebuild, and start again. The built-in migration resource applies the
   newly compiled migration before starting the web application.
6. Run **Get Database Status** on `yhab-migrations` to verify applied migrations and
   pending model changes.

CLI equivalents for inspecting and updating the current compiled model:

```powershell
aspire resource yhab-migrations ef-database-status --non-interactive
aspire resource yhab-migrations ef-database-update --non-interactive
aspire logs yhab-migrations --non-interactive
```

The built-in **Remove Migration**, **Drop Database**, and **Reset Database** commands
are also available. Drop/reset delete local application data; use them only when
that is intended. There is no application-startup migration routine or custom worker.
See [Aspire's EF migration integration](https://aspire.dev/integrations/databases/efcore/migrations/).

## Recurring budget worker

The web host posts due recurring budget entries in bounded background batches
while it is running. Set `Budgeting:AutomaticPosting` to `false` (environment
variable `Budgeting__AutomaticPosting=false`) to disable that worker for a run.
Interactive catch-up remains available. `Budgeting:TimeZone` determines the
budget's server date and defaults to `America/Chicago`. See the
[budgeting guide](budgeting.md#recurrence-and-history) for scheduling, retry, and
undo behavior.

## Logs and traces in the local dashboard

The dashboard exposes server logs, request traces, Npgsql database spans, and metrics.
Metrics include the .NET 11 `Microsoft.Extensions.Caching.Memory` meter, which
`BudgetMonthCache` enables through `TrackStatistics`; it reports cache requests with
a hit/miss tag, evictions, entries, and estimated size, and stays silent until a
listener such as the OpenTelemetry pipeline is attached.
Useful CLI commands include `aspire logs yhab`, `aspire otel traces`, and
`aspire otel logs`. The Aspire MCP server provides the same resource and telemetry
visibility to agents. Database query telemetry can contain application information;
keep exports and runtime logs out of Git.

Aspire 17 retains dashboard run history automatically in **Run** persistence
mode. Use the header's run selector to compare the live run with completed runs;
historical runs are read-only. Up to ten unpinned runs are retained per application,
and pinning keeps a useful run. Console logs are only persisted after their stream
is viewed or exported, so capture needed console output before stopping.

Keep the default dashboard data directory, `<ASPIRE_HOME>/dashboard` (normally
`~/.aspire/dashboard`), outside the checkout. Persisted resource snapshots can
contain unredacted credentials even when the dashboard masks them. On Windows,
the directory inherits filesystem permissions; keep it private and do not share
its databases or backups. The existing Git exclusions cover `.aspire/`, `*.db`,
`*.db-wal`, and `*.db-shm`. No application telemetry changes are needed for run
history. See [dashboard persistence](https://aspire.dev/dashboard/data-persistence/).

Health, readiness, and Application Insights configuration are in the
[operations guide](operations.md#health-and-telemetry).

## Agent tooling and MCP

Repository instructions start in
[`.github/copilot-instructions.md`](../.github/copilot-instructions.md). It keeps
shared rules and names the path-specific files per scope. Follow those routes even
when behavior spans directories or the operation edits no file in the owning
directory, such as running Aspire or validating a source change.

Each path-specific file in [`.github/instructions`](../.github/instructions) carries
an `applyTo` glob, so Copilot loads it for matching files whatever the session
working directory. An operation that touches no matching file therefore loads no
path-specific rules, which is why the repository-wide file names each one. A
path-specific file owns its scoped rules or routes the reader to the owning guide;
none may repeat a rule stated elsewhere, because Copilot merges instruction sources
without a precedence rule and duplicated or conflicting text is unsafe. See
[instruction sources and scope](../build/agent-instructions.md#instruction-sources-and-scope).

Repository-owned skills that encode YHAB-specific workflows remain in
`.agents/skills`. Aspire workflow skills are supplied by the first-party
`aspire@aspire-skills` plugin configured in
[`.github/copilot/settings.json`](../.github/copilot/settings.json). Copilot CLI
and the Copilot app load the configured plugin rather than copied skill folders.
Do not retain or regenerate static copies of the Aspire skill bundle: they drift
from the installed Aspire CLI and package APIs. The AppHost now uses the stable
path-based `AddDotnetProject` model.

The user-level Copilot MCP entry runs **`aspire agent mcp`**. Keep a single entry.
No repository MCP duplicate or VS Code agent configuration is required. On a new
machine, install Aspire on `PATH`, then:

- Copilot CLI: use `/mcp add` to add a local/stdio server named `aspire`, command
  `aspire`, arguments `agent mcp`, at user scope. Its configuration is stored in
  `~/.copilot/mcp-config.json` and is inherited by Copilot desktop.
- Restart/reload the agent after configuration. Start the application from this
  repository and use the Aspire MCP resource-list tool to verify the connection.
  If several AppHosts are running, select this repository's AppHost explicitly.

The checked-in repository-owned skills require no additional installation on clone.
Keep each directory's `SKILL.md` name aligned with its directory and its activation
description concise. `copilot skill list` is the authority on what actually loads:
it reports each rejected `SKILL.md` while still exiting 0.

Refresh the Aspire plugin configuration with the matching CLI:

```powershell
aspire agent init --agents copilot --skills aspire --scope project --non-interactive
```

Review the resulting `.github/copilot/settings.json` change. If older CLI versions
copied Aspire skill folders back into a clone, remove only those plugin-owned copies
with `--remove-skill-copies`; preserve YHAB-authored skills such as
`instruction-hygiene`. Keep user secrets, dashboard tokens, runtime `.aspire`
state, telemetry exports, and temporary browser output out of source control.

The repository-authored
[instruction hygiene skill](../.agents/skills/instruction-hygiene/SKILL.md) is
written to apply to any repository; its references cover instruction source
locations and scope, a section-by-section checklist, and anti-patterns.

The [Fluent UI Blazor v5 usage skill](../.agents/skills/fluentui-blazor-usage/SKILL.md)
includes setup, data-grid, and theming references. The library is configured as
described in the [frontend guide](frontend.md); installing the skill alone does not
configure an MCP server. When using its examples, follow the
[UI instructions](../.github/instructions/ui.instructions.md) for code-behind and CSS,
the [account instructions](../.github/instructions/account.instructions.md) for static
account rendering, and the
[repository-wide instructions](../.github/copilot-instructions.md) for central package
versions. Verify version-sensitive APIs against the Fluent UI Blazor MCP server's
documentation and the released package source. The v5.0.1 package's assembly file
version is `5.0.1.26287`. The Fluent MCP server's version checker compares the
assembly version against the NuGet version and reports a false mismatch; the
four-part file version is not the NuGet package version. Some skill examples
still use prerelease APIs, so verify component names and parameters against the
installed package instead of copying them verbatim.

References: [Aspire MCP](https://aspire.dev/reference/cli/commands/aspire-agent-mcp/),
[Copilot CLI skills](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills),
[Copilot CLI instructions](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-custom-instructions),
[Copilot hooks](https://docs.github.com/en/copilot/reference/hooks-reference),
[Copilot desktop](https://docs.github.com/en/copilot/how-tos/github-copilot-app/customize-github-copilot-app).
