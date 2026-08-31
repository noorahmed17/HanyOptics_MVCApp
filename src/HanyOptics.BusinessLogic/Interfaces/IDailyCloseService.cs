using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// The end-of-day screen: everything that happened in one working night, read-only.
//
// All of it comes from the vw_daily_close* views, which group by dbo.fn_business_date
// rather than by calendar date. That function is the single place the 06:00 cutoff is
// defined - reproducing it in C# would be a second copy of the rule, free to disagree
// with the views the moment someone changes the shop's hours.
public interface IDailyCloseService
{
    // The business day the shop is in right now. At 2am this is still yesterday's date,
    // which is the whole point.
    Task<DateOnly> GetCurrentBusinessDateAsync();

    // One day's close. Pass null for the day the shop is currently in.
    Task<DailyCloseReport> GetAsync(DateOnly? businessDate);
}
