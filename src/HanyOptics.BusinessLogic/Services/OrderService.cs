using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;
public class OrderService : IOrderService
{
    private readonly HanyOpticsDbContext _dbContext;

    public OrderService(HanyOpticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Order?> GetByIdAsync(int orderId) =>
        _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

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
}
