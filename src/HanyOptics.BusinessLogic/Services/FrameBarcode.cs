using System.Globalization;

namespace HanyOptics.BusinessLogic.Services;

// The shop's printed labels carry a barcode built by sp_generate_barcode, which hides the
// sell price in plain sight:
//
//     [2 digits][letter]  [price]  [letter][2 digits]
//      2  8      D         2300     N      5  5        -> 28D2300N55 = 2300 ج
//      1  5      G         1750     D      8  4        -> 15G1750D84 = 1750 ج
//
// The padding either side is random, so two frames at the same price get different codes.
// Reading the price back means a scan can fill it in rather than the user copying it off
// the label by hand.
//
// Anything that does not match the shape - the older HO-2026-000NN codes, or a supplier's
// own barcode - simply yields no price. That is not an error: the frame is still added,
// the price is just typed.
internal static class FrameBarcode
{
    public static decimal? TryReadSellPrice(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;

        var code = barcode.Trim();

        // 3 characters of padding at each end, so a price needs at least one digit between.
        if (code.Length < 7) return null;

        if (!char.IsAsciiDigit(code[0]) || !char.IsAsciiDigit(code[1]) || !char.IsAsciiLetter(code[2]))
            return null;

        var tail = code[^3..];
        if (!char.IsAsciiLetter(tail[0]) || !char.IsAsciiDigit(tail[1]) || !char.IsAsciiDigit(tail[2]))
            return null;

        var middle = code[3..^3];
        foreach (var c in middle)
        {
            if (!char.IsAsciiDigit(c)) return null;
        }

        return decimal.TryParse(middle, NumberStyles.None, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }
}
