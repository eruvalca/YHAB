# YHAB

A manual envelope-budgeting application built with .NET 10 Blazor, Fluent UI,
Aspire, PostgreSQL, and ASP.NET Core Identity. Each user has private USD plans,
manual accounts and transactions, monthly assignments, targets, credit-card
payment reserves, reconciliation, recurring entries, undo/redo, and reports.

See the [budgeting guide](docs/budgeting.md) for workflows, calculation rules,
render boundaries, and the authenticated API. Bank connections, transaction
imports, photos, shared plans, and one-off scheduled entries are intentionally
outside the application.

## Installable web app

YHAB supports installation from a compatible browser over HTTPS. Open **Install
YHAB** above the page content for an install button when the browser offers one,
or instructions for its menu. On iPhone/iPad, use **Share → Add to Home Screen**.
An installed app opens **Your plans** in its own window; normal authentication
still applies. Browser and OS support determine the installation experience.

This is an online application with an offline fallback. After one successful
visit and service-worker installation, failed full-page and Blazor enhanced GET
navigations show a connection screen with **Try again**. The worker stores only
`offline.html`, its stylesheet, and its icon. It never caches account/budget HTML
or API responses, and never intercepts POSTs or queues saves. A first visit without
a connection cannot show the fallback. Losing connectivity while already editing
continues to use the application's existing error/reconnect handling; if a save
was interrupted, check its status after reconnecting before entering it again.
The Blazor runtime may manage its own framework-resource cache independently.

New deployments install a worker in the background and offer **Reload to update**
or **Later**. Finish saves and preserve unsaved entries before confirming the
reload. Only the tab requesting it reloads; other tabs keep their forms and offer
their own reload. A later visit or returning to a visible tab checks for updates
(visibility checks are limited to once per minute). Keep server APIs compatible
with open clients during deployment; the update prompt cannot make old code
compatible with a breaking backend change.

The selected Pocket Fan artwork with **YHAB** on its front pocket is kept in
`design/branding/pocket-fan-yhab.png`. The exported 192px and 512px icons, 180px
Apple touch icon, and 32px favicon live under `src/YHAB/wwwroot`. The manifest and
offline document assume the application is hosted at the origin root, as it is
today. Serve the worker at its stable URL and allow revalidation through any CDN;
do not give that URL immutable cache rules.
Use a stable HTTPS origin for a deployed installation.

