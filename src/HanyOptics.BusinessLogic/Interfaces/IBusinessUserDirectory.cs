namespace HanyOptics.BusinessLogic.Interfaces;

// The business `users` table is the one every operational FK points at
// (orders.created_by, payments.received_by, order_status_log.changed_by,
// frame_damage_log.recorded_by, ...). Identity's AspNetUsers is a separate, login-only
// store, so each staff account has to exist in both.
//
// The two are kept in sync by giving the Identity account the *same* primary key as its
// business row: the business row is inserted first, and its IDENTITY-generated user_id is
// then used verbatim as the ApplicationUser.Id string.
public interface IBusinessUserDirectory
{
    // Inserts a row into `users` and returns the generated user_id.
    // `isAdmin` picks the value for the role column, which the CK__users__role check
    // constraint restricts to 'admin' or 'sales'.
    Task<int> CreateAsync(string fullName, string username, bool isAdmin);

    // Used by the seeder to adopt a business row that already exists (the schema script's
    // original "admin" staff row) rather than creating a duplicate for the same person.
    Task<int?> FindIdByUsernameAsync(string username);
}
