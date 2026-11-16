# YHAB

YHAB ("You Have A Budget") is a manual envelope-budgeting web application. You
add the money you actually have, assign it to categories, and record spending
yourself. Bank connections, transaction imports, photos, and one-off scheduled
entries are intentionally outside the application.

Each user owns private USD plans that can be shared with other YHAB accounts.
Plans hold manual accounts and transactions, monthly assignments, targets,
credit-card payment reserves, recurring entries, undo/redo, and reports. YHAB is
an independent implementation of the envelope method; it is not connected to or
an extension of YNAB.

It is built with .NET 11 Blazor, Fluent UI Blazor, ASP.NET Core Identity,
PostgreSQL, and .NET Aspire, and it runs as an installable progressive web app.

Start with the [budgeting guide](docs/budgeting.md) for the everyday workflow,
calculation rules, and the authenticated API.

## Who it is for

Households, couples, and small groups who want a shared zero-based budget they
fully control, and self-hosters who want to run it themselves instead of renting
a subscription.

A first session is short:

1. Register an account — signup is open and signs you in immediately; no email
   confirmation or owner approval is required.
2. Create a plan and, optionally, accept the suggested category groups.
3. Add checking, savings, cash, credit, loan, or tracking accounts.
4. Assign money in the monthly table until **Ready to assign** is zero.
5. Record spending, then review income, spending, and net worth in **Reflect**.

## Stack at a glance

| Area | Choice |
| --- | --- |
| Web app | .NET 11 Blazor: server project with a hosted WebAssembly client; the budgeting workspace runs `InteractiveAuto`, other pages use static SSR with enhanced navigation. |
| UI | Fluent UI Blazor 5.0.1, self-hosted Plus Jakarta Sans, application CSS design tokens. |
| Data | PostgreSQL 18 through EF Core 10, Npgsql, and ASP.NET Core Identity. |
| Orchestration | .NET Aspire 13.6 AppHost; Docker for local PostgreSQL, Azure Container Apps in production. |
| Tests | xUnit v3 on Microsoft.Testing.Platform, bUnit, Testcontainers, and Playwright. |

Project boundaries and dependency direction:

| Project | Responsibility |
| --- | --- |
| `YHAB` | Identity, persistence, budgeting features, minimal API, static SSR. |
| `YHAB.Client` | WebAssembly startup and the authenticated HTTP budget client. |
| `YHAB.UI` | Shared pages, components, and layouts used by both hosts. |
| `YHAB.SharedKernel` | Budget DTOs and pure calculations shared by server and browser. |
| `YHAB.ServiceDefaults` | OpenTelemetry, health checks, and service wiring. |
| `YHAB.AppHost` | Aspire resource graph, migrations resource, and Azure deployment model. |

`YHAB.Client` and `YHAB.UI` never reference the server project, and
`YHAB.SharedKernel` references no other project. Aspire starts the graph in this
order:

```text
postgres → yhabdb → yhab-migrations → yhab (server + WebAssembly client)
```

See the [frontend guide](docs/frontend.md) for Fluent UI setup, styling tokens,
render boundaries, and the installable app.

## Prerequisites

- .NET SDK **11.0.100** (selected by `global.json`).
- A CPU that meets the .NET 11 baseline: x86-64-v2 or later on x64, or Arm64 with
  LSE atomics. The deployed Container Apps host and current development machines
  already qualify; older self-hosted runners and virtual machines may not.
