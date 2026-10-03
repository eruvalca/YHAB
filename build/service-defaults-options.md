# Service defaults options

These optional examples were originally commented out in
`src/YHAB.ServiceDefaults/Extensions.cs`. They are not active configuration.
Add the relevant code and dependencies only when adopting an option.

## Restrict service discovery to HTTPS

In `AddServiceDefaults`, after registering service discovery, configure the
allowed schemes. Add `using Microsoft.Extensions.ServiceDiscovery;` to the file.

```csharp
builder.Services.Configure<ServiceDiscoveryOptions>(options =>
{
    options.AllowedSchemes = ["https"];
});
```

## Instrument outgoing gRPC calls

Add the `OpenTelemetry.Instrumentation.GrpcNetClient` package to the service
defaults project. In `ConfigureOpenTelemetry`, add `AddGrpcClientInstrumentation`
to the tracing builder chain inside `WithTracing`, alongside the existing ASP.NET
Core and HTTP client instrumentation:

```csharp
.AddGrpcClientInstrumentation()
```

## Export to Azure Monitor

Add the `Azure.Monitor.OpenTelemetry.AspNetCore` package to the service defaults
project and import its extension methods. The template's optional registration
belongs in `AddOpenTelemetryExporters`:

```csharp
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor();
}
```

The current implementation retains its existing OTLP exporter configuration.
Choose the intended exporter setup when adopting Azure Monitor.
