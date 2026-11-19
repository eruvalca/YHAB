using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Shouldly;

namespace YHAB.PlaywrightTests;

internal sealed class WebProcessMeasurements(Process[] processes, string[] resources, Uri[] endpoints) : IDisposable
{
    private readonly List<Sample> _samples = [];
    public string[] Resources { get; } = resources;
    public Uri[] Endpoints { get; } = endpoints;

    public static async Task<WebProcessMeasurements> CreateAsync(DistributedApplication app, string projectPath, string configuration, CancellationToken token)
    {
        var processes = new List<Process>();
        var resources = new List<string>();
        var endpoints = new List<Uri>();
        try
        {
            await foreach (var resource in app.ResourceNotifications.WatchAsync(token))
            {
                if (!string.Equals(resource.Resource.Name, "yhab", StringComparison.Ordinal) || resources.Contains(resource.ResourceId, StringComparer.Ordinal)) { continue; }
                var pid = resource.Snapshot.Properties.FirstOrDefault(item => string.Equals(item.Name, "executable.pid", StringComparison.Ordinal));
                if (pid?.Value is null) { continue; }
                var processId = Convert.ToInt32(pid.Value, CultureInfo.InvariantCulture);
                if (processId <= 0) { continue; }
                var urls = resource.Snapshot.EnvironmentVariables.FirstOrDefault(item => string.Equals(item.Name, "ASPNETCORE_URLS", StringComparison.Ordinal))?.Value;
                if (string.IsNullOrEmpty(urls)) { continue; }
                var endpoint = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => new Uri(value))
                    .Single(value => string.Equals(value.Scheme, "https", StringComparison.Ordinal));
                (endpoint.IsLoopback || endpoint.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                    .ShouldBeTrue("only connect to this disposable AppHost's local replicas");
                // Identity cookies belong to localhost, independently of the port.
                endpoints.Add(new UriBuilder(endpoint) { Host = "localhost" }.Uri);
                var processConfiguration = Convert.ToString(
                    resource.Snapshot.Properties.Single(item => string.Equals(item.Name, "project.configuration", StringComparison.Ordinal)).Value,
                    CultureInfo.InvariantCulture);
                processConfiguration.ShouldBe(configuration);
                var expected = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", configuration, "net11.0", OperatingSystem.IsWindows() ? "YHAB.exe" : "YHAB");
                var executable = Convert.ToString(
                    resource.Snapshot.Properties.Single(item => string.Equals(item.Name, "executable.path", StringComparison.Ordinal)).Value,
                    CultureInfo.InvariantCulture).ShouldNotBeNull();
                string.Equals(executable, expected,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    .ShouldBeTrue("sample the actual Project v2 web executable");
                Process.TryGetProcessById(processId, out var process).ShouldBeTrue("the running Project v2 web process must exist");
                processes.Add(process);
                resources.Add(resource.ResourceId);
                if (processes.Count == 2)
                {
                    endpoints.Select(item => item.Port).Distinct().Count().ShouldBe(2);
                    return new(processes.ToArray(), resources.ToArray(), endpoints.ToArray());
                }
            }
            throw new InvalidOperationException("Two running YHAB web processes were not discovered.");
        }
        catch
        {
            foreach (var process in processes) { process.Dispose(); }
            throw;
        }
    }

    public void Capture(double seconds, Action<string>? write = null)
    {
        foreach (var process in processes)
        {
            process.Refresh();
            process.HasExited.ShouldBeFalse();
            var sample = new Sample(seconds, process.Id, process.PrivateMemorySize64, process.WorkingSet64, process.TotalProcessorTime.TotalSeconds);
            _samples.Add(sample);
            write?.Invoke($"Server at {seconds:F1}s, PID {sample.ProcessId}: private={sample.PrivateBytes:N0} B; working-set={sample.WorkingSet:N0} B; CPU={sample.CpuSeconds:F2}s.");
        }
    }

    public async Task SaveAsync(string path, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_samples, JsonSerializerOptions.Web), token);
    }

    public void Dispose() { foreach (var process in processes) { process.Dispose(); } }
    private sealed record Sample(double Seconds, int ProcessId, long PrivateBytes, long WorkingSet, double CpuSeconds);
}
