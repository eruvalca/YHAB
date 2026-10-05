param(
    [Parameter(Mandatory)]
    [guid] $SubscriptionId,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]+$')]
    [string] $Location,

    [string] $ResourceGroup = 'rg-yhab-production',
    [string] $Repository = 'eruvalca/YHAB'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# One-time bootstrap only. Aspire owns the application infrastructure and deployment.
$account = az account show --subscription $SubscriptionId --output json | ConvertFrom-Json
gh auth status
$environments = gh api "repos/$Repository/environments" | ConvertFrom-Json
$existingEnvironment = $environments.environments | Where-Object { $_.name -eq 'production' }
if ($existingEnvironment) {
    if (-not $existingEnvironment.deployment_branch_policy.custom_branch_policies) {
        throw 'The existing production environment uses a different branch policy. Configure a main-only custom policy before bootstrap.'
    }
    $existingPolicies = gh api "repos/$Repository/environments/production/deployment-branch-policies" | ConvertFrom-Json
    if ($existingPolicies.branch_policies | Where-Object { $_.name -ne 'main' -or $_.type -ne 'branch' }) {
        throw 'The production environment permits deployments beyond main. Review its branch policies before enabling OIDC.'
    }
}

$group = az group create --subscription $SubscriptionId --name $ResourceGroup --location $Location `
    --tags application=YHAB environment=production --output json | ConvertFrom-Json
if ($group.location -ne $Location) {
    throw "Existing resource group is in '$($group.location)', not '$Location'. Verify the deployment destination."
}

foreach ($provider in @('Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.DBforPostgreSQL',
        'Microsoft.ManagedIdentity', 'Microsoft.OperationalInsights', 'Microsoft.Insights')) {
    az provider register --subscription $SubscriptionId --namespace $provider --wait --output none
}

$identity = az identity create --subscription $SubscriptionId --resource-group $ResourceGroup `
    --name id-yhab-github --location $Location --output json | ConvertFrom-Json

# Contributor deploys resources. RBAC Administrator lets Aspire assign application/registry roles.
# Both grants are confined to this resource group.
foreach ($role in @('b24988ac-6180-42a0-ab88-20f7382dd24c', 'f58310d9-a9f6-439a-9e8d-f62e7b41a168')) {
    az role assignment create --subscription $SubscriptionId --assignee-object-id $identity.principalId `
        --assignee-principal-type ServicePrincipal --role $role --scope $group.id --output none
}

az identity federated-credential create --subscription $SubscriptionId --resource-group $ResourceGroup `
    --identity-name $identity.name --name github-production --issuer 'https://token.actions.githubusercontent.com' `
    --subject "repo:${Repository}:environment:production" --audiences 'api://AzureADTokenExchange' --output none

$environmentPath = "repos/$Repository/environments/production"
if (-not $existingEnvironment) {
    $environmentBody = @{
        deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true }
    } | ConvertTo-Json -Depth 3
    $environmentBody | gh api --method PUT $environmentPath --input - --silent
}
$policies = gh api "$environmentPath/deployment-branch-policies" | ConvertFrom-Json
if (-not ($policies.branch_policies | Where-Object { $_.name -eq 'main' -and $_.type -eq 'branch' })) {
    gh api --method POST "$environmentPath/deployment-branch-policies" -f name=main -f type=branch --silent
}

$variables = @{
    AZURE_SUBSCRIPTION_ID = $account.id
    AZURE_TENANT_ID = $account.tenantId
    AZURE_CLIENT_ID = $identity.clientId
    AZURE_RESOURCE_GROUP = $ResourceGroup
    AZURE_LOCATION = $Location
}
foreach ($entry in $variables.GetEnumerator()) {
    gh variable set $entry.Key --repo $Repository --env production --body $entry.Value
}

Write-Output "Prepared $ResourceGroup in $Location and GitHub's production environment for $Repository."
Write-Output 'The application is provisioned by the deploy workflow after these changes reach main.'
