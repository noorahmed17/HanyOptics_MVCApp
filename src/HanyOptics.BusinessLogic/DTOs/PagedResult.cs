namespace HanyOptics.BusinessLogic.Models;

// How many rows each screen shows at a time.
//
// They differ because the rows differ: an order row carries a customer, money, a status and
// a checkbox and is read carefully, while a report row is one line in a table being
// scanned. Sized so a page is roughly a screenful rather than a scroll marathon.
public static class PageSizes
{
    public const int Orders = 20;
    public const int Customers = 20;
    public const int Frames = 30;
    public const int Reports = 50;

    // The panel beside a selected customer, showing their history. Not paged - it is a
    // sidebar, not a screen - so it takes the most recent and says if there are more.
    public const int CustomerOrders = 20;
}

// One page of a list, plus what the pager needs to draw itself.
//
// TotalCount is the count of everything matching the filter, not of this page - the screen
// has to be able to say "42 من 1,525" rather than implying the page is all there is.
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    public const int DefaultPageSize = 50;

    // A page size arriving from the query string is user input: unbounded, it is a way to
    // ask the server for every row at once, which is the thing paging exists to prevent.
    public const int MaxPageSize = 200;

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public int FirstRowNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;
    public int LastRowNumber => Math.Min(Page * PageSize, TotalCount);

    // Clamps whatever the query string asked for into something the database can serve.
    // A page past the end comes back as the last real page rather than an empty screen
    // that looks like the search found nothing.
    public static (int Page, int PageSize) Normalise(int? page, int? pageSize, int totalCount = int.MaxValue)
    {
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var requested = Math.Max(page ?? 1, 1);

        if (totalCount == int.MaxValue)
            return (requested, size);

        var lastPage = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)size);
        return (Math.Min(requested, lastPage), size);
    }
}
