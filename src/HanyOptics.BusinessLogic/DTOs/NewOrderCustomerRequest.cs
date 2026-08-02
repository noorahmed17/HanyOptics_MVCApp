using System.ComponentModel.DataAnnotations;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

public class NewOrderCustomerRequest : IValidatableObject
{
    // Optional: a customer may be identified by phone, by name, or by neither - in the
    // last case the user has to tick "زبون عابر" (see IsWalkIn).
    [Display(Name = "رقم الهاتف")]
    public string? Phone { get; set; }

    // Pre-filled from the phone lookup if the customer already exists; otherwise
    // whatever the user types is used to create a new customer (optional either way).
    [Display(Name = "اسم الزبون")]
    public string? CustomerName { get; set; }

    // A one-off customer who gave neither phone nor name. The service substitutes the
    // standing placeholder identity (WalkInPhone/WalkInName) so these orders all collapse
    // onto a single "زبون عابر" customer row instead of littering the table.
    [Display(Name = "زبون عابر")]
    public bool IsWalkIn { get; set; }

    [Required(ErrorMessage = "رقم الفاتورة مطلوب")]
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Display(Name = "نوع التسليم")]
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Normal;

    [Display(Name = "الطبيب المُحوِّل")]
    public int? DoctorId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsWalkIn && string.IsNullOrWhiteSpace(Phone) && string.IsNullOrWhiteSpace(CustomerName))
            yield return new ValidationResult(WalkInCustomer.MissingIdentityMessage, new[] { nameof(Phone) });
    }
}
