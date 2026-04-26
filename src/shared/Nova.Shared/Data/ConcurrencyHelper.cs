using System.Data;
using Dapper;

namespace Nova.Shared.Data;

/// <summary>
/// Helpers for optimistic concurrency using field-group version tokens.
/// See docs/concurrency-field-group-versioning.md for the full pattern.
/// </summary>
public static class ConcurrencyHelper
{
    /// <summary>
    /// Executes an UPDATE and returns <c>true</c> if exactly one row was affected,
    /// <c>false</c> if zero rows were affected (optimistic concurrency conflict).
    /// <para>
    /// The caller is responsible for writing SQL that includes a lock-token check,
    /// for example: <c>WHERE id = @Id AND lock_ver_booking = @ExpectedVersion</c>.
    /// The SQL must also increment the lock token: <c>lock_ver_booking = @NextVersion</c>.
    /// Use <see cref="NextVersion"/> to compute <c>@NextVersion</c> before building parameters.
    /// </para>
    /// </summary>
    public static async Task<bool> ExecuteWithConcurrencyCheckAsync(
        IDbConnection    connection,
        string           sql,
        object           parameters,
        IDbTransaction?  transaction = null)
    {
        int affected = await connection.ExecuteAsync(sql, parameters, transaction);
        return affected > 0;
    }

    /// <summary>
    /// Returns the next version value. Wraps from <see cref="int.MaxValue"/> back to 1
    /// so version columns never overflow. Zero is reserved as the initial value.
    /// </summary>
    public static int NextVersion(int current) => current == int.MaxValue ? 1 : current + 1;
}
