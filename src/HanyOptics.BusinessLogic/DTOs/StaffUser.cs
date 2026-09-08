using System.ComponentModel.DataAnnotations;

namespace HanyOptics.BusinessLogic.Models;

// A new staff account, as the admin fills it in.
//
// This is the only way an account gets created - self-registration was removed, because a
// shop's back office is not something a visitor should be able to let themselves into.
public class CreateUserRequest
{
    [Required(ErrorMessage = "أدخل الاسم")]
    [StringLength(200, ErrorMessage = "الاسم طويل أوي")]
    public string? FullName { get; set; }

    // Used as both the login and the business `users`.username, so one person is one row in
    // each store and the two can be matched later.
    [Required(ErrorMessage = "أدخل الإيميل")]
    [EmailAddress(ErrorMessage = "الإيميل مش صحيح")]
    [StringLength(100)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "أدخل كلمة السر")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة السر لازم تكون 6 حروف على الأقل")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required(ErrorMessage = "أعد كتابة كلمة السر")]
    [Compare(nameof(Password), ErrorMessage = "كلمتا السر مش متطابقتين")]
    [DataType(DataType.Password)]
    public string? ConfirmPassword { get; set; }

    // An admin sees costs, margins and every report. Defaults to off so the dangerous
    // choice is the one you have to make deliberately.
    public bool IsAdmin { get; set; }
}

public class CreateUserOutcome
{
    public bool Succeeded { get; init; }
    public int UserId { get; init; }

    // Identity can refuse for several reasons at once (weak password, duplicate email), and
    // the admin should see all of them rather than fixing one and being told the next.
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static CreateUserOutcome Success(int userId) => new() { Succeeded = true, UserId = userId };
    public static CreateUserOutcome Failure(params string[] errors) => new() { Errors = errors };
    public static CreateUserOutcome Failure(IEnumerable<string> errors) => new() { Errors = errors.ToList() };
}

// One row in the staff list.
public class StaffUserListItem
{
    public string Id { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }

    // Whether the Identity account has a matching row in the business `users` table. These
    // are two separate stores tied together by a shared id, and an account missing its
    // business row cannot have its work attributed - worth surfacing rather than hiding.
    public bool HasBusinessRow { get; set; }
}
