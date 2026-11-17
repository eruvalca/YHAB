# Instance operations

This guide covers running-instance concerns that are neither build rules nor
feature documentation: health and telemetry, Identity account policy, the
owner-assisted password-recovery command, and the local data and secrets an
operator must preserve.

## Health and telemetry

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
export remains available for the Aspire dashboard. ASP.NET Core and outbound HTTP
traces record exception events, including exception type and stack details, so
handled request and dependency failures have the same context as the runtime metric.

The deployed dashboard, Application Insights history, and Log Analytics console
logs are described in the [deployment guide](deployment.md#deployed-logs-and-telemetry).

## Identity account policy

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
application-specific setup. Azure deployment and probe exposure are configured
in the [deployment guide](deployment.md).

## Owner-assisted password recovery

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

## Protected data and secrets

- **Data Protection.** Aspire enables Container Apps' automatic .NET Data
  Protection storage. Preserve it across revisions so Identity cookies and recovery
  tokens remain usable. HTTPS uses the platform's generated domain initially; choose
  a stable custom domain before registering production passkeys, since passkeys are
  tied to the site domain.
- **Local database.** Aspire retains its generated local PostgreSQL password in
  AppHost user secrets, and local data lives in an Aspire-managed named volume whose
  name depends on the AppHost path. Keep the secrets and the volume together when
  preserving development data.
- **Telemetry and logs.** Database query telemetry and runtime logs can contain
  application information. Keep exports, dashboard state, and browser output out of
  Git and public workflow artifacts.
