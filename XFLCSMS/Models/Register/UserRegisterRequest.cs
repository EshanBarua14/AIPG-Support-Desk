
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XFLCSMS.Models.Register
{
    public class UserRegisterRequest
    {
        

        [Required(ErrorMessage = "Required")]
        //[RegularExpression(@"^[a-zA-Z.]+$", ErrorMessage = "Only letters are allowed.")]
        [RegularExpression(@"^[a-zA-Z\s.]+$", ErrorMessage = "Only letters and spaces are allowed.")]

        [StringLength(100, ErrorMessage = "The name is too long.")]
        [Display(Name = "Full Name")]
        public String FullName { get; set; } = string.Empty;

        [Required, EmailAddress(ErrorMessage = "Required")]
        [StringLength(200, ErrorMessage = "The email address is too long.")]
        public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "Required")]
        [RegularExpression("^[0-9+-]+$", ErrorMessage = "Invalid Phone Number")]
        [StringLength(30, ErrorMessage = "The phone number is too long.")]
        [Display(Name = "Phone Number")]
        public string PhonNumber { get; set; } = string.Empty;
    

        public string Designation { get; set; } = string.Empty;
        [Required]
        [Display(Name = "Organization")]
        public int BrokerageHouseName { get; set; }
        [Required]
        public int Branch { get; set; }

        [Required]
        [StringLength(15, MinimumLength = 1, ErrorMessage = "Employee ID must be 1 to 15 characters.")]
        [Display(Name = "Employee ID")]
        public string EmployeeId { get; set; } = string.Empty;
        [Required]
        [StringLength(25, MinimumLength = 5, ErrorMessage = "User name must be 5 to 25 characters.")]
        // The sign-in box takes user name or email, so a user name must not look like somebody's email address.
        [RegularExpression(@"^[^\s@]+$", ErrorMessage = "A user name has no spaces and no @.")]
        [Display(Name = "User Name")]
        public string UserName { get; set; } = string.Empty;
        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [XFLCSMS.Services.StrongPassword(NameProperties = new[] { nameof(UserName), nameof(Email) })]
        public string Password { get; set; } = string.Empty;

        // Not stored: only compared with Password (the page checked this in JavaScript only).
        [Required(ErrorMessage = "Please confirm your password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [NotMapped]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        // [Required] on a bool is always satisfied, so an unticked box used to be accepted.
        [Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to the terms & conditions.")]
        public bool Terms { get; set; }
    }
}
