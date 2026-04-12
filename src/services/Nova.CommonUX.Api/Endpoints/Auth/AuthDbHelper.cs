using Nova.Shared.Data;

namespace Nova.CommonUX.Api.Endpoints.Auth;

/// <summary>Shared helpers for auth endpoint DB access against the single <c>nova_auth</c> database.</summary>
internal static class AuthDbHelper
{
    /// <summary>Creates the SQL dialect for <paramref name="dbType"/>.</summary>
    internal static ISqlDialect Dialect(DbType dbType) => dbType switch
    {
        DbType.Postgres => new PostgresDialect(),
        DbType.MariaDb  => new MariaDbDialect(),
        _               => new MsSqlDialect()
    };

    /// <summary>UTC now as a <see cref="DateTimeOffset"/> for use in Dapper parameters.</summary>
    internal static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
}
