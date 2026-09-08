using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBusinessUserDirectory _businessUsers;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(
        UserManager<ApplicationUser> userManager,
        IBusinessUserDirectory businessUsers,
        ILogger<UserAdminService> logger)
    {
        _userManager = userManager;
        _businessUsers = businessUsers;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StaffUserListItem>> ListAsync()
    {
        var users = await _userManager.Users.AsNoTracking()
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var rows = new List<StaffUserListItem>(users.Count);

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);

            rows.Add(new StaffUserListItem
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                IsAdmin = roles.Contains(Roles.Admin),

                // The Identity id doubles as the business users.user_id, so an account with
                // no matching row is one whose work cannot be attributed. Surfaced rather
                // than hidden.
                HasBusinessRow = int.TryParse(user.Id, out var id)
                                 && await _businessUsers.FindIdByUsernameAsync(user.Email ?? string.Empty) == id
            });
        }

        return rows;
    }

    public async Task<CreateUserOutcome> CreateAsync(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            return CreateUserOutcome.Failure("أدخل الاسم");

        if (string.IsNullOrWhiteSpace(request.Email))
            return CreateUserOutcome.Failure("أدخل الإيميل");

        if (string.IsNullOrWhiteSpace(request.Password))
            return CreateUserOutcome.Failure("أدخل كلمة السر");

        var email = request.Email.Trim();
        var fullName = request.FullName.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
            return CreateUserOutcome.Failure("فيه حساب بالإيميل ده بالفعل");

        // Order matters. The business `users` row goes in first so its IDENTITY-generated
        // user_id can be reused verbatim as the Identity primary key. That one shared id is
        // what lets every stored procedure stamp created_by / received_by / changed_by
        // straight from the JWT's NameIdentifier claim.
        var businessUserId = await _businessUsers.CreateAsync(fullName, email, request.IsAdmin);

        var user = new ApplicationUser
        {
            Id = businessUserId.ToString(),
            UserName = email,
            Email = email,
            FullName = fullName
        };

        var created = await _userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            // Don't leave a staff row behind that nobody can ever log in as. The delete only
            // removes it while nothing references it, so this can never erase real history.
            await _businessUsers.DeleteIfUnreferencedAsync(businessUserId);

            _logger.LogWarning("Failed to create staff account {Email}: {Errors}",
                email, string.Join("; ", created.Errors.Select(e => e.Description)));

            return CreateUserOutcome.Failure(created.Errors.Select(e => e.Description));
        }

        var role = request.IsAdmin ? Roles.Admin : Roles.User;
        var roleAssigned = await _userManager.AddToRoleAsync(user, role);

        if (!roleAssigned.Succeeded)
        {
            // The login exists but carries no role, which would leave the person able to sign
            // in and see nothing. Better to fail loudly and undo it.
            await _userManager.DeleteAsync(user);
            await _businessUsers.DeleteIfUnreferencedAsync(businessUserId);

            return CreateUserOutcome.Failure(roleAssigned.Errors.Select(e => e.Description));
        }

        _logger.LogInformation("Created staff account {Email} (id {UserId}) as {Role}.",
            email, businessUserId, role);

        return CreateUserOutcome.Success(businessUserId);
    }
}
