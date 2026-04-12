using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using Nova.Shared.Configuration;
using Nova.Shared.Security;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Data;

/// <summary>
/// Resolves and opens database connections, decrypting connection strings via <see cref="ICipherService"/>.
/// When <c>opsettings.json → SqlLogging.Enabled</c> is <c>true</c>, wraps every connection in a
/// <see cref="LoggingDbConnection"/> so that Dapper queries are logged before execution.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly ICipherService        _cipherService;
    private readonly IOpsSettingsAccessor  _ops;
    private readonly ILogger               _logger;

    /// <summary>Initialises a new instance of <see cref="DbConnectionFactory"/>.</summary>
    public DbConnectionFactory(
        ICipherService        cipherService,
        IOpsSettingsAccessor  ops,
        ILogger<DbConnectionFactory> logger)
    {
        _cipherService = cipherService;
        _ops           = ops;
        _logger        = logger;
    }

    /// <inheritdoc />
    public IDbConnection CreateForTenant(TenantContext tenant)
    {
        string decryptedConnectionString = _cipherService.Decrypt(tenant.ConnectionString);
        return Wrap(OpenConnection(decryptedConnectionString, tenant.DbType));
    }

    /// <inheritdoc />
    public IDbConnection CreateFromConnectionString(string connectionString, DbType dbType)
    {
        string decryptedConnectionString = _cipherService.Decrypt(connectionString);
        return Wrap(OpenConnection(decryptedConnectionString, dbType));
    }

    /// <inheritdoc />
    public async Task<IDbConnection> CreateFromConnectionStringAsync(
        string connectionString,
        DbType dbType,
        CancellationToken cancellationToken = default)
    {
        string decryptedConnectionString = _cipherService.Decrypt(connectionString);
        return Wrap(await OpenConnectionAsync(decryptedConnectionString, dbType, cancellationToken));
    }

    /// <inheritdoc />
    public IDbConnection OpenRaw(string rawConnectionString, DbType dbType) =>
        Wrap(OpenConnection(rawConnectionString, dbType));

    /// <inheritdoc />
    public async Task<IDbConnection> OpenRawAsync(
        string rawConnectionString,
        DbType dbType,
        CancellationToken cancellationToken = default) =>
        Wrap(await OpenConnectionAsync(rawConnectionString, dbType, cancellationToken));

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private IDbConnection Wrap(DbConnection connection)
    {
        SqlLoggingSettings sqlLogging = _ops.Current.SqlLogging;
        return sqlLogging.Enabled
            ? new LoggingDbConnection(connection, _logger, sqlLogging)
            : connection;
    }

    private static DbConnection OpenConnection(string decryptedConnectionString, DbType dbType)
    {
        DbConnection connection = dbType switch
        {
            DbType.MsSql    => new SqlConnection(decryptedConnectionString),
            DbType.Postgres => new NpgsqlConnection(decryptedConnectionString),
            DbType.MariaDb  => new MySqlConnection(decryptedConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported DbType.")
        };
        connection.Open();
        return connection;
    }

    private static async Task<DbConnection> OpenConnectionAsync(
        string decryptedConnectionString,
        DbType dbType,
        CancellationToken cancellationToken)
    {
        DbConnection connection = dbType switch
        {
            DbType.MsSql    => new SqlConnection(decryptedConnectionString),
            DbType.Postgres => new NpgsqlConnection(decryptedConnectionString),
            DbType.MariaDb  => new MySqlConnection(decryptedConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported DbType.")
        };
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
