using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Nova.Shared.Security;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Data;

/// <summary>
/// Resolves and opens database connections, decrypting connection strings via <see cref="ICipherService"/>.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly ICipherService _cipherService;

    /// <summary>Initialises a new instance of <see cref="DbConnectionFactory"/>.</summary>
    public DbConnectionFactory(ICipherService cipherService)
    {
        _cipherService = cipherService;
    }

    /// <inheritdoc />
    public IDbConnection CreateForTenant(TenantContext tenant)
    {
        string decryptedConnectionString = _cipherService.Decrypt(tenant.ConnectionString);
        return OpenConnection(decryptedConnectionString, tenant.DbType);
    }

    /// <inheritdoc />
    public IDbConnection CreateFromConnectionString(string connectionString, DbType dbType)
    {
        string decryptedConnectionString = _cipherService.Decrypt(connectionString);
        return OpenConnection(decryptedConnectionString, dbType);
    }

    /// <inheritdoc />
    public async Task<IDbConnection> CreateFromConnectionStringAsync(
        string connectionString,
        DbType dbType,
        CancellationToken cancellationToken = default)
    {
        string decryptedConnectionString = _cipherService.Decrypt(connectionString);
        return await OpenConnectionAsync(decryptedConnectionString, dbType, cancellationToken);
    }

    private static IDbConnection OpenConnection(string decryptedConnectionString, DbType dbType)
    {
        IDbConnection connection = dbType switch
        {
            DbType.MsSql    => new SqlConnection(decryptedConnectionString),
            DbType.Postgres => new NpgsqlConnection(decryptedConnectionString),
            DbType.MariaDb  => new MySqlConnection(decryptedConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported DbType.")
        };
        connection.Open();
        return connection;
    }

    private static async Task<IDbConnection> OpenConnectionAsync(
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
