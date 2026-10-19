namespace HanyOptics.BusinessLogic.Models;

// The standing placeholder identity for a one-off customer who left neither a phone
// number nor a name. Because `customers.phone` is indexed but NOT unique, and the wizard
// resolves customers by phone, every walk-in order naturally collapses onto this single
// customer row instead of creating a near-duplicate for each visit.
public static class WalkInCustomer
{
    // 01 followed by zeros - an obviously-not-real Egyptian mobile number, and the same
    // 11-digit shape as a genuine one so it never breaks phone formatting or the input's
    // maxlength.
    public const string Phone = "01000000000";

    public const string Name = "زبون عابر";

    // Shown when the user submits step 1 without identifying the customer in any way.
    public const string MissingIdentityMessage =
        "أدخل رقم الهاتف أو اسم الزبون — أو اضغط \"زبون عابر\" لو الزبون مش هيتسجل";

    public static bool IsWalkInPhone(string? phone) =>
        string.Equals(phone?.Trim(), Phone, StringComparison.Ordinal);
}
