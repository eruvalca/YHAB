using YHAB.Build;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    if (args is ["patch-fluent", var source, var observers, var destination])
    {
        return await PatchFluentModule.RunAsync(source, observers, destination, cancellation.Token);
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

    return validator.Execute(cancellation.Token) ? 0 : 1;
}
finally
{
    Console.CancelKeyPress -= cancel;
}
