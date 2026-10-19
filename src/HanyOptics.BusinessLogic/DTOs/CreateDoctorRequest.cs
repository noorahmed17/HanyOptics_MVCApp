using System.ComponentModel.DataAnnotations;

namespace HanyOptics.BusinessLogic.Models;

// One doctor being entered by an admin, on the لوحة الأدمن-style doctors page. Doctors are
// pure reference data - no login, no role, nothing that touches Identity - so unlike
// CreateUserRequest this is the whole of what's needed to create one.
public class CreateDoctorRequest
{
    [Required(ErrorMessage = "أدخل اسم الطبيب")]
    [StringLength(100, ErrorMessage = "الاسم طويل جداً")]
    public string Name { get; set; } = string.Empty;

    // Optional - a doctor entered before their clinic/phone is known should still be
    // selectable in the new-order wizard's doctor dropdown right away.
    [StringLength(150, ErrorMessage = "اسم العيادة طويل جداً")]
    public string? Clinic { get; set; }

    [StringLength(20, ErrorMessage = "رقم الهاتف طويل جداً")]
    public string? Phone { get; set; }
}
