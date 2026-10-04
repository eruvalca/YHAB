using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace YHAB.IntegrationTests;

/// <summary>Descriptive measurements only; process allocation totals include runtime background work.</summary>
internal sealed class DatabaseProbe(bool captureQueries = false) : DbCommandInterceptor, IMaterializationInterceptor
{
    private static readonly EventSource _measurements = new("YHAB-Tests-Measurements");
    private readonly Stopwatch _timer = new();
    private long _allocated;
    private TimeSpan _databaseTime;
    public List<string> Commands { get; } = [];
    public List<QuerySample> Queries { get; } = [];
    public Dictionary<string, int> Materialized { get; } = new(StringComparer.Ordinal);
    public bool Recording { get; private set; }
    // EF's materialization interception creates property-access delegates for
    // every entity. Keep a separate command-only control for realistic timings.
    public DbCommandInterceptor CommandsOnly() => new CommandInterceptor(this);
    public void Start()
    {
        Commands.Clear();
        Queries.Clear();
        Materialized.Clear();
        _databaseTime = TimeSpan.Zero;
        _allocated = GC.GetTotalAllocatedBytes(precise: true);
        Recording = true;
        _timer.Restart();
        _measurements.Write("MeasurementBegin");
    }
    public string Stop()
    {
        _timer.Stop();
        _measurements.Write("MeasurementEnd");
        Recording = false;
        using var process = Process.GetCurrentProcess();
        return $"{_timer.Elapsed.TotalMilliseconds:F1} ms elapsed; {Commands.Count} SQL commands / {_databaseTime.TotalMilliseconds:F1} ms execution; {GC.GetTotalAllocatedBytes(precise: true) - _allocated:N0} process bytes allocated; process private={process.PrivateMemorySize64:N0} B, working-set={process.WorkingSet64:N0} B; entities [{string.Join(", ", Materialized.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))}]";
    }
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (Recording)
        {
            var name = entity.GetType().Name;
            Materialized[name] = Materialized.GetValueOrDefault(name) + 1;
        }
        return entity;
    }
    private void Record(DbCommand command, CommandExecutedEventData data)
    {
        if (!Recording) { return; }
        Commands.Add(command.CommandText);
        _databaseTime += data.Duration;
        if (captureQueries && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
        {
            Queries.Add(new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(item => item.Clone()).ToArray(), data.Duration));
        }
    }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    internal sealed record QuerySample(string Sql, NpgsqlParameter[] Parameters, TimeSpan Duration);

    private sealed class CommandInterceptor(DatabaseProbe probe) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
            => probe.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
            => probe.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
            => probe.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }
}
