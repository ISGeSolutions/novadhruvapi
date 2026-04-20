using System.Data;
using Dapper;

namespace Nova.Shared.Data;

/// <summary>
/// Registers Dapper type handlers for types not natively supported by all three dialects.
///
/// Call <see cref="RegisterDateOnly"/> at service startup regardless of dialect.
/// Call <see cref="RegisterDateTimeOffset"/> only for MSSQL and MariaDB services —
/// Npgsql maps timestamptz ↔ DateTimeOffset natively and must not have this handler registered.
/// </summary>
public static class DapperTypeHandlers
{
    /// <summary>
    /// Registers <see cref="DateOnly"/> support for all three dialects.
    /// Npgsql (v7+) maps <c>date</c> to <see cref="DateOnly"/> natively; the handler is safe
    /// to register but may not be invoked for Postgres connections.
    /// </summary>
    public static void RegisterDateOnly() =>
        SqlMapper.AddTypeHandler(new DateOnlyHandler());

    /// <summary>
    /// Registers <see cref="DateTimeOffset"/> support for MSSQL and MariaDB.
    /// MSSQL <c>datetime2</c> / <c>datetime</c> and MariaDB <c>datetime</c> map to
    /// <see cref="System.DateTime"/> in Dapper; this handler converts to/from
    /// <see cref="DateTimeOffset"/> (UTC) so that C# record types can use a single shared type.
    ///
    /// Do NOT call this for Postgres services — Npgsql returns <see cref="DateTimeOffset"/>
    /// natively for <c>timestamptz</c> and registering this handler would override it.
    /// </summary>
    public static void RegisterDateTimeOffset() =>
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());

    // ----------------------------------------------------------------
    // Handlers
    // ----------------------------------------------------------------

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) =>
            value switch
            {
                DateOnly d  => d,
                DateTime dt => DateOnly.FromDateTime(dt),
                _           => DateOnly.Parse(value.ToString()!)
            };

        public override void SetValue(IDbDataParameter parameter, DateOnly value) =>
            parameter.Value = value.ToString("yyyy-MM-dd");
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) =>
            value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt        => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _                  => DateTimeOffset.Parse(value.ToString()!)
            };

        // Write DateTime (UTC) into datetime2/datetime columns — accepted by both MSSQL and MariaDB.
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.UtcDateTime;
    }
}
