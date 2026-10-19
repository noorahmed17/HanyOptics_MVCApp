using HanyOptics.Domain.Enums;

namespace HanyOptics.Domain.Entities;

public class OrderStatusLog
{
    public int LogId { get; set; }
    public int OrderId { get; set; }
    public OrderStatus? OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public DateTime ChangedAt { get; set; }
    public int ChangedBy { get; set; }
    public string? Notes { get; set; }

    public Order? Order { get; set; }
}