The same worker runs in development and published builds. Its version comes from
application content hashes, without a manual version bump; see
[PWA build assets](build/README.md#pwa-build-assets). To reset a local experiment,
unregister YHAB's worker and clear its `yhab-offline-*` caches in browser developer
tools. Changing the local port creates a different service-worker/install origin.
See [browser validation](tests/README.md#choosing-an-integration-layer) for the
automated checks and the remaining device-level installation checks.

## Local prerequisites

- .NET SDK **10.0.401** (selected by `global.json`).
- Aspire CLI **13.6.0** on `PATH`, matching the AppHost SDK and stable integrations.
  Follow the [Aspire installation guide](https://aspire.dev/get-started/install/).
- Docker Desktop running Linux containers, or another Aspire-supported container runtime.
- A trusted .NET development HTTPS certificate: `dotnet dev-certs https --trust`.
- Optional VS Code with this repository's recommended C# and Aspire extensions.
  The editor opens `YHAB.slnx` by default.

Run `dotnet --version`, `aspire --version`, and `aspire doctor` from the repository
root to verify the environment. The AppHost is explicitly located by the root
`aspire.config.json`.

## First run and application identity

Use PowerShell 7 for the scripts in this repository. From the solution root,
run `dotnet build YHAB.slnx`, then `aspire run`. The first build/start may
restore NuGet packages, download Aspire/EF tooling, and pull container images.
Migrations create Identity and budgeting tables; no user accounts, financial
accounts, or local credentials are included. Register and sign in, open **Your
plans**, and create a plan. Suggested categories are optional. Signup is open and
signs the user in immediately; no email confirmation or owner approval is required.
Git initialization is optional and separate.

If this solution was generated, `.template-provenance.json` records its template
version and source commit. It is an independent snapshot: template updates do not
update this application. Review SDK/package/skill updates in this repository.

Use a unique application name for projects you run side by side. Different ports
do not isolate browser cookies on the same hostname. A generated secrets ID is
unique even when the application name is reused, but the development hostname is
name-based. Aspire's default data-volume name depends on the AppHost path; moving
or renaming a checkout can select a different volume. Keep the original secrets
and volume together if preserving development data.

## Run through Aspire

Aspire is the default entry point for running and debugging the application. It
starts PostgreSQL, applies migrations, supplies configuration, and starts the web
project. The hosted WebAssembly client runs through that web project.

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
and default launch profile. `AddProject` selects the
matching web profile and derives its endpoints from `applicationUrl`; there is no
AppHost override for the web profile or its ports. The web profiles retain
`yhab.dev.localhost:0`: the hostname is our local convention, and port `0` asks
Aspire to allocate an available port instead of using the template's fixed ports.
The other web launch settings preserve the template's environment and Blazor debugging.

The AppHost's `https` profile omits dashboard, OTLP, and resource-service addresses:
Aspire 13.6.0 supplies secure, dynamically allocated local endpoints. The optional
AppHost `http` profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`, which opts into
HTTP endpoints with allocated ports. This opt-in applies only when that profile
is selected; the default `https` profile keeps Aspire's secure defaults.

Use the web resource's **Rebuild** command to apply compiled application changes.
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

The web host posts due recurring budget entries in bounded background batches
while it is running. Set `Budgeting:AutomaticPosting` to `false` (environment
variable `Budgeting__AutomaticPosting=false`) to disable that worker for a run.
Interactive catch-up remains available. `Budgeting:TimeZone` determines the
budget's server date and defaults to `America/Chicago`. See the existing
[budgeting guide](docs/budgeting.md#recurrence-and-history) for scheduling,
approval, retry and undo behavior.

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

## Fluent UI Blazor and styling

`Microsoft.FluentUI.AspNetCore.Components` **5.0.0** is centrally pinned in
`Directory.Packages.props` and referenced by the server, client, and shared UI.
Both host `Program.cs` files call `AddFluentUIComponents()` so prerendering,
Interactive Server, and Interactive WebAssembly resolve the same services.
The shared kernel has no UI dependency. Fluent UI Blazor v5+ is the application's
layout, styling, and component framework. Use its components and design tokens
for new UI; do not introduce a parallel CSS framework or default to custom Grid/Flexbox.

`Microsoft.FluentUI.AspNetCore.Components.Icons` **5.0.0** is referenced by the
shared UI and flows to both hosts. Navigation uses `IconRest`/`IconActive` with
regular/filled system icons; the counter uses `IconStart`. Use strongly typed
instances such as `new Icons.Regular.Size20.Home()` (the Razor imports define the
`Icons` alias) so publishing can retain referenced icons. Keep meaningful visible
labels; redundant icons are decorative and not extra tab stops. No icon font,
CDN, script, service registration, or separate emoji package is needed.

`App.razor` loads the Fluent baseline stylesheet, `YHAB.styles.css`, then
`app.css`. The generated solution stylesheet already imports the Fluent component
CSS bundle; do not add a second link to that bundle. `no-fuib-style` on the body
prevents Fluent's initializer from adopting another copy of the baseline after
the application overrides. The package's Blazor JavaScript initializer loads the
web components automatically, including static layout hamburger behavior. Do not
add a v4 web-components script or `FluentDesignTheme`. The body specifies a teal
brand palette with `data-theme-color`. The static header offers System, Light,
and Dark through v5's `Blazor.theme.setThemeMode` API. Fluent persists the
preference and follows OS changes in System mode. The shared initializer reapplies
the selected theme and restores the selector after enhanced navigation, which can
replace the body's client-side theme marker. Appearance does not start a .NET renderer.
Application styles use v5 CSS design tokens.

The shell uses `FluentLayout`, `FluentNav`, and `FluentNavItem`; the home, counter,
and authenticated pages use Fluent cards and buttons with native content links.
Native anchors preserve accessible link names and navigation before JavaScript
initializes. Account management
has Fluent navigation and a card around its content. `FluentGrid`/`FluentGridItem`
provide responsive page columns and `FluentStack` provides content spacing, with
a mobile drawer below the Fluent layout's 768px breakpoint. Keep grid adaptive
rendering off for static SSR; responsive CSS does not need a .NET event handler.

Render boundaries are deliberate:

- `Routes`, `MainLayout`, Home, plan selection, reports, plan settings, and Identity
  pages use static SSR. The budgeting workspace and retained Counter example opt
  into `InteractiveAuto` for .NET event handling. Workspace and Counter actions
  are disabled during prerendering until
  their renderer becomes interactive. Static pages don't start a .NET interactive
  runtime simply to display navigation or cards.
- The budgeting workspace and `Counter` host `FluentProviders` inside their interactive boundaries. Providers
  inherit its renderer and scoped services for dialogs, toasts, tooltips, and key
  handling. Add providers inside other interactive pages/subtrees when needed,
  once per active renderer/service scope. The layout's `Body` never crosses an
  interactive boundary.
- Static navigation has ordinary `Href` links with `tabindex="0"`, since Fluent's
  interactive roving-tabindex setup does not run in SSR. Shell links use Blazor's
  enhanced navigation: the server renders the destination and Blazor updates the
  existing document without restarting the page or reloading the runtime.
  `YHAB.UI.lib.module.js` registers the collocated `MainLayout.razor.js` handler
  through `afterWebStarted`. It closes the Fluent mobile drawer on
  `enhancednavigationstart`, before the DOM update, including back/forward
  navigation. Stable layout/hamburger IDs preserve their JS wiring across updates.
  The desktop **Hide menu / Show menu** button stores its preference in local
  storage and reapplies it after enhanced navigation. It uses the same static
  shell initializer, without starting a .NET renderer; the mobile drawer remains
  independent of that preference. Its explicit `tabindex="0"` survives SSR patches
  that would otherwise remove Fluent's browser-added keyboard focus attribute.
  The shell grows with its content and uses document scrolling, allowing Blazor
  to reset scroll position on navigation and restore it through browser history.
  `app.css` overrides Fluent's default fixed-height, overflow-hidden body so
  mouse-wheel and touch scrolling can reach the full document.
  The account settings menu does not use interactive categories or event callbacks.
- Identity forms retain native inputs, submit buttons, form names, antiforgery,
  and passkey hooks. They receive Fluent token styling while preserving static
  POST mapping, autofill, and cookie redirects. These POSTs keep normal navigation
  so cookie changes refresh authentication state. Shared notices retain their
  `notice`/`data-kind` contract. The reconnect/error UI retains native controls
  because it must function while the Blazor circuit is unavailable.

Shared styles live in `src/YHAB/wwwroot/app.css`: sizing, typography, forms
(`account-form`, `form-field`, `checkbox-field`), action groups (`actions`),
notices (`notice` with a semantic `data-kind`), and table overflow
(`table-container`). Component-specific styles belong in adjacent `.razor.css`
files. Use narrowly scoped `::deep` selectors for child component markup.
Native button styling excludes slotted controls: Fluent dropdowns render a native
button in their `control` slot and own its border and padding. Filter toolbars use
explicit Fluent alignment and zero field margins so labels do not displace actions.
Editor fields also use zero margins, leaving spacing to their Fluent grid/stack;
the shared editor surface stretches text inputs, dropdowns, and text areas to the
available column width while keeping checkbox sizing intrinsic.
All three navigation menus use transparent resting backgrounds and Fluent's subtle
hover token. The signed-in shell link reads **Your account**; account details keep
the email address. Money uses shared `data-money-tone` colors from Fluent tokens.

Custom CSS is limited to application sizing, accessibility, and static form
compatibility, using Fluent tokens. Prefer Fluent layout/spacing parameters;
the library's generated styles are expected. Use normal flow for prose and
semantic tables. Do not recreate Bootstrap utilities or add `!important` or
ornamental animations. Use icons to clarify navigation/actions. Keep form
labels before their controls and preserve Blazor/Identity behavior hooks when
editing markup. Check narrow screens, keyboard focus, and text wrapping when
changing layouts. See `AGENTS.md` for the authoring conventions.

Setup references: [installation](https://www.fluentui-blazor.net/installation),
[layout](https://www.fluentui-blazor.net/layout), and the
[released package source](https://github.com/microsoft/fluentui-blazor/tree/358e449b08711f3343ce6c2ffbe6c9f77045bde5).
Navigation references: [enhanced navigation](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/navigation?view=aspnetcore-10.0#enhanced-navigation-and-form-handling)
and [JavaScript with static SSR](https://learn.microsoft.com/aspnet/core/blazor/javascript-interoperability/static-server-rendering?view=aspnetcore-10.0).

## Resource graph and database

```text
postgres (PostgreSQL 18.3, managed data volume)
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
`Aspire.Hosting.PostgreSQL` 13.6.0. Container lifetime remains Aspire's default
session lifetime. Data uses an Aspire-managed named volume rather than a repository
file. This is a fresh PostgreSQL schema; the original SQLite scaffold is removed.

Aspire supplies `ConnectionStrings:yhabdb` to both the application and migration
tool. The application uses Npgsql EF Core 10.0.3, EF Core 10.0.12, and Aspire's
Azure Npgsql EF integration 13.6.0 (password authentication locally, managed identity
in Azure). Its non-pooled `IDbContextFactory<ApplicationDbContext>`
supports a context per Blazor operation; Identity can still resolve the scoped
context. Dispose factory-created contexts with `await using`.

Identity schema version 3 is retained, including the `AspNetUserPasskeys` table.
Registration and later sign-ins do not require email confirmation, including for
existing unconfirmed accounts. New addresses remain **unverified** in Identity;
signup does not send a confirmation message. The legacy registration-confirmation
page never exposes a confirmation token. Password rules, duplicate-user checks,
passkeys, and optional two-factor authentication remain in place.

`IdentityNoOpEmailSender` remains configured. **Forgot your password?** explains
owner-assisted recovery instead of promising an email. Email verification and
email-address changes still require a real sender; adding one later does not
automatically make unverified addresses eligible for emailed password recovery.
Owner-assisted recovery instructions remain available on the password-recovery
and confirmation pages even with a real sender, including when external-login
linking failed after creating an unconfirmed account without a password.
External-login provider credentials and custom-domain configuration remain optional
application-specific setup. Azure deployment and probe exposure are configured below.

### Owner-assisted password recovery

Users contact the person running their YHAB instance through an established private
channel. There is no public recovery-token endpoint or admin role to bootstrap.
The owner verifies the requester against the intended account before generating a
link. A claimed email address alone is not proof of ownership, because signup does
not verify it. Do not ask users for passwords or two-factor recovery codes.

From a private shell on the server, under the **same OS/service identity, environment,
content root, database configuration, and Data Protection key ring** as the web app:

```powershell
# Run from the deployed YHAB directory; use the actual account ID and site origin.
dotnet ./YHAB.dll recover-account '<user-id>' 'https://your-yhab-host'
```

This maintenance command builds the service container without starting an HTTP
server or background jobs. It uses the existing `ConnectionStrings:yhabdb` settings;
do not put connection strings in shell history. For local development, keep Aspire
as the application entry point and run the maintenance DLL with the web resource's
configuration and `--contentRoot` pointing to `src/YHAB`. The account ID is the
`Id` column of `AspNetUsers`, available to the owner through the PostgreSQL REPL or
their secured database administration tool. No account is created if it is missing.

The command prints a reset link to the operator's console (not application logs).
Keep that terminal private, do not run it through a log-capturing resource command,
and send the link only to the verified requester. The user opens it, enters their
existing account email, and chooses their own password. The standard Identity token
expires after one day and is invalid after a successful password reset; generating
a link alone changes neither the password nor active sessions. Using it rotates
the security stamp, so existing sessions are rejected on their next Identity stamp
validation, not necessarily immediately. Do not change Data Protection keys or
content roots between generating and using the link.

Password recovery does **not** disable two-factor authentication, remove passkeys,
or clear lockouts. Users who lose their authenticator should use their saved 2FA
recovery codes; this command does not bypass the second factor.

## EF migrations

`Aspire.Hosting.EntityFrameworkCore` **13.6.0-preview.1.26479.8** manages
`yhab-migrations` and its **dotnet-ef 10.0.12** tool. It does not alter the machine's
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

## Azure deployment and GitHub Actions

The AppHost publishes to **Azure Container Apps** in standard mode, including its
managed Aspire dashboard. Local `aspire start` continues to use Docker PostgreSQL
and optional pgAdmin. Production provisions:

| Resource | Initial configuration |
| --- | --- |
| Container Apps environment and registry | Consumption environment, Azure Container Registry, hosted Aspire dashboard. |
| `yhab` web app | Public HTTPS, one minimum/maximum replica, status-only `/alive` and `/health` probes. One replica keeps the recurring worker active and avoids Blazor circuit distribution. |
| `yhab-migrations` job | Manual Linux EF bundle, ten-minute execution limit, no automatic retry; deployment waits up to fifteen minutes for a successful execution. |
| PostgreSQL Flexible Server | PostgreSQL 18, Burstable B1ms, 32 GiB, seven-day backups, no high availability. Entra managed identity authentication; no database password in GitHub. |
| Application Insights and Log Analytics | Retained server telemetry alongside the dashboard's live diagnostics. Ingestion and retention incur Azure charges. |

These are small initial production settings, not a high-availability configuration.
The PostgreSQL integration's default firewall permits Azure services (not arbitrary
public clients); managed identity still controls database access. Private networking
can be added later. The Azure provisioning SDK lacks an enum member for PostgreSQL
18, so the AppHost supplies the supported Bicep string explicitly. Azure manages
minor PostgreSQL updates; local and cloud minor versions can differ.

Aspire enables Container Apps' automatic .NET Data Protection storage. Preserve it
across revisions so Identity cookies and recovery tokens remain usable. HTTPS uses
the platform's generated domain initially; choose a stable custom domain before
registering production passkeys, since passkeys are tied to the site domain.

### One-time Azure and GitHub setup

Sign in with `az login` and `gh auth login`, then run from the repository root:

```powershell
pwsh ./scripts/Initialize-AzureDeployment.ps1 `
  -SubscriptionId '<azure-subscription-id>' -Location '<azure-region>'
```

The script creates `rg-yhab-production` and `id-yhab-github`, registers the required
Azure providers, and grants the deployment identity **Contributor** and **Role Based
Access Control Administrator** within that resource group only. Bootstrap requires
permission to create those assignments and administer the GitHub repository.
It trusts GitHub OIDC for `repo:eruvalca/YHAB:environment:production`, creates the
`production` GitHub environment with a `main` branch policy, and writes these
non-secret environment variables:

`AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`,
`AZURE_RESOURCE_GROUP`, and `AZURE_LOCATION`.

No client secret or publish profile is needed. Override `-ResourceGroup` or
`-Repository` for another instance. The bootstrap script configures branch policy
for automated deployment and preserves existing environment approvals; it rejects
an existing incompatible branch policy. Do not give pull-request
validation Azure credentials. Resource providers are registered at subscription
scope; the workflow's resource permissions remain scoped to the dedicated group.

### Deployment sequence

[Validate and deploy](.github/workflows/deploy.yml) runs formatting, a Release build,
all five test projects using isolated resources, and `aspire publish` on pull requests
and pushes to `main`. A successful main run signs into Azure with OIDC and runs
`aspire deploy --environment Production --non-interactive`. Manual dispatch is also
available on `main`. Runs are serialized without canceling an active deployment;
a superseded commit cannot begin another deployment.

The AppHost builds/pushes the migration image, provisions the job and its database
identity, starts the job, and polls **that execution** until success. Only then can
it provision the new web revision. Failed/stopped/canceled migrations fail the
pipeline before the web rollout. The old revision can continue serving, so schema
changes must remain compatible with the previously deployed application. A canceled
CI run can leave a job running in Azure: inspect its execution before retrying.
Deployment failure does not roll back database changes. Restore/recovery and
destructive migrations require a deliberate operator decision.

Bundle generation uses the application's real startup/Identity model. When
`EF.IsDesignTime` is true and no connection is supplied, startup uses a localhost
placeholder with dummy username/password values without opening a database. Both
values prevent Azure token discovery during credential-free CI publishing; real
connections still use the configured authentication. Normal web startup still
rejects a missing connection. `linux-x64` is restored explicitly and the bundle
container includes the ASP.NET Core runtime. The Azure job runs in Production and
invokes `/app/efbundle` directly without `--connection`: EF's command-line override
discards Azure Npgsql's configured data source, including TLS and token authentication.
The job reads `ConnectionStrings__yhabdb` through normal startup instead.
Production connections are injected into both the job
and web app, sharing `yhab-database-identity` so both can access objects created by
the bundle. This identity receives the PostgreSQL administrator access provided by
Aspire's integration and is separate from GitHub's deployment identity. Splitting
it into separate migration and restricted runtime users requires explicit database
grants and default privileges; changing identities alone breaks object access.

For a local operator deployment after bootstrap, sign in to Azure and set:

```powershell
$env:Azure__SubscriptionId = '<azure-subscription-id>'
$env:Azure__ResourceGroup = 'rg-yhab-production'
$env:Azure__Location = '<azure-region>'
$env:Azure__CredentialSource = 'AzureCli'
$env:Azure__AllowResourceGroupCreation = 'false'
aspire publish --environment Production --output-path "$env:TEMP/yhab-preview" --non-interactive
aspire do diagnostics --environment Production --non-interactive
aspire deploy --environment Production --non-interactive
```

Publishing generates a preview and migration bundle without applying infrastructure.
The diagnostics step shows the fully initialized dependency graph, including
`apply-yhab-migrations` before `provision-yhab-containerapp`; `--list-steps` alone
does not materialize all Azure targets in Aspire 13.6. Deploy reads the AppHost
model again. Keep generated artifacts, deployment state and telemetry out of Git
and public workflow artifacts. Local deploys must not overlap a GitHub deploy.

### Deployed logs and telemetry

Aspire prints the dashboard URL after deployment. It is also available from the
Container Apps environment's **Aspire dashboard** entry in Azure Portal. Access uses
Azure sign-in and the platform's supported resource-group roles (Contributor/Owner).
The managed dashboard is for live diagnosis; do not assume local dashboard run
history is retained there. Use Application Insights for historical requests,
exceptions, dependency traces and metrics, and Log Analytics for container console
logs. `/health` reports database readiness and `/alive` reports process liveness;
neither returns connection details. The workflow checks both and `/Account/Login`
after deployment, and writes the app URL into its run summary.

References: [Aspire Container Apps](https://aspire.dev/deployment/azure/container-apps/),
[Aspire CI/CD](https://aspire.dev/deployment/ci-cd/),
[EF bundles](https://aspire.dev/integrations/databases/efcore/migrations/),
[Azure .NET features](https://learn.microsoft.com/azure/container-apps/dotnet-overview),
[GitHub OIDC with Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure).

## Health, telemetry, and pgAdmin

The application references `YHAB.ServiceDefaults`. In Development, `/health`
checks readiness including PostgreSQL connectivity; `/alive` checks process liveness
independently of PostgreSQL. Aspire monitors `/health`. The database readiness check
has a five-second timeout so EF's transient retries do not hold an unhealthy response
open for minutes. Cancellation is cooperative: an in-flight Npgsql connection attempt
can delay the response (about 15 seconds in local outage validation). Normal
application operations retain Aspire's retry behavior. Outside Development, these
endpoints are disabled unless `HealthChecks:ExposeEndpoints` is true. The production
AppHost deliberately sets this flag for Container Apps' probes. Application Insights
export is enabled when `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured; OTLP
export remains available for the Aspire dashboard.

The dashboard exposes server logs, request traces, Npgsql database spans, and metrics.
Useful CLI commands include `aspire logs yhab`, `aspire otel traces`, and
`aspire otel logs`. The Aspire MCP server provides the same resource and telemetry
visibility to agents. Database query telemetry can contain application information;
keep exports and runtime logs out of Git.

Aspire 13.6 retains dashboard run history automatically in **Run** persistence
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

## Agent tools and skills

Shared repository skills live only in `.agents/skills`, supported by Codex and
Copilot CLI. Copilot desktop inherits repository/CLI skills and MCP configuration.
The Aspire skills use the first-party **aspire-skills v0.0.3** bundle refreshed
with CLI 13.6.0. Local corrections document 13.6's Project v2 diagnostic changes
and the distinction between persistent-resource cleanup and volume deletion.
Reconcile those corrections when refreshing the skills; use installed package/API
evidence and current documentation when upstream guidance differs. Project v2
migration remains deferred; the AppHost continues to use `AddProject`.

The existing user-level Codex and Copilot MCP entries run **`aspire agent mcp`**.
Keep a single entry per agent. No repository MCP duplicate or VS Code agent
configuration is required. On a new machine, install Aspire on `PATH`, then:

- Codex: `codex mcp add aspire -- aspire agent mcp` (unless already configured).
- Copilot CLI: use `/mcp add` to add a local/stdio server named `aspire`, command
  `aspire`, arguments `agent mcp`, at user scope. Its configuration is stored in
  `~/.copilot/mcp-config.json` and is inherited by Copilot desktop.
- Restart/reload the agent after configuration. Start the application from this
  repository and use the Aspire MCP resource-list tool to verify the connection.
  If several AppHosts are running, select this repository's AppHost explicitly.

The checked-in skills require no additional installation on clone. To refresh the
Aspire skills, use the matching CLI's `aspire agent init` with the **standard** skill
location (`.agents/skills`), then review the diff. Avoid creating alternate copies under
`.github`, `.codex`, or VS Code agent configuration. Keep user secrets, dashboard
tokens, runtime `.aspire` state, telemetry exports, and temporary browser output out
of source control.

The [Fluent UI Blazor v5 usage skill](.agents/skills/fluentui-blazor-usage/SKILL.md)
includes setup, data-grid, and theming references. The library is configured as
described above; installing the skill alone does not configure an MCP server.
When using its examples, follow
`AGENTS.md` for code-behind, CSS, static account rendering, and central package
versions. Verify version-sensitive APIs against the Fluent UI Blazor MCP server's
documentation and the released package source. The v5.0.0 package's assembly file
version is `5.0.0.26268`, also reported by the installed MCP server. Its version
checker compares this assembly version against the NuGet version and reports a
false mismatch; `5.0.0.26268` is not the NuGet package version. Some skill examples
still use prerelease APIs, so verify component names and parameters against the
installed package instead of copying them verbatim.

References: [Aspire MCP](https://aspire.dev/reference/cli/commands/aspire-agent-mcp/),
[Codex skills](https://learn.chatgpt.com/docs/build-skills#where-codex-loads-local-skills),
[Copilot CLI skills](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills),
[Copilot desktop](https://docs.github.com/en/copilot/how-tos/github-copilot-app/customize-github-copilot-app).

## Validation

For every change set, run formatting from the repository root, review the fixes,
and require a clean verification pass. Stop Aspire first on Windows. After code
changes, also build and run the unit and bUnit projects. All five test projects use
native Microsoft.Testing.Platform and Shouldly. The complete suite additionally
requires Docker, Aspire tooling, development HTTPS, and Playwright Chromium:

```powershell
dotnet format YHAB.slnx --severity warn
dotnet format YHAB.slnx --severity warn --verify-no-changes
dotnet build YHAB.slnx
pwsh ./tests/YHAB.PlaywrightTests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --solution YHAB.slnx
```

The unit and component projects can still run individually without external
processes. Testcontainers covers focused database integration, Aspire integration
covers the real resource graph and HTTP behavior, and Playwright covers browser
interaction against its own isolated Aspire application. Test resources are
disposable and never use the development database volumes. See
[tests/README.md](tests/README.md) for commands, prerequisites, layer selection, and
[build/README.md](build/README.md) for formatting scope, analyzer rules, and Razor
code-behind validation. Formatting does not replace checks for other file types.
