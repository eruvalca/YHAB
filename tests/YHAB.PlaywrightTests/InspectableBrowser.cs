using System.Diagnostics;
using Microsoft.Playwright;

namespace YHAB.PlaywrightTests;

/// <summary>Owns a disposable browser independently of its diagnostic connection.</summary>
internal sealed class InspectableBrowser : IAsyncDisposable
{
    private static readonly string[] _inspectionArguments = ["--enable-automation"];
    private readonly Process _process = new() { EnableRaisingEvents = true };
    private readonly string _profile = Directory.CreateTempSubdirectory("YHAB-browser-").FullName;
    private IBrowser? _browser;
    private string? _endpoint;
    private bool _started;
    public IBrowser Browser => _browser ?? throw new InvalidOperationException("The diagnostic connection is not open.");

    public async Task StartAsync(IPlaywright playwright, CancellationToken token)
    {
        var arguments = await ReferenceArgumentsAsync(playwright);
        var start = new ProcessStartInfo(arguments[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
        };
        // Use the same pinned executable and defaults as the normal browser
        // suite. Only transport, owned profile and initial page differ.
        foreach (var argument in arguments.Skip(1).Where(argument => !argument.StartsWith("--user-data-dir=", StringComparison.Ordinal)
            && argument is not ("--remote-debugging-pipe" or "--no-startup-window" or "--enable-automation")))
        {
            start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add("--remote-debugging-port=0");
        start.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        start.ArgumentList.Add($"--user-data-dir={_profile}");
        start.ArgumentList.Add("--ignore-certificate-errors");
        start.ArgumentList.Add("about:blank");
        _process.StartInfo = start;
        var endpoint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnError(object sender, DataReceivedEventArgs args)
        {
            const string Prefix = "DevTools listening on ";
            if (args.Data?.StartsWith(Prefix, StringComparison.Ordinal) == true
                && Uri.TryCreate(args.Data[Prefix.Length..], UriKind.Absolute, out var uri) && uri.IsLoopback)
            {
                endpoint.TrySetResult(uri.AbsoluteUri);
            }
        }
        void OnExit(object? sender, EventArgs args)
            => endpoint.TrySetException(new InvalidOperationException("Chromium exited before its diagnostic endpoint was ready."));
        _process.ErrorDataReceived += OnError;
        _process.Exited += OnExit;
        try
        {
            _started = _process.Start();
            if (!_started) { throw new InvalidOperationException("Chromium did not start."); }
            _process.BeginErrorReadLine();
            _endpoint = await endpoint.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            _browser = await playwright.Chromium.ConnectOverCDPAsync(_endpoint, new() { NoDefaults = true, Timeout = 30000 });
        }
        finally
        {
            _process.ErrorDataReceived -= OnError;
            _process.Exited -= OnExit;
        }
    }

    private static async Task<string[]> ReferenceArgumentsAsync(IPlaywright playwright)
    {
        // Chromium requires this temporary flag to reveal its arguments. The
        // reference browser stays blank and is disposed before the control starts.
        await using var reference = await playwright.Chromium.LaunchAsync(new() { Args = _inspectionArguments });
        var session = await reference.NewBrowserCDPSessionAsync();
        try
        {
            var result = await session.SendAsync("Browser.getBrowserCommandLine").WaitAsync(TimeSpan.FromSeconds(30));
            return result!.Value.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray();
        }
        finally { await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
    }

    public async Task ReconnectAsync(IPlaywright playwright)
    {
        // Closing a CDP connection preserves the browser's default context.
        // The test proves document/runtime identity survived; no page reload,
        // cache clearing, memory-pressure signal or app-state reset is involved.
        await Browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(30));
        _browser = await playwright.Chromium.ConnectOverCDPAsync(_endpoint!, new() { NoDefaults = true, Timeout = 30000 });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_browser?.IsConnected == true) { await _browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        }
        finally
        {
            await StopAsync();
        }
    }

    private async Task StopAsync()
    {
        if (_started && !_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        _process.Dispose();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Directory.GetParent(_profile)?.FullName, Path.TrimEndingDirectorySeparator(Path.GetTempPath()), comparison)
            || !Path.GetFileName(_profile).StartsWith("YHAB-browser-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to remove a browser profile outside the owned temporary directory.");
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { Directory.Delete(_profile, recursive: true); break; }
            catch (IOException) when (attempt < 4) { await Task.Delay(100); }
        }
    }
}
