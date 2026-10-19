using HanyOptics.Domain.Enums;
namespace HanyOptics.Domain.Entities;

public class Order
{
    public int OrderId { get; set; }
    public int BranchId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int CreatedBy { get; set; }
    public int? DoctorId { get; set; }
    public DateTime OrderDate { get; set; }
    public DeliveryType DeliveryType { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }

    // Persisted computed column (total_amount - paid_amount) - read-only from EF's perspective
    public decimal RemainingAmount { get; set; }

    public string? Notes { get; set; }
    public DateTime? DeliveredAt { get; set; }

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<OrderStatusLog> StatusLogs { get; set; } = new List<OrderStatusLog>();
}
