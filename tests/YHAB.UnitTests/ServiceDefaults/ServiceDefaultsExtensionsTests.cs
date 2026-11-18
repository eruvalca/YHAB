using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
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
        options.RecordException.ShouldBeTrue();
        var http = new DefaultHttpContext();
        http.Request.Path = path;

        options.Filter.ShouldNotBeNull()(http).ShouldBe(expected);
    }

    [Fact]
    public async Task ExceptionRecordingIsEnabledForIncomingAndOutgoingHttpAsync()
    {
        var builder = CreateBuilder(Environments.Development);
        builder.ConfigureOpenTelemetry();
        await using var app = builder.Build();

        app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value.RecordException.ShouldBeTrue();
        app.Services.GetRequiredService<IOptions<HttpClientTraceInstrumentationOptions>>().Value.RecordException.ShouldBeTrue();
    }

    [Fact]
    public async Task IncomingHttpExceptionIsExportedAsAnOpenTelemetryExceptionEventAsync()
    {
        var exceptionEvent = new TaskCompletionSource<ActivityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var candidate in activity.Events)
                {
                    if (string.Equals(candidate.Name, "exception", StringComparison.Ordinal)
                        && candidate.Tags.Any(tag => string.Equals(tag.Key, "exception.type", StringComparison.Ordinal)
                            && tag.Value?.ToString()?.EndsWith(nameof(IOException), StringComparison.Ordinal) == true))
                    {
                        exceptionEvent.TrySetResult(candidate);
                        break;
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var builder = CreateBuilder(Environments.Development);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.ConfigureOpenTelemetry();
        await using var app = builder.Build();
        _ = app.Services.GetRequiredService<TracerProvider>();
        app.MapGet("/telemetry-exception", static context =>
            throw new IOException($"Telemetry exception test for {context.Request.Path}"));
        await app.StartAsync(TestContext.Current.CancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        addresses.ShouldNotBeNull();
        using var httpClient = new HttpClient();
        var exceptionUri = new Uri($"{addresses.Single()}/telemetry-exception", UriKind.Absolute);
        using var response = await httpClient.GetAsync(exceptionUri,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        app.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var recordedEvent = await exceptionEvent.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        var tags = recordedEvent.Tags.ToDictionary(tag => tag.Key, tag => tag.Value?.ToString(), StringComparer.Ordinal);

        tags["exception.type"].ShouldBe(typeof(IOException).FullName);
        tags["exception.message"].ShouldBe("Telemetry exception test for /telemetry-exception");
        tags["exception.stacktrace"].ShouldNotBeNull().ShouldContain(nameof(IOException));
    }

    [Fact]
    public async Task OutgoingHttpExceptionIsExportedAsAnOpenTelemetryExceptionEventAsync()
    {
        var exceptionEvent = new TaskCompletionSource<ActivityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var candidate in activity.Events)
                {
                    if (string.Equals(candidate.Name, "exception", StringComparison.Ordinal)
                        && candidate.Tags.Any(tag => string.Equals(tag.Key, "exception.type", StringComparison.Ordinal)
                            && tag.Value?.ToString()?.EndsWith(nameof(HttpRequestException), StringComparison.Ordinal) == true))
                    {
                        exceptionEvent.TrySetResult(candidate);
                        break;
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var builder = CreateBuilder(Environments.Development);
        builder.ConfigureOpenTelemetry();
        await using var app = builder.Build();
        _ = app.Services.GetRequiredService<TracerProvider>();

        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var endpoint = (IPEndPoint)tcpListener.LocalEndpoint;
        var acceptTask = tcpListener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        var responseUri = new Uri($"http://{endpoint}/telemetry-exception", UriKind.Absolute);
        var responseTask = httpClient.GetAsync(responseUri,
            TestContext.Current.CancellationToken);
        using var accepted = await acceptTask.AsTask().WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        accepted.Close();
        tcpListener.Stop();
        await Should.ThrowAsync<HttpRequestException>(() => responseTask);

        app.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var recordedEvent = await exceptionEvent.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        var tags = recordedEvent.Tags.ToDictionary(tag => tag.Key, tag => tag.Value?.ToString(), StringComparer.Ordinal);

        tags["exception.type"].ShouldBe(typeof(HttpRequestException).FullName);
        tags["exception.message"].ShouldNotBeNullOrEmpty();
        tags["exception.stacktrace"].ShouldNotBeNull().ShouldContain(nameof(HttpRequestException));
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
