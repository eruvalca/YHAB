using YHAB.Build;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, args) =>
{
    args.Cancel = true;
#pragma warning disable MA0045 // The synchronous console event must finish cancellation callbacks before the token source can be disposed.
    cancellation.Cancel();
#pragma warning restore MA0045
};
Console.CancelKeyPress += cancel;
try
{
    if (args is ["patch-fluent", var source, var destination])
    {
        return await PatchFluentModule.RunAsync(source, destination, cancellation.Token);
    }

    if (args.Length != 5)
    {
        await Console.Error.WriteLineAsync("Expected: project-directory root-namespace define-constants components-manifest compile-manifest");
        return 2;
    }

    var validator = new ValidateRazorCodeBehind
    {
        ProjectDirectory = args[0],
        RootNamespace = args[1],
        DefineConstants = args[2],
        Components = (await File.ReadAllLinesAsync(args[3], cancellation.Token)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray(),
        CompileFiles = (await File.ReadAllLinesAsync(args[4], cancellation.Token)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray(),
    };

    return await validator.ExecuteAsync(cancellation.Token) ? 0 : 1;
}
finally
{
    Console.CancelKeyPress -= cancel;
}
