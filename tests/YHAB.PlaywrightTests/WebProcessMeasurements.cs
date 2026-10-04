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
        var children = new List<Process>();
        var resources = new List<string>();
        var endpoints = new List<Uri>();
        try
        {
            await foreach (var resource in app.ResourceNotifications.WatchAsync(token))
            {
                if (!string.Equals(resource.Resource.Name, "yhab", StringComparison.Ordinal) || resources.Contains(resource.ResourceId, StringComparer.Ordinal)) { continue; }
                var pid = resource.Snapshot.Properties.FirstOrDefault(item => string.Equals(item.Name, "executable.pid", StringComparison.Ordinal));
                if (pid?.Value is null) { continue; }
                var parent = Convert.ToInt32(pid.Value, CultureInfo.InvariantCulture);
                if (parent <= 0) { continue; }
                var urls = resource.Snapshot.EnvironmentVariables.FirstOrDefault(item => string.Equals(item.Name, "ASPNETCORE_URLS", StringComparison.Ordinal))?.Value;
                if (string.IsNullOrEmpty(urls)) { continue; }
                var endpoint = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => new Uri(value))
                    .Single(value => string.Equals(value.Scheme, "https", StringComparison.Ordinal));
                (endpoint.IsLoopback || endpoint.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                    .ShouldBeTrue("only connect to this disposable AppHost's local replicas");
                // Identity cookies belong to localhost, independently of the port.
                endpoints.Add(new UriBuilder(endpoint) { Host = "localhost" }.Uri);
                var arguments = JsonSerializer.SerializeToElement(resource.Snapshot.Properties.Single(item => string.Equals(item.Name, "executable.args", StringComparison.Ordinal)).Value);
                arguments.EnumerateArray().Select(item => item.GetString()).ShouldContain(value => string.Equals(value, configuration, StringComparison.Ordinal));
                var ids = await ChildIdsAsync(parent, token);
                var expected = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", configuration, "net10.0", OperatingSystem.IsWindows() ? "YHAB.exe" : "YHAB");
                var matches = new List<int>();
                foreach (var id in ids)
                {
                    using var child = Process.GetProcessById(id);
                    if (string.Equals(child.MainModule!.FileName, expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { matches.Add(id); }
                }
                matches.Count.ShouldBe(1, "sample the actual web executable, not the dotnet launcher");
                children.Add(Process.GetProcessById(matches[0]));
                resources.Add(resource.ResourceId);
                if (children.Count == 2)
                {
                    endpoints.Select(item => item.Port).Distinct().Count().ShouldBe(2);
                    return new(children.ToArray(), resources.ToArray(), endpoints.ToArray());
                }
            }
            throw new InvalidOperationException("Two running YHAB web processes were not discovered.");
        }
        catch
        {
            foreach (var child in children) { child.Dispose(); }
            throw;
        }
    }

    private static async Task<int[]> ChildIdsAsync(int parent, CancellationToken token)
    {
        string text;
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add($"Get-CimInstance Win32_Process -Filter 'ParentProcessId={parent.ToString(CultureInfo.InvariantCulture)}' | Select-Object -ExpandProperty ProcessId");
            using var query = Process.Start(start).ShouldNotBeNull();
            text = await query.StandardOutput.ReadToEndAsync(token);
            var error = await query.StandardError.ReadToEndAsync(token);
            await query.WaitForExitAsync(token);
            query.ExitCode.ShouldBe(0, error);
        }
        else if (OperatingSystem.IsLinux())
        {
            text = await File.ReadAllTextAsync($"/proc/{parent}/task/{parent}/children", token);
        }
        else { throw new PlatformNotSupportedException("The process-memory probe currently supports Windows and Linux."); }
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
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
