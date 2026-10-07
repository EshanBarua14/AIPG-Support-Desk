using System.ComponentModel.DataAnnotations;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;

namespace XFLCSMS.Models.Admin
{
    /// <summary>
    /// "New user" form of an administrator (platform admin: anybody; house admin: people of the own house).
    /// The account is active at once: no token, no e-mail. Same rules as the public registration form
    /// (Models/Register/UserRegisterRequest.cs).
    /// </summary>
    public class NewUser
    {
        [Required(ErrorMessage = "Enter the full name.")]
        [RegularExpression(@"^[a-zA-Z\s.]+$", ErrorMessage = "Only letters, spaces and dots are allowed.")]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the email address.")]
        [EmailAddress(ErrorMessage = "This is not an email address.")]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the phone number.")]
        [RegularExpression("^[0-9+-]+$", ErrorMessage = "Digits, + and - only.")]
        [StringLength(30)]
        public string PhonNumber { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Designation { get; set; }

        /// <summary>A house admin cannot choose: it is always the own house (the posted value is ignored).</summary>
        [Required(ErrorMessage = "Choose the brokerage house.")]
        public int? BrokerageId { get; set; }

        [Required(ErrorMessage = "Choose the branch.")]
        public int? BranchId { get; set; }

        [Required(ErrorMessage = "Enter the employee ID.")]
        [StringLength(15, MinimumLength = 1, ErrorMessage = "Employee ID must be 1 to 15 characters.")]
        public string EmployeeId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a user name.")]
        [StringLength(25, MinimumLength = 5, ErrorMessage = "User name must be 5 to 25 characters.")]
        [RegularExpression(@"^[^\s@]+$", ErrorMessage = "A user name has no spaces and no @.")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a password.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [RegularExpression(SetPassword.Rule, ErrorMessage = SetPassword.RuleMessage)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the password again.")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>One of the roles the administrator may give (see CsmsController.AssignableRoles).</summary>
        public Role Role { get; set; } = Role.HouseUser;

        // Choices shown on the form (not posted).
        public List<Brokerage> Brokerages { get; set; } = new();
        public List<Branchh> Branches { get; set; } = new();
        public List<Role> Roles { get; set; } = new();
        /// <summary>Set for a house admin: the house is fixed.</summary>
        public string? FixedHouseName { get; set; }
    }

    /// <summary>"Edit user" form: the details of an existing account, its role and whether it may sign in.</summary>
    public class EditUserForm
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter the full name.")]
        [RegularExpression(@"^[a-zA-Z\s.]+$", ErrorMessage = "Only letters, spaces and dots are allowed.")]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the email address.")]
        [EmailAddress(ErrorMessage = "This is not an email address.")]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the phone number.")]
        [RegularExpression("^[0-9+-]+$", ErrorMessage = "Digits, + and - only.")]
        [StringLength(30)]
        public string PhonNumber { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Designation { get; set; }

        [Required(ErrorMessage = "Enter the employee ID.")]
        [StringLength(15, MinimumLength = 1, ErrorMessage = "Employee ID must be 1 to 15 characters.")]
        public string EmployeeId { get; set; } = string.Empty;

        public int? BrokerageId { get; set; }

        [Required(ErrorMessage = "Choose the branch.")]
        public int? BranchId { get; set; }

        public Role Role { get; set; } = Role.HouseUser;

        /// <summary>Active (may sign in) or disabled.</summary>
        public bool UStatus { get; set; } = true;

        // Shown on the form (not posted).
        public string UserName { get; set; } = string.Empty;
        public DateTime? VerifiedAt { get; set; }
        public bool IsSelf { get; set; }
        /// <summary>Tickets raised by this user: the house of such an account cannot be changed.</summary>
        public int TicketCount { get; set; }
        public bool CanChangeHouse { get; set; }
        public List<Brokerage> Brokerages { get; set; } = new();
        public List<Branchh> Branches { get; set; } = new();
        public List<Role> Roles { get; set; } = new();
    }

    /// <summary>An administrator gives a user a new password (the user has lost it and the reset e-mail does not arrive).</summary>
    public class SetPassword
    {
        public const string Rule = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{4,}$";
        public const string RuleMessage = "Password needs a small letter, a capital letter, a digit and one of @ $ ! % * ? & (no other characters).";

        public int Id { get; set; }

        [Required(ErrorMessage = "Enter a password.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [RegularExpression(Rule, ErrorMessage = RuleMessage)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the password again.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
