namespace HanyOptics.BusinessLogic.Models;

// How a column should be read, which decides how the screen formats it. The database gives
// back a decimal for both a price and a count; only the report knows which one it meant.
public enum ReportColumnType
{
    Text,
    Number,
    Money,
    Date,
    DateTime
}

public class ReportColumn
{
    public required string Key { get; init; }      // the column name as the query returns it
    public required string Label { get; init; }    // the Arabic heading
    public ReportColumnType Type { get; init; } = ReportColumnType.Text;

    // Money that can legitimately be negative - a profit line, a net-of-refunds total -
    // gets coloured red when it goes below zero. A plain price never should, so it stays
    // black and a minus sign there reads as a data problem rather than a loss.
    public bool SignedMoney { get; init; }
}

public enum ReportAggregate { Count, Sum, Average }

// A headline number above the table. Declared rather than hand-written per report so it is
// always computed from the same rows the table shows - a separate aggregate query could
// land either side of someone else's sale and quietly disagree with the list under it.
public class ReportKpiDefinition
{
    public required string Label { get; init; }
    public string? Column { get; init; }           // null with Count = number of rows
    public ReportAggregate Aggregate { get; init; } = ReportAggregate.Sum;
    public ReportColumnType Format { get; init; } = ReportColumnType.Money;
}

// Which reports belong together on the index page.
public enum ReportGroup
{
    Money,
    Receivables,
    Stock,
    Operations
}

public class ReportDefinition
{
    public required string Key { get; init; }          // the URL slug
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public ReportGroup Group { get; init; }

    // Whether the من/إلى filter does anything here. A stock report describes this moment,
    // not a period, so offering it a date range would only invite a meaningless answer.
    public bool SupportsDateRange { get; init; } = true;

    // The query. It always declares @from and @to, even when the report ignores them, so
    // execution can bind the same two parameters every time.
    //
    // Internal, so the query text stops at the business layer and the web project only ever
    // sees a report's shape and its results. That rules out `required` (a required member
    // cannot be less visible than its type), so ReportService checks it is set instead.
    internal string Sql { get; init; } = string.Empty;

    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public IReadOnlyList<ReportKpiDefinition> Kpis { get; init; } = [];
}

public class ReportKpi
{
    public required string Label { get; init; }
    public decimal Value { get; init; }
    public ReportColumnType Format { get; init; }
}

public class ReportResult
{
    public required ReportDefinition Definition { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    // One array per row, in the same order as Definition.Columns.
    public required IReadOnlyList<object?[]> Rows { get; init; }
    public IReadOnlyList<ReportKpi> Kpis { get; init; } = [];

    // True when the query had more rows than the cap. The KPIs then describe the rows shown,
    // not the whole period, and the screen has to say so rather than quietly under-report.
    public bool Truncated { get; init; }
    public int RowLimit { get; init; }
}
