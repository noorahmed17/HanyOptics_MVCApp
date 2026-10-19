using System.Data;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;

// Plain ADO.NET reads over the views and tables that have no EF entity (expenses,
// suppliers, corrections_log, ...), on the DbContext's own connection so they share its
// connection string and any transaction it has open.
internal static class SqlReader
{
    public static async Task<T> WithConnectionAsync<T>(HanyOpticsDbContext dbContext, Func<SqlConnection, Task<T>> work)
    {
        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        var opened = false;
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                opened = true;
            }

            return await work(connection);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    public static SqlCommand Command(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return command;
    }

    public static async Task<List<T>> QueryAsync<T>(SqlConnection connection, string sql, Func<SqlDataReader, T> map, params SqlParameter[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
            rows.Add(map(reader));
        return rows;
    }

    public static string? NullableString(this SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetString(i);
    }

    public static DateTime? NullableDateTime(this SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetDateTime(i);
    }

    public static int? NullableInt(this SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetInt32(i);
    }

    public static decimal Decimal(this SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? 0m : reader.GetDecimal(i);
    }

    public static SqlParameter NVarChar(string name, string? value, int size) =>
        new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };

    public static SqlParameter Money(string name, decimal? value) =>
        new(name, SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = (object?)value ?? DBNull.Value };

    public static SqlParameter IntParam(string name, int? value) =>
        new(name, SqlDbType.Int) { Value = (object?)value ?? DBNull.Value };

    // Blank and whitespace-only text is "not given", so the database stores NULL rather
    // than an empty string that looks like a value.
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
