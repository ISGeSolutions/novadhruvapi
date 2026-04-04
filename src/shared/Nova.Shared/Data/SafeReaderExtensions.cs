using System.Data;

namespace Nova.Shared.Data;

/// <summary>Extension methods for <see cref="IDataReader"/> that return safe defaults instead of throwing on DBNull.</summary>
public static class SafeReaderExtensions
{
    /// <summary>Returns the string value of the named column, or <see cref="string.Empty"/> if DBNull.</summary>
    public static string GetStringSafe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    /// <summary>Returns the int value of the named column, or <c>0</c> if DBNull.</summary>
    public static int GetInt32Safe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    /// <summary>
    /// Returns the <see cref="DateTime"/> value of the named column, or <see cref="DateTime.MinValue"/> if DBNull.
    /// Use only for <c>DateOnly</c>-style columns (date with no time component) where no timezone conversion is needed.
    /// For UTC timestamp columns (<c>created_on</c>, <c>updated_on</c>) use <see cref="GetDateTimeOffsetSafe"/> instead.
    /// </summary>
    public static DateTime GetDateTimeSafe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? default : reader.GetDateTime(ordinal);
    }

    /// <summary>
    /// Returns the <see cref="DateTimeOffset"/> value of the named column as UTC,
    /// or <see cref="DateTimeOffset.MinValue"/> if DBNull.
    /// Use for all UTC timestamp columns (<c>created_on</c>, <c>updated_on</c>, <c>scheduled_on</c>, etc.).
    /// The value read from the DB is treated as UTC — <see cref="TimeSpan.Zero"/> offset is applied.
    /// </summary>
    public static DateTimeOffset GetDateTimeOffsetSafe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return default;

        // IDataReader.GetDateTime returns a DateTime — we wrap it as UTC DateTimeOffset.
        // MSSQL DATETIME2 and Postgres TIMESTAMPTZ both store UTC; MySqlConnector returns UTC DateTime.
        DateTime dt = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
    }

    /// <summary>Returns the bool value of the named column, or <c>false</c> if DBNull.</summary>
    public static bool GetBoolSafe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
    }

    /// <summary>Returns the Guid value of the named column, or <see cref="Guid.Empty"/> if DBNull.</summary>
    public static Guid GetGuidSafe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? Guid.Empty : reader.GetGuid(ordinal);
    }
}
