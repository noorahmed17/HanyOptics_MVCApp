using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

public class ReportService : IReportService
{
    // A report is meant to be read on screen. Past a few hundred rows nobody is reading it
    // any more, and rendering thousands of rows only makes the page slow, so the query is
    // capped and the screen says when the cap was hit rather than pretending it wasn't.
    private const int RowLimit = 1000;

    private readonly HanyOpticsDbContext _dbContext;
    private readonly ILogger<ReportService> _logger;

    public ReportService(HanyOpticsDbContext dbContext, ILogger<ReportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IReadOnlyList<ReportDefinition> Catalog => ReportCatalog.All;

    public ReportDefinition? Find(string? key) => ReportCatalog.Find(key);

    public async Task<ReportResult> RunAsync(ReportDefinition definition, DateOnly? from, DateOnly? to)
    {
        if (string.IsNullOrWhiteSpace(definition.Sql))
            throw new InvalidOperationException($"Report '{definition.Key}' has no query.");

        // A report that describes right now - stock on hand, money currently owed - has no
        // period to filter by, so any dates the URL carried are dropped rather than
        // silently narrowing an answer that is supposed to be complete.
        if (!definition.SupportsDateRange)
        {
            from = null;
            to = null;
        }
        else if (from.HasValue && to.HasValue && from > to)
        {
            // Swap rather than return nothing: a reversed range is a slip, and an empty
            // table would look like "no sales that week" instead of "you typed it backwards".
            (from, to) = (to, from);
        }

        var rows = new List<object?[]>();
        var truncated = false;

        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                opened = true;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = definition.Sql;
            command.CommandTimeout = 60;
            command.Parameters.Add(new SqlParameter("@from", SqlDbType.DateTime2)
            {
                Value = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
            });
            command.Parameters.Add(new SqlParameter("@to", SqlDbType.DateTime2)
            {
                Value = to.HasValue ? to.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
            });

            await using var reader = await command.ExecuteReaderAsync();

            // Resolve each declared column to its position once, rather than per row. A
            // column the query stopped returning is a bug in the catalogue, not something
            // to paper over at runtime - so it throws here rather than rendering blanks.
            var ordinals = definition.Columns.Select(c => reader.GetOrdinal(c.Key)).ToArray();

            while (await reader.ReadAsync())
            {
                if (rows.Count == RowLimit)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[ordinals.Length];
                for (var i = 0; i < ordinals.Length; i++)
                    row[i] = reader.IsDBNull(ordinals[i]) ? null : reader.GetValue(ordinals[i]);

                rows.Add(row);
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        _logger.LogInformation(
            "Report {ReportKey} ran for {From}..{To}, {RowCount} rows{Truncated}.",
            definition.Key, from, to, rows.Count, truncated ? " (capped)" : string.Empty);

        return new ReportResult
        {
            Definition = definition,
            From = from,
            To = to,
            Rows = rows,
            Kpis = ComputeKpis(definition, rows),
            Truncated = truncated,
            RowLimit = RowLimit
        };
    }

    // Computed from the rows just returned rather than by a second aggregate query, so the
    // headline figures can never disagree with the table printed underneath them.
    private static List<ReportKpi> ComputeKpis(ReportDefinition definition, List<object?[]> rows)
    {
        var kpis = new List<ReportKpi>(definition.Kpis.Count);

        foreach (var kpi in definition.Kpis)
        {
            decimal value;

            if (kpi.Column is null)
            {
                value = rows.Count;
            }
            else
            {
                var index = IndexOfColumn(definition, kpi.Column);
                var values = rows
                    .Select(r => r[index])
                    .Where(v => v is not null)
                    .Select(Convert.ToDecimal)
                    .ToList();

                value = kpi.Aggregate switch
                {
                    ReportAggregate.Count => values.Count,
                    // Average over nothing is not zero, it is undefined - reporting 0 ج as
                    // an average would read as a real figure.
                    ReportAggregate.Average => values.Count == 0 ? 0m : values.Sum() / values.Count,
                    _ => values.Sum()
                };
            }

            kpis.Add(new ReportKpi
            {
                Label = kpi.Label,
                Value = decimal.Round(value, 2),
                Format = kpi.Format
            });
        }

        return kpis;
    }

    private static int IndexOfColumn(ReportDefinition definition, string key)
    {
        for (var i = 0; i < definition.Columns.Count; i++)
        {
            if (string.Equals(definition.Columns[i].Key, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new InvalidOperationException(
            $"Report '{definition.Key}' has a KPI on column '{key}', which it does not select.");
    }
}
