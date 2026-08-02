using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

// The single place a failure coming back from one of the stored procedures is turned into
// something a user - or someone reading a log after the fact - can act on. Every write
// path goes through here: the new-order wizard (NewOrderService) and the order-detail
// edits and bulk status updates (OrderService).
//
// This is shared rather than copied for a concrete reason. The two services each had their
// own copy of this logic, the copies drifted, and the stale one silently replaced real SP
// rejection messages with "حدث خطأ غير متوقع" - so a genuine, explainable failure looked
// like a bug in the app. Anything added here reaches both callers at once.
internal static class StoredProcedureErrors
{
    public const string GenericMessage = "حدث خطأ غير متوقع — حاول مرة أخرى";

    // A single SqlException carries several SQL Server messages at once
    // (SqlException.Errors) - not just the one SqlException.Message happens to surface.
    // When a chained SP rejects a write from inside our ambient transaction, its own
    // ROLLBACK TRANSACTION unwinds our outer transaction too, so SQL Server appends this
    // "transaction count mismatch" diagnostic alongside the SP's real RAISERROR message -
    // and this is frequently the one Message shows. Dropping just this number and keeping
    // whatever remains recovers the SP's actual Arabic rejection reason.
    private const int TransactionCountMismatch = 266;

    // Duplicate-key errors. 2627 = unique constraint, 2601 = unique index.
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    // The SPs raise business-rule failures via RAISERROR (e.g. "رقم الفاتورة مستخدم من
    // قبل", "الكمية المتاحة من الإطار غير كافية"). That Arabic text is written to be shown
    // to staff as-is, so it is passed through rather than re-worded or re-validated in C#.
    // Only two things get a generic fallback: a raw unique-constraint violation (a
    // check-then-insert race inside a SP, whose message is English and unhelpful), and the
    // case where filtering leaves nothing at all.
    //
    // duplicateMessage differs per caller on purpose: the wizard knows a collision can
    // only be the invoice number or a barcode and can say so, while the edit paths have no
    // equivalent field to name.
    public static string ToUserMessage(SqlException ex, string duplicateMessage)
    {
        if (ex.Number is UniqueConstraintViolation or UniqueIndexViolation)
            return duplicateMessage;

        var realMessages = ex.Errors
            .Cast<SqlError>()
            .Where(e => e.Number != TransactionCountMismatch)
            .Select(e => e.Message)
            .ToList();

        return realMessages.Count > 0 ? string.Join(" ", realMessages) : GenericMessage;
    }

    // Every message with its number, for the log. Unlike ToUserMessage this keeps the 266
    // noise: when reading a failure after the fact, knowing an inner SP rolled the
    // transaction back is useful context rather than clutter.
    public static string Describe(SqlException ex) =>
        string.Join(" | ", ex.Errors.Cast<SqlError>().Select(e => $"[{e.Number}] {e.Message}"));

    // The SPs manage their own BEGIN/COMMIT/ROLLBACK. When one of them rolls back from
    // inside our transaction the transaction is already doomed, so rolling it back again
    // can itself throw - which must not mask the original error. Still swallowed on
    // purpose, but logged, so a rollback that misbehaves is visible instead of silent.
    public static async Task SafeRollbackAsync(IDbContextTransaction transaction, ILogger logger)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rollback after a failed commit threw; the original error is reported instead.");
        }
    }
}
