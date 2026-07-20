using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Interfaces;

public interface IOrderService
{
    Task<Order?> GetByIdAsync(int orderId);
    Task<IReadOnlyList<Order>> GetAllAsync(int take = 50);
    Task<int> CreateOrderAsync(Order order);

    // Flat, filterable listing used by Orders/Index and the Customers/Index detail panel.
    Task<IReadOnlyList<OrderListItem>> GetOrderListAsync(OrderStatus? status = null, DeliveryType? deliveryType = null, int? customerId = null);
}
