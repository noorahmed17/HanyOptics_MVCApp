using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static HanyOptics.BusinessLogic.Services.SqlReader;

namespace HanyOptics.BusinessLogic.Services;

// Parameterised SQL rather than stored procedures for the three inserts: suppliers,
// purchase_invoices and purchase_returns carry no triggers and the deployed module ships no
// procedure for them, so the checks a procedure would make are made here instead.
public class SupplierService : ISupplierService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SupplierService> _logger;

    public SupplierService(HanyOpticsDbContext dbContext, ICurrentUser currentUser, ILogger<SupplierService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<IReadOnlyList<SupplierBalance>> ListAsync() =>
        WithConnectionAsync<IReadOnlyList<SupplierBalance>>(_dbContext, async connection =>
            // Owed first, largest first - that is the order someone deciding who to pay
            // reads the list in.
            await QueryAsync(connection,
                "SELECT * FROM dbo.vw_supplier_balances ORDER BY CASE WHEN balance_due > 0 THEN 0 ELSE 1 END, balance_due DESC, name;",
                MapBalance));

    public Task<IReadOnlyList<SupplierOption>> GetOptionsAsync() =>
        WithConnectionAsync<IReadOnlyList<SupplierOption>>(_dbContext, async connection =>
            await QueryAsync(connection,
                "SELECT supplier_id, name FROM dbo.suppliers ORDER BY name;",
                r => new SupplierOption(r.GetInt32(0), r.GetString(1))));

    public Task<SupplierDetail?> GetAsync(int supplierId) =>
        WithConnectionAsync(_dbContext, async connection =>
        {
            var id = new SqlParameter("@id", supplierId);

            var balance = (await QueryAsync(connection,
                "SELECT * FROM dbo.vw_supplier_balances WHERE supplier_id = @id;", MapBalance, id)).FirstOrDefault();
            if (balance is null)
                return null;

            string? notes;
            await using (var command = Command(connection, "SELECT notes FROM dbo.suppliers WHERE supplier_id = @id;",
                             new SqlParameter("@id", supplierId)))
                notes = await command.ExecuteScalarAsync() as string;

            var invoices = await QueryAsync(connection,
                "SELECT purchase_id, purchase_date, total_amount, notes FROM dbo.purchase_invoices WHERE supplier_id = @id ORDER BY purchase_date DESC, purchase_id DESC;",
                r => new SupplierInvoiceOption
                {
                    PurchaseId = r.GetInt32(0),
                    PurchaseDate = DateOnly.FromDateTime(r.GetDateTime(1)),
                    Amount = r.GetDecimal(2),
                    Notes = r.IsDBNull(3) ? null : r.GetString(3)
                },
                new SqlParameter("@id", supplierId));

            // The same three sources vw_supplier_balances sums, listed line by line, so the
            // history always adds up to the balance shown above it.
            var history = await QueryAsync(connection,
                """
                SELECT d, kind, amount, note FROM (
                    SELECT CAST(purchase_date AS DATETIME2) AS d, created_at AS seq, N'invoice' AS kind, total_amount AS amount, notes AS note
                    FROM dbo.purchase_invoices WHERE supplier_id = @id
                    UNION ALL
                    SELECT CAST(return_date AS DATETIME2), created_at, N'return', amount, notes
                    FROM dbo.purchase_returns WHERE supplier_id = @id
                    UNION ALL
                    SELECT paid_at, created_at, N'payment', amount, ISNULL(description, category)
                    FROM dbo.expenses WHERE supplier_id = @id AND entry_type = N'expense' AND cancelled_at IS NULL
                ) h
                ORDER BY d DESC, seq DESC;
                """,
                r => new SupplierLedgerEntry
                {
                    Date = r.GetDateTime(0),
                    Kind = r.GetString(1),
                    Amount = r.GetDecimal(2),
                    Note = r.IsDBNull(3) ? null : r.GetString(3)
                },
                new SqlParameter("@id", supplierId));

            return new SupplierDetail { Balance = balance, Notes = notes, Invoices = invoices, History = history };
        });

    public async Task<CreateOutcome> CreateAsync(CreateSupplierRequest request)
    {
        var name = Clean(request.Name);
        if (name is null)
            return CreateOutcome.Failure("أدخل اسم المورد.");

        try
        {
            var idParam = new SqlParameter("@p_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

            // The name check and the insert run as one batch, so two people adding the same
            // supplier at the same moment cannot both get through.
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF EXISTS (SELECT 1 FROM dbo.suppliers WITH (UPDLOCK, HOLDLOCK) WHERE name = @p_name)
                BEGIN
                    ROLLBACK TRANSACTION;
                    THROW 50000, N'يوجد مورد بهذا الاسم بالفعل.', 1;
                END
                INSERT INTO dbo.suppliers (name, phone, notes) VALUES (@p_name, @p_phone, @p_notes);
                SET @p_id = CAST(SCOPE_IDENTITY() AS INT);
                COMMIT TRANSACTION;
                """,
                NVarChar("@p_name", name, 100),
                NVarChar("@p_phone", Clean(request.Phone), 20),
                NVarChar("@p_notes", Clean(request.Notes), 500),
                idParam);

            return CreateOutcome.Success((int)idParam.Value!);
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL error creating supplier {Name}. SqlErrors={SqlErrors}", name, StoredProcedureErrors.Describe(ex));
            return CreateOutcome.Failure(StoredProcedureErrors.ToUserMessage(ex, "يوجد مورد بهذا الاسم بالفعل."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating supplier {Name}.", name);
            return CreateOutcome.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    public Task<OperationResult> AddInvoiceAsync(SupplierInvoiceRequest request)
    {
        if (request.Amount is null or <= 0)
            return Task.FromResult(OperationResult.Failure("أدخل قيمة الفاتورة."));

        return RunAsync("adding purchase invoice", request.SupplierId,
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.suppliers WHERE supplier_id = @p_supplier)
                THROW 50000, N'المورد غير موجود.', 1;
            INSERT INTO dbo.purchase_invoices (supplier_id, purchase_date, total_amount, notes, created_by)
            VALUES (@p_supplier, ISNULL(@p_date, dbo.fn_business_date(SYSDATETIME())), @p_amount, @p_notes, @p_user);
            """,
            new SqlParameter("@p_supplier", request.SupplierId),
            new SqlParameter("@p_date", SqlDbType.Date) { Value = request.PurchaseDate.HasValue ? request.PurchaseDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value },
            Money("@p_amount", request.Amount),
            NVarChar("@p_notes", Clean(request.Notes), 500),
            new SqlParameter("@p_user", _currentUser.RequireUserId()));
    }

    public Task<OperationResult> AddReturnAsync(SupplierReturnRequest request)
    {
        if (request.Amount is null or <= 0)
            return Task.FromResult(OperationResult.Failure("أدخل قيمة المرتجع."));

        return RunAsync("adding purchase return", request.SupplierId,
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.suppliers WHERE supplier_id = @p_supplier)
                THROW 50000, N'المورد غير موجود.', 1;
            -- A return tied to an invoice must be tied to one of this supplier's invoices;
            -- the foreign key alone would accept another supplier's.
            IF @p_purchase IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.purchase_invoices WHERE purchase_id = @p_purchase AND supplier_id = @p_supplier)
                THROW 50000, N'الفاتورة المختارة لا تخص هذا المورد.', 1;
            INSERT INTO dbo.purchase_returns (supplier_id, purchase_id, return_date, amount, notes, created_by)
            VALUES (@p_supplier, @p_purchase, ISNULL(@p_date, dbo.fn_business_date(SYSDATETIME())), @p_amount, @p_notes, @p_user);
            """,
            new SqlParameter("@p_supplier", request.SupplierId),
            IntParam("@p_purchase", request.PurchaseId),
            new SqlParameter("@p_date", SqlDbType.Date) { Value = request.ReturnDate.HasValue ? request.ReturnDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value },
            Money("@p_amount", request.Amount),
            NVarChar("@p_notes", Clean(request.Notes), 500),
            new SqlParameter("@p_user", _currentUser.RequireUserId()));
    }

    private async Task<OperationResult> RunAsync(string what, int supplierId, string sql, params SqlParameter[] parameters)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(sql, parameters);
            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL error {What} for supplier {SupplierId}. SqlErrors={SqlErrors}",
                what, supplierId, StoredProcedureErrors.Describe(ex));
            return OperationResult.Failure(StoredProcedureErrors.ToUserMessage(ex, "بيانات مكررة"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error {What} for supplier {SupplierId}.", what, supplierId);
            return OperationResult.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    private static SupplierBalance MapBalance(SqlDataReader r) => new()
    {
        SupplierId = r.GetInt32(r.GetOrdinal("supplier_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Phone = r.NullableString("phone"),
        TotalInvoiced = r.Decimal("total_invoiced"),
        TotalReturned = r.Decimal("total_returned"),
        TotalPaid = r.Decimal("total_paid"),
        BalanceDue = r.Decimal("balance_due")
    };
}
