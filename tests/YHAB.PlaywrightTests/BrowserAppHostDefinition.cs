using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace YHAB.PlaywrightTests;

// Each case starts a complete AppHost, PostgreSQL, migration tooling and Chromium.
// Running those stacks together can exhaust startup capacity and also contaminates
// the process-load and retained-memory measurements. Keep each case's resources
// independent, but give these browser scenarios exclusive use of the test runner.
[CollectionDefinition(DisableParallelization = true)]
[SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "xUnit requires public collection definitions for discovery.")]
public sealed class BrowserAppHostDefinition;
