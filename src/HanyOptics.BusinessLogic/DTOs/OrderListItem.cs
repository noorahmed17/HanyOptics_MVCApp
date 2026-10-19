using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

// Flat row for the orders table (Orders/Index, Customers/Index detail panel) - pulls in
// customer phone (not on Order itself) and the first item's type, which plain
// IOrderService.GetAllAsync doesn't provide.
public class OrderListItem
{
    public int OrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public IReadOnlyList<OrderItemType> ItemTypes { get; set; } = Array.Empty<OrderItemType>();
    public int ItemCount { get; set; }
    public DeliveryType DeliveryType { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
}
