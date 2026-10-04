using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using Shouldly;

namespace YHAB.PlaywrightTests;

internal static class BrowserMemory
{
    // CDP cleanup deliberately ignores test cancellation; its waits have independent timeouts.
    private static readonly string[] _memoryCategories = ["disabled-by-default-memory-infra"];
    private static readonly string[] _excludedCategories = ["*"];
    public static async Task SnapshotAsync(IBrowserContext context, IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YHAB_HEAP_SNAPSHOTS");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        var session = await context.NewCDPSessionAsync(page);
        await using var writer = new StreamWriter(Path.Combine(directory, $"{name}.heapsnapshot"));
        var chunks = session.Event("HeapProfiler.addHeapSnapshotChunk");
        void WriteChunk(object? sender, JsonElement? value)
            => writer.Write(value!.Value.GetProperty("chunk").GetString());
        chunks.OnEvent += WriteChunk;
        try
        {
            await session.SendAsync("HeapProfiler.takeHeapSnapshot").WaitAsync(TimeSpan.FromSeconds(45), Xunit.TestContext.Current.CancellationToken);
            var native = await session.SendAsync("Memory.getAllTimeSamplingProfile").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{name}-native.json"), JsonSerializer.Serialize(native), Xunit.TestContext.Current.CancellationToken);
        }
        finally
        {
            chunks.OnEvent -= WriteChunk;
            await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        await DumpNativeAsync(context.Browser.ShouldNotBeNull(), Path.Combine(directory, $"{name}-allocators.json"));
    }

    private static async Task DumpNativeAsync(IBrowser browser, string path)
    {
        var session = await browser.NewBrowserCDPSessionAsync();
        var complete = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = session.Event("Tracing.tracingComplete");
        void Completed(object? sender, JsonElement? value) => complete.TrySetResult(value!.Value);
        events.OnEvent += Completed;
        try
        {
            // Only memory allocator statistics, without screenshots or continuous
            // event recording. Never purge caches or force a browser memory-pressure event.
            await session.SendAsync("Tracing.start", new(StringComparer.Ordinal)
            {
                ["transferMode"] = "ReturnAsStream",
                ["traceConfig"] = new { includedCategories = _memoryCategories, excludedCategories = _excludedCategories, traceBufferSizeInKb = 8192 },
            }).WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            try
            {
                var dump = (await session.SendAsync("Tracing.requestMemoryDump", new(StringComparer.Ordinal) { ["deterministic"] = false, ["levelOfDetail"] = "detailed" }).WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken)).ShouldNotBeNull();
                dump.GetProperty("success").GetBoolean().ShouldBeTrue();
            }
            finally
            {
                await session.SendAsync("Tracing.end").WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            var result = await complete.Task.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            result.GetProperty("dataLossOccurred").GetBoolean().ShouldBeFalse();
            var stream = result.GetProperty("stream").GetString().ShouldNotBeNull();
            try
            {
                await using var writer = new StreamWriter(path);
                while (true)
                {
                    var chunk = (await session.SendAsync("IO.read", new(StringComparer.Ordinal) { ["handle"] = stream }).WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken)).ShouldNotBeNull();
                    var data = chunk.GetProperty("data").GetString().ShouldNotBeNull();
                    await writer.WriteAsync(chunk.TryGetProperty("base64Encoded", out var encoded) && encoded.GetBoolean()
                        ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data)) : data);
                    if (chunk.GetProperty("eof").GetBoolean()) { break; }
                }
            }
            finally
            {
                await session.SendAsync("IO.close", new(StringComparer.Ordinal) { ["handle"] = stream }).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
        }
        finally
        {
            events.OnEvent -= Completed;
            await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }

