using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;
public class OrderService : IOrderService
{
    // Matches NewOrderService's DefaultStaffUserId - see the TODO there about the missing
    // AspNetUsers -> business `users` mapping.
    private const int DefaultStaffUserId = 1;
    private const string GenericErrorMessage = "حدث خطأ غير متوقع — حاول مرة أخرى";

    private readonly HanyOpticsDbContext _dbContext;

    public OrderService(HanyOpticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Order?> GetByIdAsync(int orderId) =>
        _dbContext.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Prescription)
            .Include(o => o.Payments)
            .Include(o => o.StatusLogs)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

    public Task<Doctor?> GetDoctorByIdAsync(int doctorId) =>
        _dbContext.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.DoctorId == doctorId);

    public async Task<IReadOnlyList<Order>> GetAllAsync(int take = 50) =>
        await _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .Take(take)
            .ToListAsync();

    public async Task<int> CreateOrderAsync(Order order)
    {
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();
        return order.OrderId;
    }

    public async Task<IReadOnlyList<OrderListItem>> GetOrderListAsync(OrderStatus? status = null, DeliveryType? deliveryType = null, int? customerId = null)
    {
        var query = _dbContext.Orders.Include(o => o.OrderItems).AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (deliveryType.HasValue)
            query = query.Where(o => o.DeliveryType == deliveryType.Value);
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);

        var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();

        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var phones = await _dbContext.Customers
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.Phone);

        return orders.Select(o => new OrderListItem
        {
            OrderId = o.OrderId,
            InvoiceNumber = o.InvoiceNumber,
            OrderDate = o.OrderDate,
            CustomerName = o.CustomerName,
            CustomerPhone = phones.GetValueOrDefault(o.CustomerId),
            ItemTypes = o.OrderItems.Select(i => i.ItemType).Distinct().ToList(),
            ItemCount = o.OrderItems.Count,
            DeliveryType = o.DeliveryType,
            Status = o.Status,
            TotalAmount = o.TotalAmount,
            PaidAmount = o.PaidAmount,
            RemainingAmount = o.RemainingAmount
        }).ToList();
    }

    public async Task<OperationResult> UpdateOrderStatusAsync(int orderId, OrderStatus newStatus, string? notes = null)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "EXEC sp_update_order_status @order_id=@p_order_id, @new_status=@p_new_status, @changed_by=@p_changed_by, @notes=@p_notes",
                new SqlParameter("@p_order_id", orderId),
                new SqlParameter("@p_new_status", StatusToDb(newStatus)),
                new SqlParameter("@p_changed_by", DefaultStaffUserId),
                new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)notes ?? DBNull.Value });

            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            // sp_update_order_status raises business-rule rejections (terminal state,
            // ready->cancelled blocked, no active items, ...) via RAISERROR with a clear
            // Arabic message - shown to staff as-is, same convention as NewOrderService.
            return OperationResult.Failure(ex.Number is 2627 or 2601 ? "بيانات مكررة" : ex.Message);
        }
        catch (Exception)
        {
            return OperationResult.Failure(GenericErrorMessage);
        }
    }

    public async Task<OperationResult> SwapDamagedFrameAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes = null)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                EXEC sp_swap_frame
                    @item_id = @p_item_id, @new_frame_agreed_price = @p_new_frame_agreed_price,
                    @new_frame_id = @p_new_frame_id, @old_frame_disposition = @p_old_frame_disposition,
                    @discount_reason = @p_discount_reason, @changed_by = @p_changed_by
                """,
                new SqlParameter("@p_item_id", itemId),
                new SqlParameter("@p_new_frame_agreed_price", newFrameAgreedPrice),
                new SqlParameter("@p_new_frame_id", newFrameId),
                new SqlParameter("@p_old_frame_disposition", "damaged"),
                new SqlParameter("@p_discount_reason", SqlDbType.NVarChar, 200) { Value = (object?)notes ?? DBNull.Value },
                new SqlParameter("@p_changed_by", DefaultStaffUserId));

            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            // sp_swap_frame raises business-rule rejections (terminal order, item not
            // active, replacement frame unavailable, same-frame no-op, ...) via RAISERROR
            // with a clear Arabic message - shown to staff as-is.
            return OperationResult.Failure(ex.Number is 2627 or 2601 ? "بيانات مكررة" : ex.Message);
        }
        catch (Exception)
        {
            return OperationResult.Failure(GenericErrorMessage);
        }
    }

    private static string StatusToDb(OrderStatus status) => status switch
    {
        OrderStatus.Sold => "sold",
        OrderStatus.Ready => "ready",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
