using System.ComponentModel.DataAnnotations;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

// Step 1 revisited ("رجوع" from step 2) for an order that already exists. Only phone
// (customer resolution) and delivery type are actually editable - invoice number is
// immutable once set, and doctor is fixed at order creation (sp_create_order has no
// update path for it), so it's carried through as a hidden value rather than exposed
// for editing.
public class EditOrderCustomerRequest
{
    public int OrderId { get; set; }

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    [Display(Name = "اسم الزبون")]
    public string? CustomerName { get; set; }

    [Display(Name = "نوع التسليم")]
    public DeliveryType DeliveryType { get; set; }

    public int? DoctorId { get; set; }
}
