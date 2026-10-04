using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.FluentUI.AspNetCore.Components;
using YHAB.Components;
using YHAB.Data;
using YHAB.Features.Account.Endpoints;
using YHAB.Features.Account.Services;
using YHAB.Features.Budgeting.Endpoints;
using YHAB.Features.Budgeting.Services;
using YHAB.ServiceDefaults;
using YHAB.SharedKernel.Budgeting;

var recoveryCommand = args.Length > 0 && string.Equals(args[0], "recover-account", StringComparison.Ordinal);
if (recoveryCommand && args.Length < 3)
{
    // No host/request exists yet; finish the local usage message before exiting.
    await Console.Error.WriteLineAsync("Usage: YHAB recover-account <user-id> <https-origin> [host configuration arguments]".AsMemory(), CancellationToken.None);
    Environment.ExitCode = 2;
    return;
}

var builder = WebApplication.CreateBuilder(recoveryCommand ? args[3..] : args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddFluentUIComponents();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BudgetClock>();
builder.Services.AddScoped<BudgetStore>();
builder.Services.AddSingleton<BudgetMonthCache>();
builder.Services.AddScoped<BudgetQueries>();
builder.Services.AddScoped<RecurringProcessor>();
builder.Services.AddHostedService<RecurringWorker>();
builder.Services.AddScoped<IBudgetClient, ServerBudgetClient>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<IdentityCancellation>();
builder.Services.AddScoped<AccountSignInService>();
builder.Services.AddScoped<AccountPasskeyService>();
builder.Services.AddScoped<AccountRegistrationService>();
builder.Services.AddScoped<AccountEmailChangeService>();
builder.Services.AddScoped<AccountTwoFactorService>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("yhabdb")
    ?? throw new InvalidOperationException("Connection string 'yhabdb' not found. Start the application through Aspire or configure ConnectionStrings:yhabdb.");
// The factory supports one context per Blazor operation and also registers the scoped context used by Identity.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
builder.EnrichNpgsqlDbContext<ApplicationDbContext>();
// Bound readiness independently of EF's transient retries for normal application operations.
builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
{
    var databaseCheck = options.Registrations.SingleOrDefault(registration =>
        string.Equals(registration.Name, nameof(ApplicationDbContext), StringComparison.Ordinal));
    if (databaseCheck is not null)
    {
        databaseCheck.Timeout = TimeSpan.FromSeconds(5);
    }
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Signup is open; an unverified email is not proof of account ownership.
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserManager<CancellableUserManager>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

await using var app = builder.Build();

if (recoveryCommand)
{
    // Maintenance only: share the app's Identity configuration and keys, without
    // starting HTTP listeners or background workers. Bound database access.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    Environment.ExitCode = await AccountRecoveryCommand.ExecuteAsync(app.Services, args[1], args[2], Console.Out, timeout.Token);
    return;
}

// Request scopes and interactive circuit scopes are distinct. Background revalidation
// supplies its own token in its fresh scope instead of capturing a request here.
app.Use(async (context, next) =>
{
    context.RequestServices.GetRequiredService<IdentityCancellation>().Token = context.RequestAborted;
    await next(context);
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
    app.MapOpenApi().RequireAuthorization();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/plans", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next(context);
});

// Explicit ordering ensures cookie validation also sees the scoped request token.
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(YHAB.UI.UiAssemblyMarker).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
BudgetEndpoints.Map(app);
app.MapDefaultEndpoints();

await app.RunAsync();
