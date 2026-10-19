using System.ComponentModel.DataAnnotations;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

public class NewOrderPaymentRequest
{
    public int OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    // Nullable so "left blank" (invalid) is distinguishable from "typed 0" (valid - means
    // no payment yet, the order still completes with paid_amount = 0).
    [Required(ErrorMessage = "أدخل المبلغ المدفوع — اكتب ٠ لو لسه معندوش دفعة")]
    public decimal? Amount { get; set; }
}
