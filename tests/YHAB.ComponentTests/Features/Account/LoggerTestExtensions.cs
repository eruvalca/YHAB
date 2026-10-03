using Microsoft.Extensions.Logging;
using NSubstitute;

namespace YHAB.ComponentTests.Features.Account;

internal static class LoggerTestExtensions
{
    extension(ILogger logger)
    {
        internal IEnumerable<int> GetLoggedEventIds() => logger.ReceivedCalls()
            .Where(call => string.Equals(call.GetMethodInfo().Name, "Log", StringComparison.Ordinal))
            .Select(call => ((EventId)call.GetArguments()[1]!).Id);
    }
}