- Aspire CLI **13.6.5** on `PATH` (the version the deployment workflow pins), with
  the AppHost SDK on **13.6.5**. Follow the
  [Aspire installation guide](https://aspire.dev/get-started/install/).
- Docker Desktop running Linux containers, or another Aspire-supported container runtime.
- A trusted .NET development HTTPS certificate: `dotnet dev-certs https --trust`.
- Optional VS Code with this repository's recommended C# and Aspire extensions.
  The editor opens `YHAB.slnx` by default.

Verify the environment from the repository root:

```powershell
dotnet --version
aspire --version
aspire doctor
```

The root `aspire.config.json` locates the AppHost explicitly. Use PowerShell 7 for
the scripts in this repository.

## Run it

From the solution root, build once and then start Aspire:

```powershell
dotnet build YHAB.slnx
aspire run
```

The first build/start may restore NuGet packages, download Aspire/EF tooling, and
pull container images. Migrations create the Identity and budgeting tables; no
user accounts, financial accounts, or local credentials are included. Register,
then create a plan from **Your plans**.

Stop with `aspire stop --non-interactive` (or Ctrl+C for a foreground run).
PostgreSQL and any started pgAdmin container stop with Aspire; the database volume
survives.

Aspire is the default entry point for running and debugging. Do not launch the web
or WebAssembly project alone, and stop a running instance before a full solution
build on Windows to release file locks. The
[local development guide](docs/local-development.md) covers background runs,
launch profiles, dashboard endpoints, database inspection, EF migrations, and
agent tooling.

## Debug

- **VS Code:** install the recommended Aspire and C# extensions, select
  **Aspire: YHAB** in Run and Debug, and press **F5** (or **Ctrl+F5** to run
  without debugging). The checked-in `.vscode/launch.json` discovers the AppHost
  through `aspire.config.json`.
- **Visual Studio:** set **YHAB.AppHost** as the startup project, select its
  **https** profile, and use F5 or Ctrl+F5.

Set breakpoints in server code-behind, then exercise the page through the HTTPS
endpoint Aspire shows. See [debug through Aspire](docs/local-development.md#debug-through-aspire)
for dashboard browser troubleshooting and breakpoint caveats.

## Build, test, and validate

Run from the repository root. Formatting is required for every change set,
including documentation; stop Aspire first on Windows, and review the fixes
before requiring a clean verification pass. Formatting does not replace checks for
other file types:

```powershell
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
dotnet build YHAB.slnx
```

Headless unit and component tests need no external services; after code changes,
build the solution and run both before the infrastructure suites. All five test
projects use native Microsoft.Testing.Platform and Shouldly:

```powershell
dotnet test --project tests/YHAB.UnitTests/YHAB.UnitTests.csproj
dotnet test --project tests/YHAB.ComponentTests/YHAB.ComponentTests.csproj
```

The integration, Aspire, and browser suites need Docker, Aspire tooling,
development HTTPS, and Playwright Chromium:

```powershell
pwsh ./tests/YHAB.PlaywrightTests/bin/Debug/net11.0/playwright.ps1 install chromium
dotnet test --solution YHAB.slnx --max-parallel-test-modules 1
```

For opt-in local feedback, `pwsh ./scripts/Invoke-Validation.ps1 -Scope Fast`
verifies formatting, builds, and runs the headless tests; the same entry point
backs the opt-in Git hooks and
[agent validation](build/agent-hooks.md#opt-in-agent-validation)
(`YHAB_AGENT_VALIDATE=1`). Install local hooks explicitly with
`pwsh ./scripts/Install-GitHooks.ps1`: pre-commit checks staged whitespace and
pre-push runs Fast validation. Test resources are disposable and never use the
development database volumes. The solution test command matches CI's project-level
concurrency limit so database stress does not compete with AppHost/browser startup;
per-project xUnit settings remain unchanged. Local checks complement, not replace,
CI. See [local validation](build/README.md#local-validation-and-opt-in-git-hooks),
[build rules](build/README.md) for formatting scope, analyzer rules, architecture
checks, and the Razor code-behind policy, and
[tests/README.md](tests/README.md) for test projects, prerequisites, layer
selection, and conventions.

## Deploy

The AppHost publishes to Azure Container Apps with a migrations job, PostgreSQL
Flexible Server, and managed identity authentication. Pushes to `main` deploy
through GitHub Actions after validation; pull requests validate without deploying.
A one-time bootstrap script creates the resource group, deployment identity, and
GitHub environment. See the [deployment guide](docs/deployment.md) for
provisioning, the workflow sequence, and deployed diagnostics.

## Technical notes

- **Configuration.** `Budgeting:AutomaticPosting` (environment variable
  `Budgeting__AutomaticPosting=false`) disables the background recurring-entry
  worker for a run; interactive catch-up remains. `Budgeting:TimeZone` sets the
  budget's server date and defaults to `America/Chicago`.
- **Health.** `/health` reports database readiness and `/alive` reports process
  liveness. Outside Development they are disabled unless
  `HealthChecks:ExposeEndpoints` is true; the production AppHost sets it for probes.
- **Installable app.** YHAB installs from a compatible browser over HTTPS and
  keeps an offline fallback page without caching budget data. See
  [installable web app](docs/frontend.md#installable-web-app).
- **Account recovery.** Signup does not verify email addresses, so password
  recovery is owner-assisted through a server-side maintenance command. See
  [owner-assisted password recovery](docs/operations.md#owner-assisted-password-recovery).
- **Local secrets and data.** Keep AppHost user secrets and the Aspire data volume
  together when preserving development data; see
  [application identity and local data](docs/local-development.md#application-identity-and-local-data).
- **Framework versions.** The projects target .NET 11 while EF Core, Npgsql, and
  the Identity EF integration stay on 10.0.x; the
  [build rules](build/README.md#central-package-management) explain why.

## Documentation

| Document | Contents |
| --- | --- |
| [docs/budgeting.md](docs/budgeting.md) | Everyday workflow, budget terms, sharing, monthly rules, recurrence, app boundaries, cancellation lifetimes, minimal API. |
| [docs/budgeting-validation.md](docs/budgeting-validation.md) | Dated validation evidence log for the budgeting foundations and performance work. |
| [docs/local-development.md](docs/local-development.md) | Aspire lifecycle, debugging, resource graph, database inspection, EF migrations, agent tooling. |
| [docs/deployment.md](docs/deployment.md) | Azure resources, GitHub Actions, bootstrap, deployment sequence, deployed diagnostics. |
| [docs/operations.md](docs/operations.md) | Health and telemetry, Identity account policy, password recovery, protected data. |
| [docs/frontend.md](docs/frontend.md) | Fluent UI setup, typography, design tokens, render boundaries, installable app. |
| [build/README.md](build/README.md) | SDK, package, formatting, analyzer, architecture, and Razor code-behind rules. |
| [build/agent-hooks.md](build/agent-hooks.md) | Agent documentation hooks, instruction placement, and opt-in validation. |
| [tests/README.md](tests/README.md) | Test projects, browser policy, architecture boundaries, assertions, runner settings. |
| [.github/copilot-instructions.md](.github/copilot-instructions.md) | Repository-wide agent rules and routing to path-scoped instruction files. |