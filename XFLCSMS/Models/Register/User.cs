using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Todos;

namespace XFLCSMS.Models.Register
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public String FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhonNumber { get; set; } = string.Empty;
        public string? Designation { get; set; } = string.Empty;
        public int BrokerageHouseName { get; set; }
        public int BrokerageHouseAcronym { get; set; }
        public int Branch { get; set; }
        public string? Department { get; set; }= string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public byte[] PasswordHash { get; set; } = new byte[32];
        public byte[] PasswordSalt { get; set; } = new byte[32];
        public string? VerificationToken { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? PasswordResetToken { get; set; }
        public DateTime? ResetTokenExpires { get; set; }

        public bool UCatagory { get; set; } = false;
        public bool UType { get; set; } = false;
        public bool UStatus { get; set; } = false;

        public bool Terms { get; set; }

        /// <summary>Wrong passwords (or activation tokens) in a row. Back to 0 with the next right one.</summary>
        public int FailedAttempts { get; set; }

        /// <summary>Set after too many wrong attempts: until then nobody can sign in to the account, not even with the right password.</summary>
        public DateTime? LockedUntil { get; set; }

        /// <summary>
        /// The password was chosen by somebody else (first administrator from the settings file, account created or
        /// password set by an administrator): after signing in, the owner has to choose a new one before anything else.
        /// </summary>
        public bool MustChangePassword { get; set; }

        public ICollection<IssueTable> issue { get; set; }
        public ICollection<Todo> Todos { get; set; } = new List<Todo>();
    }
}
