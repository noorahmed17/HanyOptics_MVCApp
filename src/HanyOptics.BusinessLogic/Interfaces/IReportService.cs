using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Read-only reporting over the database's existing views.
//
// Nothing here writes, and nothing here re-derives money that the database already
// computes - the reporting views own the awkward rules (refunds counted negative,
// cancelled orders excluded, payment date and delivery date being different days) and a
// second copy of those rules living in C# would be free to drift from the first.
public interface IReportService
{
    IReadOnlyList<ReportDefinition> Catalog { get; }

    ReportDefinition? Find(string? key);

    // Runs one report. `from`/`to` are inclusive days and are ignored by reports that
    // describe a moment rather than a period (stock on hand, money currently owed).
    Task<ReportResult> RunAsync(ReportDefinition definition, DateOnly? from, DateOnly? to);
}
