using System.Text;

namespace HanyOptics.BusinessLogic.Models;

// The shop records Egyptian mobile numbers only: 11 digits starting 010, 011, 012 or 015.
// A customer with a foreign number is entered as زبون عابر with their name on the invoice.
//
// Normalize cleans what staff commonly type - spaces, dashes, Arabic digits, a +20 / 0020
// prefix - so the same customer is always stored, and found, under one spelling. dbo.customers
// carries the same rule as a CHECK constraint and a unique index (v14), so this is where the
// friendly message comes from, not the only guard.
public static class EgyptianMobile
{
    public const string InvalidMessage =
        "رقم الهاتف غير صحيح. اكتب رقم موبايل مصري من 11 رقمًا يبدأ بـ 010 أو 011 أو 012 أو 015.";

    // The cleaned number, or null when what was typed is not an Egyptian mobile number
    // (or is empty - callers check for that separately).
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9')
                digits.Append(ch);
            else if (ch is >= '\u0660' and <= '\u0669')      // ٠١٢٣٤٥٦٧٨٩
                digits.Append((char)('0' + (ch - '\u0660')));
            else if (ch is >= '\u06F0' and <= '\u06F9')      // ۰۱۲۳۴۵۶۷۸۹
                digits.Append((char)('0' + (ch - '\u06F0')));
            else if (ch is ' ' or '\u00A0' or '-' or '.' or '(' or ')' or '+')
                continue;
            else
                return null;
        }

        var d = digits.ToString();
        if (d.StartsWith("0020"))
            d = d[3..];                                       // 0020 10… → 010…
        else if (d.Length == 12 && d.StartsWith("20"))
            d = "0" + d[2..];                                 // +20 10… → 010…
        else if (d.Length == 10 && d[0] == '1')
            d = "0" + d;                                      // 10… → 010…

        return IsValid(d) ? d : null;
    }

    public static bool IsValid(string? phone) =>
        phone is { Length: 11 }
        && phone.StartsWith("01")
        && (phone[2] is '0' or '1' or '2' or '5')
        && phone.All(char.IsAsciiDigit);
}