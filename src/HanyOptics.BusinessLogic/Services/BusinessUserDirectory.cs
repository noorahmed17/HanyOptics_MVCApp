using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;

// Direct parameterised SQL against `users`: there is no stored procedure for staff
// accounts (the SPs cover orders/items/payments/status only), and `users` has no EF
// entity because nothing in the app queries staff rows as objects - they exist purely to
// satisfy the operational FKs.
public class BusinessUserDirectory : IBusinessUserDirectory
{
    // Authentication is entirely Identity's job (AspNetUsers.PasswordHash). The business
    // table still declares password_hash NOT NULL from the original schema, so it gets a
    // marker making it obvious no credential lives here.
    private const string PasswordHashPlaceholder = "MANAGED_BY_ASPNET_IDENTITY";

    // CK__users__role allows exactly these two values.
    private const string AdminRole = "admin";
    private const string StaffRole = "sales";

    private readonly HanyOpticsDbContext _dbContext;

    public BusinessUserDirectory(HanyOpticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(string fullName, string username, bool isAdmin)
    {
        var userIdParam = new SqlParameter("@p_user_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

        // branch_id / is_active / created_at are left to their column defaults so this
        // stays correct if the schema's defaults ever change.
        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (name, username, password_hash, role)
            VALUES (@p_name, @p_username, @p_password_hash, @p_role);
            SET @p_user_id = CAST(SCOPE_IDENTITY() AS INT);
            """,
            new SqlParameter("@p_name", SqlDbType.NVarChar, 200) { Value = fullName },
            new SqlParameter("@p_username", SqlDbType.NVarChar, 100) { Value = username },
            new SqlParameter("@p_password_hash", SqlDbType.NVarChar, 255) { Value = PasswordHashPlaceholder },
            new SqlParameter("@p_role", SqlDbType.NVarChar, 20) { Value = isAdmin ? AdminRole : StaffRole },
            userIdParam);

        return (int)userIdParam.Value!;
    }

    public async Task<int?> FindIdByUsernameAsync(string username)
    {
        var ids = await _dbContext.Database
            .SqlQueryRaw<int>(
                "SELECT user_id AS Value FROM users WHERE username = @p_username",
                new SqlParameter("@p_username", SqlDbType.NVarChar, 100) { Value = username })
            .ToListAsync();

        return ids.Count > 0 ? ids[0] : null;
    }

    public async Task DeleteIfUnreferencedAsync(int userId)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM users
                WHERE user_id = @p_user_id
                  AND NOT EXISTS (SELECT 1 FROM orders            WHERE created_by  = @p_user_id)
                  AND NOT EXISTS (SELECT 1 FROM payments          WHERE received_by = @p_user_id)
                  AND NOT EXISTS (SELECT 1 FROM order_status_log  WHERE changed_by  = @p_user_id)
                  AND NOT EXISTS (SELECT 1 FROM frame_damage_log  WHERE recorded_by = @p_user_id)
                  AND NOT EXISTS (SELECT 1 FROM restock_log       WHERE recorded_by = @p_user_id)
                  AND NOT EXISTS (SELECT 1 FROM purchase_invoices WHERE created_by  = @p_user_id)
                """,
                new SqlParameter("@p_user_id", userId));
        }
        catch
        {
            // Best-effort cleanup of a half-created account; the caller is already
            // returning a failure and must not be derailed by this.
        }
    }
}
