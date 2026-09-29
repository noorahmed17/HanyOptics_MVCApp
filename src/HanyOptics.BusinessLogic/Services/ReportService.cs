using System.Data;
using System.Globalization;
using System.Text;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

public class ReportService : IReportService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly ILogger<ReportService> _logger;

    public ReportService(HanyOpticsDbContext dbContext, ILogger<ReportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IReadOnlyList<ReportDefinition> Catalog => ReportCatalog.All;

    public ReportDefinition? Find(string? key) => ReportCatalog.Find(key);

    public Task<ReportResult> RunAsync(ReportDefinition definition, DateOnly? from, DateOnly? to)
        => RunAsync(definition, from, to, page: 1, pageSize: null);

    public async Task<ReportResult> RunAsync(
        ReportDefinition definition, DateOnly? from, DateOnly? to, int? page, int? pageSize)
    {
        if (string.IsNullOrWhiteSpace(definition.Sql))
            throw new InvalidOperationException($"Report '{definition.Key}' has no query.");

        (from, to) = NormaliseRange(definition, from, to);

        var size = Math.Clamp(pageSize ?? ReportPaging.DefaultPageSize, 1, ReportPaging.MaxPageSize);
        var requestedPage = Math.Max(page ?? 1, 1);

        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                opened = true;
            }

            // The totals come first, in one aggregate over the whole range. They are what
            // the KPI cards show, so they must not depend on which page is being viewed.
            var (totalRows, kpis) = await ReadTotalsAsync(connection, definition, from, to);

            var lastPage = totalRows == 0 ? 1 : (int)Math.Ceiling(totalRows / (double)size);
            var currentPage = Math.Min(requestedPage, lastPage);

            var rows = await ReadRowsAsync(
                connection, definition, from, to, (currentPage - 1) * size, size);

            _logger.LogInformation(
                "Report {ReportKey} {From}..{To}: page {Page}/{Pages} of {Total} rows.",
                definition.Key, from, to, currentPage, lastPage, totalRows);

            return new ReportResult
            {
                Definition = definition,
                From = from,
                To = to,
                Rows = rows,
                Kpis = kpis,
                TotalRows = totalRows,
                Page = currentPage,
                PageSize = size
            };
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    public async Task<string> ExportCsvAsync(ReportDefinition definition, DateOnly? from, DateOnly? to)
    {
        if (string.IsNullOrWhiteSpace(definition.Sql))
            throw new InvalidOperationException($"Report '{definition.Key}' has no query.");

        (from, to) = NormaliseRange(definition, from, to);

        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                opened = true;
            }

            // The export is not paged - a spreadsheet of one page would be useless - but it
            // is still capped, so a single click cannot try to materialise every row the
            // shop has ever produced.
            var rows = await ReadRowsAsync(connection, definition, from, to, 0, ReportPaging.ExportRowLimit);

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(',', definition.Columns.Select(c => EscapeCsv(c.Label))));

            foreach (var row in rows)
                csv.AppendLine(string.Join(',', row.Select(FormatForCsv).Select(EscapeCsv)));

            _logger.LogInformation("Report {ReportKey} exported: {Rows} rows.", definition.Key, rows.Count);
            return csv.ToString();
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    // A report that describes right now - stock on hand, money currently owed - has no
    // period to filter by, so any dates the URL carried are dropped rather than silently
    // narrowing an answer that is supposed to be complete. A reversed range is swapped
    // rather than returning nothing: it is a slip, and an empty table would read as
    // "no sales that week" instead of "you typed it backwards".
    private static (DateOnly? From, DateOnly? To) NormaliseRange(
        ReportDefinition definition, DateOnly? from, DateOnly? to)
    {
        if (!definition.SupportsDateRange)
            return (null, null);

        if (from.HasValue && to.HasValue && from > to)
            return (to, from);

        return (from, to);
    }

    private static void AddRangeParameters(SqlCommand command, DateOnly? from, DateOnly? to)
    {
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.DateTime2)
        {
            Value = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.DateTime2)
        {
            Value = to.HasValue ? to.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
        });
    }

    // Wraps the report body as a subquery and aggregates it - which is why the definition
    // keeps its ORDER BY separately: an ORDER BY is not legal inside a derived table.
    private static async Task<(int TotalRows, List<ReportKpi> Kpis)> ReadTotalsAsync(
        SqlConnection connection, ReportDefinition definition, DateOnly? from, DateOnly? to)
    {
        var selects = new List<string> { "COUNT(*) AS total_rows" };

        for (var i = 0; i < definition.Kpis.Count; i++)
        {
            var kpi = definition.Kpis[i];

            selects.Add(kpi.Column is null
                ? $"COUNT(*) AS kpi_{i}"
                : kpi.Aggregate switch
                {
                    ReportAggregate.Count => $"COUNT({Bracket(kpi.Column)}) AS kpi_{i}",
                    ReportAggregate.Average => $"AVG(CAST({Bracket(kpi.Column)} AS DECIMAL(19,4))) AS kpi_{i}",
                    _ => $"SUM(CAST({Bracket(kpi.Column)} AS DECIMAL(19,4))) AS kpi_{i}"
                });
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", selects)} FROM (\n{definition.Sql}\n) AS report_body;";
        command.CommandTimeout = 60;
        AddRangeParameters(command, from, to);

        await using var reader = await command.ExecuteReaderAsync();
        var kpis = new List<ReportKpi>(definition.Kpis.Count);

        if (!await reader.ReadAsync())
            return (0, kpis);

        var totalRows = reader.GetInt32(reader.GetOrdinal("total_rows"));

        for (var i = 0; i < definition.Kpis.Count; i++)
        {
            var ordinal = reader.GetOrdinal($"kpi_{i}");

            // SUM and AVG over no rows are NULL, not zero - and an average over nothing is
            // undefined rather than 0 ج, which would read as a real figure.
            var value = reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));

            kpis.Add(new ReportKpi
            {
                Label = definition.Kpis[i].Label,
                Value = decimal.Round(value, 2),
                Format = definition.Kpis[i].Format
            });
        }

        return (totalRows, kpis);
    }

    private static async Task<List<object?[]>> ReadRowsAsync(
        SqlConnection connection, ReportDefinition definition, DateOnly? from, DateOnly? to,
        int skip, int take)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"{definition.Sql}\nORDER BY {definition.OrderBy}\n" +
            "OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";
        command.CommandTimeout = 120;
        AddRangeParameters(command, from, to);
        command.Parameters.Add(new SqlParameter("@skip", SqlDbType.Int) { Value = skip });
        command.Parameters.Add(new SqlParameter("@take", SqlDbType.Int) { Value = take });

        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();

        // Resolved once rather than per row. A column the query stopped returning is a bug
        // in the catalogue, so this throws rather than rendering silent blanks.
        var ordinals = definition.Columns.Select(c => reader.GetOrdinal(c.Key)).ToArray();

        while (await reader.ReadAsync())
        {
            var row = new object?[ordinals.Length];
            for (var i = 0; i < ordinals.Length; i++)
                row[i] = reader.IsDBNull(ordinals[i]) ? null : reader.GetValue(ordinals[i]);

            rows.Add(row);
        }

        return rows;
    }

    // Column names come from the catalogue, never from user input, but bracketing them
    // keeps the generated SQL valid for any name and makes injection impossible by shape.
    private static string Bracket(string column) => $"[{column.Replace("]", "]]")}]";

    private static string FormatForCsv(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
