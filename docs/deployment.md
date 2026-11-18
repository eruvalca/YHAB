# Azure deployment and GitHub Actions

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

## One-time Azure and GitHub setup

Sign in with `az login` and `gh auth login`, then run from the repository root:

```powershell
pwsh ./scripts/Initialize-AzureDeployment.ps1 `
  -SubscriptionId '<azure-subscription-id>' -Location '<azure-region>'
```

The script creates `rg-yhab-production` and `id-yhab-github`, registers the required
Azure providers, and grants the deployment identity **Contributor** and **Role Based
Access Control Administrator** within that resource group only. Bootstrap requires
permission to create those assignments and administer the GitHub repository.
It reads GitHub's effective repository OIDC subject prefix and appends
`:environment:production` for Azure trust. This includes immutable owner/repository
IDs when enabled (for example,
`repo:eruvalca@25803306/YHAB@1402539322:environment:production`), while preserving
the legacy format for repositories that still use it. Custom subject templates or
a missing prefix stop bootstrap before Azure resources or roles are changed.
See [GitHub's immutable subject claims](https://docs.github.com/en/actions/reference/security/oidc#immutable-subject-claims).
The script creates the `production` GitHub environment with a `main` branch policy
and writes these non-secret environment variables:

`AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`,
`AZURE_RESOURCE_GROUP`, and `AZURE_LOCATION`.

No client secret or publish profile is needed. Override `-ResourceGroup` or
`-Repository` for another instance. The bootstrap script configures branch policy
for automated deployment and preserves existing environment approvals; it rejects
an existing incompatible branch policy. Do not give pull-request
validation Azure credentials. Resource providers are registered at subscription
scope; the workflow's resource permissions remain scoped to the dedicated group.

## Deployment sequence

[Validate and deploy](../.github/workflows/deploy.yml) checks formatting and tooling
on every PR and push to `main`. On pull requests,
`scripts/Get-ValidationScope.ps1` classifies the changed paths: application
changes select the Release build, unit/component tests, and PostgreSQL/Aspire
checks; deployment inputs also select `aspire publish`; documentation-only
changes skip that work. Browser jobs are never selected by changed paths and are
not merge-blocking. Applying `run-full-browser` to a ready pull request requests
the full non-performance Chromium suite without rerunning unrelated jobs; while
the label remains, new commits also request it. Removing the label stops future
requested runs. Every `main` push and merge group runs the non-browser validation
only. The separate
[Nightly browser and performance validation](../.github/workflows/nightly-browser.yml)
workflow runs both the full non-performance suite and `Category=Performance`
daily at 06:17 UTC and on manual dispatch. A failed browser or performance run on
`main` creates one rolling issue for its suite; repeat failures add comments
instead of duplicates. Performance assertion failures, including detected
regressions, use the performance issue. Heavy test projects run on separate
runners, not concurrently on one constrained runner. Every successful run on
`main` deploys to production, including documentation-only and test-only merges.
Pull requests validate without deploying.

New commits cancel obsolete PR runs. Main validation runs independently rather
than queuing behind an older deployment; it is not canceled. The deploy job
requires the `fast`, `tests`, and `publish` jobs to succeed and
runs only for a push to `main`. It deliberately has no job-level concurrency
group: a group left occupied by a stuck deployment blocks every later deployment.
The `main` ruleset requires `fast`, `Test YHAB.IntegrationTests`,
`Test YHAB.AspireIntegrationTests`, and `Validate deployment artifacts`,
requires branches to be up to date before merging, and blocks force pushes and
deletion. Native merge queue is unavailable for a public repository owned by a
user account. The workflow supports non-browser validation on `merge_group`, so
moving the repository to an organization lets native merge queue use the same
required checks without further workflow changes. A successful main run signs into Azure with OIDC and
runs `aspire deploy --environment Production --non-interactive`, preserving
migration-before-web ordering. SDK setup caches downloaded NuGet packages using SDK, central
build/dependency manifests and project files as inputs; it does not reuse compiled
test results, credentials or Aspire runtime/deployment state. The browser jobs
cache the Playwright Chromium download keyed by the central package manifest and
check for missing operating-system dependencies before installing them.

The workflow's JavaScript actions use SHA-pinned releases targeting Node.js 24,
including artifact upload and Azure login. This runtime is supplied by the GitHub
Actions runner and is separate from the application's .NET SDK and NuGet packages.

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

## Local operator deployment

After bootstrap, sign in to Azure and set:

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

## Deployed logs and telemetry

Aspire prints the dashboard URL after deployment. It is also available from the
Container Apps environment's **Aspire dashboard** entry in Azure Portal. Access uses
Azure sign-in and the platform's supported resource-group roles (Contributor/Owner).
The managed dashboard is for live diagnosis; do not assume local dashboard run
history is retained there. Use Application Insights for historical requests,
exceptions, dependency traces and metrics, and Log Analytics for container console
logs. `/health` reports database readiness and `/alive` reports process liveness;
neither returns connection details. The workflow checks both and `/Account/Login`
after deployment, and writes the app URL into its run summary. See the
[operations guide](operations.md#health-and-telemetry) for endpoint exposure rules.

References: [Aspire Container Apps](https://aspire.dev/deployment/azure/container-apps/),
[Aspire CI/CD](https://aspire.dev/deployment/ci-cd/),
[EF bundles](https://aspire.dev/integrations/databases/efcore/migrations/),
[Azure .NET features](https://learn.microsoft.com/azure/container-apps/dotnet-overview),
[GitHub OIDC with Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure).
