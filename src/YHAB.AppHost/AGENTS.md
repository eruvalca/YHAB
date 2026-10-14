# Aspire and infrastructure

Read this guide before application lifecycle, migration, deployment, or Aspire
tooling operations, even when no AppHost files change. It also governs resource
wiring and related root configuration, deployment scripts/workflows, and
infrastructure test orchestration. Database/migration dependencies and health
contracts are in the [server guide](../YHAB/AGENTS.md); read it before changing
that wiring. Consult the [test guide](../../tests/AGENTS.md) before running tests.
Detailed setup and runtime procedures remain in the [root README](../../README.md).

## Orchestration and developer resources

- Aspire is the default application run/debug entry point. Use the CLI for agent
  runs and the checked-in **Aspire: YHAB** VS Code configuration for F5 debugging;
  in Visual Studio, use `YHAB.AppHost` as the startup project. Do not bypass
  orchestration by launching the web or WebAssembly project alone. Unit/component
  tests remain headless and do not require Aspire.
- Run Aspire CLI commands from the repository root; `aspire.config.json` selects
  `src/YHAB.AppHost/YHAB.AppHost.csproj`. Use
  `aspire start --launch-profile https --non-interactive`,
  `aspire wait yhab --timeout 120 --non-interactive`, and `aspire describe`.
  Stop Aspire before formatting/full builds on Windows and when agent validation
  is finished. Ordinary shutdown uses `aspire stop`; `--force` removes persistent
  resource instances and requires explicit destructive intent for one AppHost.
  In 13.6, named volumes are preserved unless `--volumes` is also requested;
  never add either cleanup flag to routine validation.
- Keep the Aspire CLI 13.6.0 workflow pin, the compatible Aspire 13.6.1 package
  set including the explicitly selected `Aspire.Hosting.EntityFrameworkCore`
  preview, and the managed EF tool pinned at 10.0.12 in `AppHost.cs` aligned with
  the documented setup in `README.md`.
  Use `.agents/skills` as the shared skill location; retain the single user-level
  `aspire agent mcp` entry for each agent rather than adding repository duplicates.
- pgAdmin is an explicit-start, run-mode developer tool. See `README.md` for
  endpoint discovery, data persistence, and telemetry. PostgreSQL's **REPL**
  command opens `psql`; use `\connect yhab` to select the application database
  and `\q` to exit. It has normal write permissions. Database reset/drop requires
  an explicit intent to delete the local data. Do not add preview browser logging
  unless explicitly requested.
- The dashboard retains run history automatically. Keep persisted dashboard data
  in its user-local default directory and out of shared artifacts: it can contain
  unredacted credentials. View or export console logs before stopping if they are
  needed in historical runs; not every console stream is captured automatically.
- A normal run leaves pgAdmin **Not started**, migrations
  **Finished**, and the web/database resources **Running / Healthy**. Inspect
  resource commands and logs before treating optional or one-shot resource states
  as failures. Use existing Identity registration/login/account management to
  exercise PostgreSQL; remove only disposable accounts created for validation.
