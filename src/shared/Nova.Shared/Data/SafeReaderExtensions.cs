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

    /// <summary>Returns the DateTime value of the named column, or <see cref="DateTime.MinValue"/> if DBNull.</summary>
    public static DateTime GetDateTimeSafe(this IDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? default : reader.GetDateTime(ordinal);
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
