using System.Globalization;
using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.Web.Models;

// How the expenses screens say things: labels and badge colours for the database's own
// values, and the money/time formats. Shared so حركة الدرج, المصروفات and التصحيحات
// cannot describe the same entry three different ways.
public static class ExpenseDisplay
{
    public static readonly CultureInfo ArEg = CultureInfo.GetCultureInfo("ar-EG");

    // Whole pounds as the rest of the app shows them; piasters only when there are any, so
    // a correction screen never hides the difference between 799.50 and 800.
    public static string Money(decimal value) =>
        value.ToString(value % 1 == 0 ? "N0" : "N2", ArEg) + " ج";

    public static string Time(DateTime value) => value.ToString("h:mm tt", ArEg);

    public static string DateTimeShort(DateTime value) => value.ToString("d MMM، h:mm tt", ArEg);

    public static string DateShort(DateTime value) => value.ToString("d MMM yyyy", ArEg);

    public static string MonthName(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", ArEg);

    public static (string Css, string Label) Type(string entryType) => entryType switch
    {
        ExpenseEntryTypes.Expense => ("badge-exp", "مصروف"),
        ExpenseEntryTypes.OwnerDraw => ("badge-owner", "مسحوبات المالك"),
        ExpenseEntryTypes.Income => ("badge-income", "إيراد آخر"),
        _ => ("badge-user", entryType)
    };

    // Where the money came from, as one badge - the thing that decides whether the drawer
    // moved at all.
    public static (string Css, string Label) Source(ExpenseEntry e)
    {
        if (e.IsIncome)
            return e.PaymentMethod == PaymentMethods.Visa ? ("badge-visa", "بطاقة") : ("badge-drawer", "الدرج");

        if (e.FundingSource == FundingSources.Drawer)
            return ("badge-drawer", "من الدرج");

        return ("badge-outside", e.PaymentMethod == PaymentMethods.Visa ? "من خارج الدرج · بطاقة" : "من خارج الدرج");
    }

    public static string Method(string method) => method == PaymentMethods.Visa ? "بطاقة" : "نقدًا";

    public static string PaymentType(string type) => type switch
    {
        "deposit" => "عربون",
        "final" => "باقي",
        "full" => "كامل",
        "refund" => "استرجاع",
        _ => type
    };

    public static (string Css, string Label) OrderStatus(string status) => status switch
    {
        "sold" => ("badge-sold", "تم البيع"),
        "ready" => ("badge-ready", "جاهز"),
        "delivered" => ("badge-delivered", "تم التسليم"),
        "cancelled" => ("badge-cancelled", "ملغي"),
        _ => ("badge-user", status)
    };

    public static (string Css, string Label) LedgerKind(string kind) => kind switch
    {
        SupplierLedgerKinds.Invoice => ("badge-exp", "فاتورة"),
        SupplierLedgerKinds.Return => ("badge-outside", "مرتجع"),
        SupplierLedgerKinds.Payment => ("badge-income", "دفعة"),
        _ => ("badge-user", kind)
    };
}
