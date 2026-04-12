using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Nova.Shared.Configuration;

namespace Nova.Shared.Data;

/// <summary>
/// Wraps a <see cref="DbConnection"/> and returns a <see cref="LoggingDbCommand"/> from
/// <see cref="CreateDbCommand"/>, so that every Dapper query is logged before execution.
/// Activated by <see cref="DbConnectionFactory"/> when <c>opsettings.json → SqlLogging.Enabled</c>
/// is <c>true</c>.
/// </summary>
internal sealed class LoggingDbConnection : DbConnection
{
    private readonly DbConnection      _inner;
    private readonly ILogger           _logger;
    private readonly SqlLoggingSettings _settings;

    internal LoggingDbConnection(DbConnection inner, ILogger logger, SqlLoggingSettings settings)
    {
        _inner    = inner;
        _logger   = logger;
        _settings = settings;
    }

    // -------------------------------------------------------------------------
    // CreateDbCommand — the only method Dapper calls to build queries
    // -------------------------------------------------------------------------

    protected override DbCommand CreateDbCommand() =>
        new LoggingDbCommand(_inner.CreateCommand(), _logger, _settings);

    // -------------------------------------------------------------------------
    // DbConnection abstract members — delegate to inner
    // -------------------------------------------------------------------------

#pragma warning disable CS8765 // BCL DbConnection.ConnectionString setter carries [AllowNull]
    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value!;
    }
#pragma warning restore CS8765

    public override int             ConnectionTimeout => _inner.ConnectionTimeout;
    public override string          Database          => _inner.Database;
    public override string          DataSource        => _inner.DataSource;
    public override string          ServerVersion     => _inner.ServerVersion;
    public override ConnectionState State             => _inner.State;

    public override void Open()  => _inner.Open();
    public override void Close() => _inner.Close();

    public override Task OpenAsync(CancellationToken cancellationToken) =>
        _inner.OpenAsync(cancellationToken);

    public override void ChangeDatabase(string databaseName) =>
        _inner.ChangeDatabase(databaseName);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        _inner.BeginTransaction(isolationLevel);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
