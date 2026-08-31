using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

public class DailyCloseService : IDailyCloseService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly ILogger<DailyCloseService> _logger;

    public DailyCloseService(HanyOpticsDbContext dbContext, ILogger<DailyCloseService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // Asked of the database rather than worked out from DateTime.Now. The cutoff lives in
    // dbo.fn_business_date, and the server's clock is the one the rows were stamped with -
    // deciding "which day are we in" on the web server could disagree with the data.
    public async Task<DateOnly> GetCurrentBusinessDateAsync()
    {
        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                opened = true;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT dbo.fn_business_date(SYSDATETIME());";
            var value = await command.ExecuteScalarAsync();
            return DateOnly.FromDateTime(Convert.ToDateTime(value));
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    public async Task<DailyCloseReport> GetAsync(DateOnly? businessDate)
    {
        var day = businessDate ?? await GetCurrentBusinessDateAsync();
        var current = businessDate is null ? day : await GetCurrentBusinessDateAsync();

        var report = new DailyCloseReport
        {
            BusinessDate = day,
            CurrentBusinessDate = current
        };

        var connection = (SqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                opened = true;
            }

            report.Summary = await ReadSummaryAsync(connection, day);
            report.Orders = await ReadOrdersAsync(connection, day);
            report.Payments = await ReadPaymentsAsync(connection, day);
            report.Deliveries = await ReadDeliveriesAsync(connection, day);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        _logger.LogInformation(
            "Daily close for {Day}: {Orders} orders, {Payments} payments, {Deliveries} deliveries.",
            day, report.Orders.Count, report.Payments.Count, report.Deliveries.Count);

        return report;
    }

    private static SqlCommand DayCommand(SqlConnection connection, string sql, DateOnly day)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@day", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
        return command;
    }

    private static async Task<DailyCloseSummary?> ReadSummaryAsync(SqlConnection connection, DateOnly day)
    {
        await using var command = DayCommand(connection,
            "SELECT * FROM vw_daily_close WHERE business_date = @day;", day);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;   // nothing happened that day at all

        return new DailyCloseSummary
        {
            BusinessDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("business_date"))),
            DayStartAt = reader.GetDateTime(reader.GetOrdinal("day_start_at")),
            DayEndAt = reader.GetDateTime(reader.GetOrdinal("day_end_at")),

            CashNet = reader.GetDecimal(reader.GetOrdinal("cash_net")),
            VisaNet = reader.GetDecimal(reader.GetOrdinal("visa_net")),
            NetTotal = reader.GetDecimal(reader.GetOrdinal("net_total")),
            FromTodayInvoices = reader.GetDecimal(reader.GetOrdinal("from_today_invoices")),
            FromOldInvoices = reader.GetDecimal(reader.GetOrdinal("from_old_invoices")),
            RefundsTotal = reader.GetDecimal(reader.GetOrdinal("refunds_total")),
            RefundsCount = reader.GetInt32(reader.GetOrdinal("refunds_count")),
            PaymentsCount = reader.GetInt32(reader.GetOrdinal("payments_count")),
            FirstPaymentAt = GetNullableDateTime(reader, "first_payment_at"),
            LastPaymentAt = GetNullableDateTime(reader, "last_payment_at"),

            OrdersCount = reader.GetInt32(reader.GetOrdinal("orders_count")),
            OrdersTotal = reader.GetDecimal(reader.GetOrdinal("orders_total")),
            OrdersPaid = reader.GetDecimal(reader.GetOrdinal("orders_paid")),
            OrdersRemaining = reader.GetDecimal(reader.GetOrdinal("orders_remaining")),
            OrdersWithBalance = reader.GetInt32(reader.GetOrdinal("orders_with_balance")),
            CancelledCount = reader.GetInt32(reader.GetOrdinal("cancelled_count")),

            DeliveriesCount = reader.GetInt32(reader.GetOrdinal("deliveries_count")),
            DeliveriesTotal = reader.GetDecimal(reader.GetOrdinal("deliveries_total")),

            LastActivityAt = GetNullableDateTime(reader, "last_activity_at")
        };
    }

    private static async Task<List<DailyCloseOrder>> ReadOrdersAsync(SqlConnection connection, DateOnly day)
    {
        await using var command = DayCommand(connection,
            "SELECT * FROM vw_daily_close_orders WHERE business_date = @day ORDER BY order_date;", day);

        var rows = new List<DailyCloseOrder>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new DailyCloseOrder
            {
                OrderId = reader.GetInt32(reader.GetOrdinal("order_id")),
                InvoiceNumber = reader.GetString(reader.GetOrdinal("invoice_number")),
                OrderDate = reader.GetDateTime(reader.GetOrdinal("order_date")),
                CustomerName = GetNullableString(reader, "customer_name"),
                CustomerPhone = GetNullableString(reader, "customer_phone"),
                Status = reader.GetString(reader.GetOrdinal("status")),
                DeliveryType = GetNullableString(reader, "delivery_type"),
                TotalAmount = reader.GetDecimal(reader.GetOrdinal("total_amount")),
                PaidAmount = reader.GetDecimal(reader.GetOrdinal("paid_amount")),
                RemainingAmount = reader.GetDecimal(reader.GetOrdinal("remaining_amount")),
                DeliveredAt = GetNullableDateTime(reader, "delivered_at"),
                CreatedBy = GetNullableString(reader, "created_by"),
                IsAfterMidnight = reader.GetInt32(reader.GetOrdinal("is_after_midnight")) == 1,
                ItemsCount = reader.GetInt32(reader.GetOrdinal("items_count")),
                ItemTypes = GetNullableString(reader, "item_types")
            });
        }

        return rows;
    }

    private static async Task<List<DailyClosePayment>> ReadPaymentsAsync(SqlConnection connection, DateOnly day)
    {
        await using var command = DayCommand(connection,
            "SELECT * FROM vw_daily_close_payments WHERE business_date = @day ORDER BY paid_at;", day);

        var rows = new List<DailyClosePayment>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new DailyClosePayment
            {
                PaymentId = reader.GetInt32(reader.GetOrdinal("payment_id")),
                PaidAt = reader.GetDateTime(reader.GetOrdinal("paid_at")),
                InvoiceNumber = reader.GetString(reader.GetOrdinal("invoice_number")),
                CustomerName = GetNullableString(reader, "customer_name"),
                PaymentType = reader.GetString(reader.GetOrdinal("payment_type")),
                PaymentMethod = reader.GetString(reader.GetOrdinal("payment_method")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                SignedAmount = reader.GetDecimal(reader.GetOrdinal("signed_amount")),
                IsOldInvoice = reader.GetInt32(reader.GetOrdinal("is_old_invoice")) == 1,
                IsAfterMidnight = reader.GetInt32(reader.GetOrdinal("is_after_midnight")) == 1,
                ReceivedBy = GetNullableString(reader, "received_by"),
                Notes = GetNullableString(reader, "notes")
            });
        }

        return rows;
    }

    private static async Task<List<DailyCloseDelivery>> ReadDeliveriesAsync(SqlConnection connection, DateOnly day)
    {
        await using var command = DayCommand(connection,
            "SELECT * FROM vw_daily_close_deliveries WHERE business_date = @day ORDER BY delivered_at;", day);

        var rows = new List<DailyCloseDelivery>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new DailyCloseDelivery
            {
                OrderId = reader.GetInt32(reader.GetOrdinal("order_id")),
                InvoiceNumber = reader.GetString(reader.GetOrdinal("invoice_number")),
                CustomerName = GetNullableString(reader, "customer_name"),
                CustomerPhone = GetNullableString(reader, "customer_phone"),
                DeliveredAt = reader.GetDateTime(reader.GetOrdinal("delivered_at")),
                OrderDate = reader.GetDateTime(reader.GetOrdinal("order_date")),
                TotalAmount = reader.GetDecimal(reader.GetOrdinal("total_amount")),
                RemainingAmount = reader.GetDecimal(reader.GetOrdinal("remaining_amount")),
                IsSameDay = reader.GetInt32(reader.GetOrdinal("is_same_day")) == 1,
                IsAfterMidnight = reader.GetInt32(reader.GetOrdinal("is_after_midnight")) == 1,
                CollectedToday = reader.GetDecimal(reader.GetOrdinal("collected_today"))
            });
        }

        return rows;
    }

    private static string? GetNullableString(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetString(i);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : reader.GetDateTime(i);
    }
}
