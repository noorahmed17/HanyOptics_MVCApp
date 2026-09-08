using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Staff accounts, created by whoever runs the shop.
//
// This exists because self-registration was removed: a back office is not something a
// visitor lets themselves into. Everything here is admin-only at the controller.
public interface IUserAdminService
{
    Task<IReadOnlyList<StaffUserListItem>> ListAsync();

    // Creates the business `users` row and the Identity login together, sharing one id.
    //
    // That shared id is the whole point: every stored procedure stamps created_by /
    // received_by / changed_by from the signed-in user's NameIdentifier claim, which is the
    // Identity id. If the two stores drifted apart, an order would be attributed to a staff
    // row that does not exist.
    Task<CreateUserOutcome> CreateAsync(CreateUserRequest request);
}
