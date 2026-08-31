using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.Web.Models;

// What _Pager.cshtml needs to draw itself, flattened out of PagedResult<T>.
//
// Non-generic on purpose: a Razor partial cannot take an open generic model, and the pager
// does not care what it is paging over - only how many rows there are and where we are in
// them.
public class PagerInfo
{
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public int FirstRow { get; init; }
    public int LastRow { get; init; }

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagerInfo From<T>(PagedResult<T> result) => new()
    {
        Page = result.Page,
        TotalPages = result.TotalPages,
        TotalCount = result.TotalCount,
        FirstRow = result.FirstRowNumber,
        LastRow = result.LastRowNumber
    };
}
