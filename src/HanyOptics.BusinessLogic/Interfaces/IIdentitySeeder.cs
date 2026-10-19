namespace HanyOptics.BusinessLogic.Interfaces;

// Ensures the "Admin"/"User" Identity roles exist and that a default admin login is
// available on first run. Separate from - and never touches - the business `users` table.
public interface IIdentitySeeder
{
    Task SeedAsync();
}
