using Microsoft.Playwright;

namespace YHAB.PlaywrightTests;

internal static class BrowserArtifacts
{
    internal static async Task CaptureAsync(IPage page, IBrowserContext context, string directory, Action<string> writeOutput, bool captureTrace = true)
    {
        var screenshot = Path.Combine(directory, "page.png");
        var trace = Path.Combine(directory, "trace.zip");
        await TryCaptureAsync(screenshot, () => page.ScreenshotAsync(new() { Path = screenshot, FullPage = true }), writeOutput);
        if (captureTrace)
        {
            await TryCaptureAsync(trace, () => context.Tracing.StopAsync(new() { Path = trace }), writeOutput);
        }
    }

    private static async Task TryCaptureAsync(string path, Func<Task> capture, Action<string> writeOutput)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await capture();
        }
        catch (Exception exception) when (exception is PlaywrightException or IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
        {
            // Artifacts are best-effort diagnostics: never replace the test's failure
            // or prevent the other artifact from being attempted.
            writeOutput($"Failed to capture browser artifact '{path}': {exception}");
        }
    }
}
