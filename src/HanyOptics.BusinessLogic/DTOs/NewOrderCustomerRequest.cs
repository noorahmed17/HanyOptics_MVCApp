using System.ComponentModel.DataAnnotations;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

public class NewOrderCustomerRequest
{
    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    // Pre-filled from the phone lookup if the customer already exists; otherwise
    // whatever the user types is used to create a new customer (optional either way).
    [Display(Name = "اسم الزبون")]
    public string? CustomerName { get; set; }

    [Required(ErrorMessage = "رقم الفاتورة مطلوب")]
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Display(Name = "نوع التسليم")]
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Normal;

    [Display(Name = "الطبيب المُحوِّل")]
    public int? DoctorId { get; set; }
}
