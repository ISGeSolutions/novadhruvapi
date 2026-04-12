using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Logging;
using Nova.Shared.Configuration;

namespace Nova.Shared.Data;

/// <summary>
/// Wraps a <see cref="DbCommand"/> and logs the SQL text and parameters before execution.
/// Created by <see cref="LoggingDbConnection"/> when SQL logging is enabled in <c>opsettings.json</c>.
/// </summary>
internal sealed class LoggingDbCommand : DbCommand
{
    private readonly DbCommand           _inner;
    private readonly ILogger             _logger;
    private readonly SqlLoggingSettings  _settings;

    internal LoggingDbCommand(DbCommand inner, ILogger logger, SqlLoggingSettings settings)
    {
        _inner    = inner;
        _logger   = logger;
        _settings = settings;
    }

    // -------------------------------------------------------------------------
    // DbCommand abstract members — delegate to inner, log before execution
    // -------------------------------------------------------------------------

#pragma warning disable CS8765 // BCL DbCommand.CommandText setter carries [AllowNull]
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value!;
    }
#pragma warning restore CS8765

    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    protected override DbConnection? DbConnection
    {
        get => _inner.Connection;
        set => _inner.Connection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value;
    }

    public override void Cancel() => _inner.Cancel();

    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    public override void Prepare() => _inner.Prepare();

    // -------------------------------------------------------------------------
    // Execution — log then delegate (sync)
    // -------------------------------------------------------------------------

    public override int ExecuteNonQuery()
    {
        Log();
        return _inner.ExecuteNonQuery();
    }

    public override object? ExecuteScalar()
    {
        Log();
        return _inner.ExecuteScalar();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behaviour)
    {
        Log();
        return _inner.ExecuteReader(behaviour);
    }

    // -------------------------------------------------------------------------
    // Execution — log then delegate (async)
    // -------------------------------------------------------------------------

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        Log();
        return _inner.ExecuteNonQueryAsync(cancellationToken);
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        Log();
        return _inner.ExecuteScalarAsync(cancellationToken);
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behaviour,
        CancellationToken cancellationToken)
    {
        Log();
        return _inner.ExecuteReaderAsync(behaviour, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    private void Log()
    {
        if (_settings.IncludeParameters && Parameters.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (DbParameter p in Parameters)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p.ParameterName).Append('=').Append(p.Value ?? "NULL");
            }

            WriteLog("[SQL] {CommandText} | Params: {Parameters}", CommandText, sb.ToString());
        }
        else
        {
            WriteLog("[SQL] {CommandText}", CommandText);
        }
    }

    private void WriteLog(string template, params object?[] args)
    {
        switch (_settings.LogLevel)
        {
            case "Verbose":
            case "Debug":
                _logger.LogDebug(template, args);
                break;
            case "Information":
                _logger.LogInformation(template, args);
                break;
            case "Warning":
                _logger.LogWarning(template, args);
                break;
            default:
                _logger.LogDebug(template, args);
                break;
        }
    }
}
