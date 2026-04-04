using System.Data;
using Nova.Shared.Tenancy;

namespace Nova.Shared.Data;

/// <summary>Creates database connections for tenants or explicit connection strings.</summary>
public interface IDbConnectionFactory
{
    /// <summary>Creates and opens a connection for the given tenant context.</summary>
    IDbConnection CreateForTenant(TenantContext tenant);

    /// <summary>Creates and opens a connection from an explicit connection string and db type.</summary>
    IDbConnection CreateFromConnectionString(string connectionString, DbType dbType);

    /// <summary>Creates and opens a connection asynchronously, honouring the cancellation token.</summary>
    Task<IDbConnection> CreateFromConnectionStringAsync(string connectionString, DbType dbType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and opens a connection from a <em>raw</em> (already-decrypted) connection string.
    /// Use this when the connection string has already been decrypted by the caller —
    /// unlike <see cref="CreateFromConnectionString"/>, this method does not attempt decryption.
    /// </summary>
    IDbConnection OpenRaw(string rawConnectionString, DbType dbType);

    /// <inheritdoc cref="OpenRaw"/>
    Task<IDbConnection> OpenRawAsync(string rawConnectionString, DbType dbType, CancellationToken cancellationToken = default);
}
