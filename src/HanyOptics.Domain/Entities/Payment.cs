using HanyOptics.Domain.Enums;

namespace HanyOptics.Domain.Entities;

public class Payment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentType PaymentType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public DateTime PaidAt { get; set; }
    public int ReceivedBy { get; set; }
    public string? Notes { get; set; }

    public Order? Order { get; set; }
}