    public static async Task<(long JsBytes, int DomNodes)> CaptureAsync(IBrowser browser, IBrowserContext context, IPage page, string label, Action<string> write)
    {
        var sample = (JsBytes: 0L, DomNodes: 0);
        long wasmBytes = 0;
        JsonElement heap;
        JsonElement dom;
        JsonElement firstCollectionDom;
        var renderer = await page.Locator(".workspace").GetAttributeAsync("data-renderer");
        var session = await context.NewCDPSessionAsync(page);
        try
        {
            await session.SendAsync("HeapProfiler.collectGarbage").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            firstCollectionDom = (await session.SendAsync("Memory.getDOMCounters").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken)).ShouldNotBeNull();
            // Weak cleanup can enqueue work for a later task. Sample after a
            // rendering opportunity and a second fixed collection, never an
            // unbounded collect-until-the-assertion-passes loop.
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => resolve()))").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            await session.SendAsync("HeapProfiler.collectGarbage").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            heap = (await session.SendAsync("Runtime.getHeapUsage").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken)).ShouldNotBeNull();
            dom = (await session.SendAsync("Memory.getDOMCounters").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken)).ShouldNotBeNull();
            sample = (heap.GetProperty("usedSize").GetInt64(), dom.GetProperty("nodes").GetInt32());
            // .NET's memory view is short-lived: read the length synchronously and never retain the view.
            wasmBytes = await page.EvaluateAsync<long>("getDotnetRuntime(0).localHeapViewU8().byteLength").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
            wasmBytes.ShouldBeGreaterThan(0);
            write($"{label}: renderer={renderer}; post-GC JS heap={heap.GetProperty("usedSize").GetInt64():N0} B; WASM linear capacity={wasmBytes:N0} B; DOM={dom.GetProperty("nodes").GetInt32():N0}; documents={dom.GetProperty("documents").GetInt32()}; listeners={dom.GetProperty("jsEventListeners").GetInt32()}. WASM capacity is not live managed-object usage.");
            write($"{label}: first-collection DOM={firstCollectionDom.GetProperty("nodes").GetInt32():N0}; documents={firstCollectionDom.GetProperty("documents").GetInt32()}; listeners={firstCollectionDom.GetProperty("jsEventListeners").GetInt32()}.");
        }
        finally
        {
            await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        var browserSession = await browser.NewBrowserCDPSessionAsync();
        try
        {
            var processes = (await browserSession.SendAsync("SystemInfo.getProcessInfo").WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken)).ShouldNotBeNull().GetProperty("processInfo");
            long privateBytes = 0, workingSet = 0;
            var sampled = 0;
            var exited = 0;
            var details = new List<ProcessSample>();
            foreach (var item in processes.EnumerateArray())
            {
                try
                {
                    using var process = Process.GetProcessById(checked((int)item.GetProperty("id").GetDouble()));
                    process.Refresh();
                    privateBytes += process.PrivateMemorySize64;
                    workingSet += process.WorkingSet64;
                    sampled++;
                    details.Add(new(process.Id, item.GetProperty("type").GetString().ShouldNotBeNull(), process.PrivateMemorySize64, process.WorkingSet64));
                }
                catch (ArgumentException) { exited++; }
                catch (InvalidOperationException) { exited++; }
            }
            sampled.ShouldBeGreaterThan(0);
            write($"{label}: CDP-reported Chromium processes={sampled}, exited before sample={exited}; aggregate private bytes={privateBytes:N0} B; working sets={workingSet:N0} B (shared pages may be counted more than once; processes omitted by CDP are outside this sample).");
            foreach (var detail in details)
            {
                write($"{label}: {detail.Type} PID {detail.Id}; private={detail.PrivateBytes:N0} B; working-set={detail.WorkingSet:N0} B.");
            }
            var path = Environment.GetEnvironmentVariable("YHAB_MEMORY_SAMPLES");
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var data = new { label, renderer, heap, dom, firstCollectionDom, wasmBytes, processes = details };
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(data, JsonSerializerOptions.Web) + Environment.NewLine, Xunit.TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            await browserSession.DetachAsync().WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        return sample;
    }

    private sealed record ProcessSample(int Id, string Type, long PrivateBytes, long WorkingSet);
}
