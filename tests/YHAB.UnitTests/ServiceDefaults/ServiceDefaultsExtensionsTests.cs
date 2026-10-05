using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;
using YHAB.ServiceDefaults;
using ServiceDefaultsApi = YHAB.ServiceDefaults.Extensions;

namespace YHAB.UnitTests.ServiceDefaults;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ServiceDefaultsExtensionsTests
{
    [Theory]
    [InlineData("/health", false)]
    [InlineData("/HEALTH/ready", false)]
    [InlineData("/alive", false)]
    [InlineData("/ALIVE/check", false)]
    [InlineData("/healthcare", true)]
    [InlineData("/alive-status", true)]
    [InlineData("/events", true)]
    public async Task TraceFilterOmitsHealthSegmentsWithoutDroppingApplicationRequestsAsync(string path, bool expected)
    {
        var builder = CreateBuilder(Environments.Development);
        builder.ConfigureOpenTelemetry();
        await using var app = builder.Build();
        _ = app.Services.GetRequiredService<TracerProvider>();
        var options = app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;
        var http = new DefaultHttpContext();
        http.Request.Path = path;

        options.Filter.ShouldNotBeNull()(http).ShouldBe(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceDefaultsPreserveConcreteBuilderAndRegisterSharedServicesAsync(bool explicitStaticCall)
    {
        var builder = CreateBuilder(Environments.Development);

        WebApplicationBuilder result = explicitStaticCall
            ? ServiceDefaultsApi.AddServiceDefaults(builder: builder)
            : builder.AddServiceDefaults();

        result.ShouldBeSameAs(builder);
        await using var app = result.Build();
        app.Services.GetRequiredService<IHttpClientFactory>().ShouldNotBeNull();
        var health = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);
        health.Entries.Keys.ShouldBe(["self"]);
        health.Entries["self"].Status.ShouldBe(HealthStatus.Healthy);
        health.Entries["self"].Tags.ShouldBe(["live"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenTelemetryPreservesConcreteBuilderAndRegistersProvidersAsync(bool explicitStaticCall)
    {
        var builder = CreateBuilder(Environments.Development);

        WebApplicationBuilder result = explicitStaticCall
            ? ServiceDefaultsApi.ConfigureOpenTelemetry(builder: builder)
            : builder.ConfigureOpenTelemetry();

        result.ShouldBeSameAs(builder);
        await using var app = result.Build();
        app.Services.GetRequiredService<MeterProvider>().ShouldNotBeNull();
        app.Services.GetRequiredService<TracerProvider>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultHealthChecksPreserveConcreteBuilderAndSelfCheckAsync(bool explicitStaticCall)
    {
        var builder = CreateBuilder(Environments.Development);

        WebApplicationBuilder result = explicitStaticCall
            ? ServiceDefaultsApi.AddDefaultHealthChecks(builder: builder)
            : builder.AddDefaultHealthChecks();

        result.ShouldBeSameAs(builder);
        await using var app = result.Build();
        var health = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);
        health.Entries.Keys.ShouldBe(["self"]);
        health.Entries["self"].Status.ShouldBe(HealthStatus.Healthy);
        health.Entries["self"].Tags.ShouldBe(["live"]);
    }

    [Theory]
    [InlineData("Development", false, false)]
    [InlineData("Development", false, true)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, true)]
    public async Task EnabledHealthEndpointsSeparateReadinessFromLivenessAsync(string environment, bool exposeEndpoints, bool explicitStaticCall)
    {
        var builder = CreateBuilder(environment);
        builder.Configuration["HealthChecks:ExposeEndpoints"] = exposeEndpoints.ToString();
        builder.AddDefaultHealthChecks();
        builder.Services.AddHealthChecks().AddCheck("startup", () => HealthCheckResult.Unhealthy());
        await using var app = builder.Build();

        var result = explicitStaticCall ? ServiceDefaultsApi.MapDefaultEndpoints(app: app) : app.MapDefaultEndpoints();

        result.ShouldBeSameAs(app);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Order(StringComparer.Ordinal).ShouldBe(["/alive", "/health"]);
        foreach (var endpoint in endpoints)
        {
            await using var responseBody = new MemoryStream();
            await using var scope = app.Services.CreateAsyncScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Response.Body = responseBody;

            await endpoint.RequestDelegate!(http);

            var isAlive = string.Equals(endpoint.RoutePattern.RawText, "/alive", StringComparison.Ordinal);
            http.Response.StatusCode.ShouldBe(isAlive ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
            responseBody.Position = 0;
            using var reader = new StreamReader(responseBody);
            (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldBe(isAlive ? "Healthy" : "Unhealthy");
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopmentDoesNotExposeHealthEndpointsAsync(string environment)
    {
        var builder = CreateBuilder(environment);
        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();

        app.MapDefaultEndpoints().ShouldBeSameAs(app);

        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ShouldBeEmpty();
    }

    [Fact]
    public void MappingNullApplicationRetainsArgumentGuard() =>
        Should.Throw<ArgumentNullException>(() => ServiceDefaultsApi.MapDefaultEndpoints(null!))
            .ShouldBeOfType<ArgumentNullException>().ParamName.ShouldBe("app");

    private static WebApplicationBuilder CreateBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(ServiceDefaultsExtensionsTests).Assembly.GetName().Name,
        });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] = null;
        return builder;
    }
}
