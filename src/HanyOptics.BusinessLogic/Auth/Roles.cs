namespace HanyOptics.BusinessLogic.Auth;

// Identity role names for the web portal login (AspNetRoles) - unrelated to the business
// `users` table's staff `role` column ('admin'/'sales'), which is a separate concept.
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
