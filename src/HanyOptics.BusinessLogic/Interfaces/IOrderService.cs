using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Interfaces;

public interface IOrderService
{
    // Full detail (items + prescriptions, payments, status log) for the order-detail popup.
    Task<Order?> GetByIdAsync(int orderId);
    Task<IReadOnlyList<Order>> GetAllAsync(int take = 50);
    Task<int> CreateOrderAsync(Order order);
    Task<Doctor?> GetDoctorByIdAsync(int doctorId);

    // Flat, filterable listing used by Orders/Index and the Customers/Index detail panel.
    Task<IReadOnlyList<OrderListItem>> GetOrderListAsync(OrderStatus? status = null, DeliveryType? deliveryType = null, int? customerId = null);

    // Wraps sp_update_order_status directly - no business rules are re-implemented here,
    // the SP owns every transition guard (terminal states, ready->cancelled block, must
    // have an active item, etc.) and raises a clear Arabic error when it rejects one.
    Task<OperationResult> UpdateOrderStatusAsync(int orderId, OrderStatus newStatus, string? notes = null);

    // Wraps sp_swap_frame with @old_frame_disposition fixed to 'damaged' - the SP already
    // logs the loss to frame_damage_log and reserves the replacement frame; no restock
    // logic needed here since the damaged frame's stock was already decremented at sale.
    Task<OperationResult> SwapDamagedFrameAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes = null);
}
