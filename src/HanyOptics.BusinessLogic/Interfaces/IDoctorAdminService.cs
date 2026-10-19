using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;

namespace HanyOptics.BusinessLogic.Interfaces;

// Backs the admin-only doctors page - لوحة الأدمن's counterpart for referring doctors
// instead of staff accounts. Doctors carry no login and no role, so this is a plain CRUD
// surface over the doctors table rather than the dual-store dance IUserAdminService does;
// list + create only for now, matching how لوحة الأدمن itself only creates staff accounts
// (no edit/deactivate) until there's an actual need for more.
public interface IDoctorAdminService
{
    Task<IReadOnlyList<Doctor>> ListAsync();
    Task<OperationResult> CreateAsync(CreateDoctorRequest request);
}
