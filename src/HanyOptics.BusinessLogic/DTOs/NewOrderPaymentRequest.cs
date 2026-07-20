using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;


public class NewOrderPaymentRequest
{
    public int OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal Amount { get; set; }
}
