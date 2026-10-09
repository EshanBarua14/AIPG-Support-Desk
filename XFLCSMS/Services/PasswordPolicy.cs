using System.ComponentModel.DataAnnotations;

namespace XFLCSMS.Services
{
    /// <summary>
    /// What a new password must look like. One place, used by every form that sets a password: registration,
    /// change password, reset with a token, an administrator creating an account or setting a password.
    /// Passwords that were saved before stay valid until they are changed.
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinimumLength = 10;
        public const int MaximumLength = 100;

        /// <summary>The rule in one line, for the hint under a password box.</summary>
        public const string Hint = "10 or more characters with at least one letter and one digit. Not your user name, not a common password.";

        // Passwords people really choose when a form asks for "10 characters, a letter and a digit".
        private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
        {
            "password12", "password123", "password1234", "password12345", "passw0rd123", "p@ssw0rd123", "p@ssword123",
            "1234567890a", "a1234567890", "abc1234567", "abcd123456", "abcde12345", "qwerty12345", "qwertyuiop1", "1q2w3e4r5t",
            "1qaz2wsx3edc", "q1w2e3r4t5", "q1w2e3r4t5y6", "iloveyou123", "welcome123", "welcome1234", "letmein1234",
            "admin12345", "admin123456", "admin@12345", "admin@1234", "administrator1", "changeme123", "default12345",
            "bangladesh1", "bangladesh123", "dhaka12345", "xpert12345", "xpert@12345", "xfl1234567", "xfl@123456", "csms123456", "csms@12345", "aipg123456", "aipg@12345", "aipgsupport1", "supportdesk1"
        };

        /// <summary>Null when the password is acceptable; otherwise what is wrong with it, as a sentence for the form.</summary>
        public static string? Problem(string? password, params string?[] namesOfTheOwner)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            {
                return "The password must be at least " + MinimumLength + " characters long.";
            }

            if (password.Length > MaximumLength)
            {
                return "The password is too long (at most " + MaximumLength + " characters).";
            }

            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            {
                return "The password needs at least one letter and one digit.";
            }

            if (password.Distinct().Count() < 5)
            {
                return "The password repeats the same few characters. Choose one that is harder to guess.";
            }

            if (Common.Contains(password))
            {
                return "This password is on the list of common passwords. Choose another one.";
            }

            foreach (var name in namesOfTheOwner)
            {
                // the part of an e-mail address before the @ counts as a name too
                var bare = (name ?? string.Empty).Split('@')[0].Trim();
                if (bare.Length >= 4 && password.Contains(bare, StringComparison.OrdinalIgnoreCase))
                {
                    return "The password must not contain your user name or e-mail address.";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Puts <see cref="PasswordPolicy"/> on a form field. <see cref="NameProperties"/> are other properties of the
    /// same form (user name, e-mail) the password must not contain.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class StrongPasswordAttribute : ValidationAttribute
    {
        public string[] NameProperties { get; set; } = Array.Empty<string>();

        protected override ValidationResult? IsValid(object? value, ValidationContext context)
        {
            var password = value as string;
            if (string.IsNullOrEmpty(password))
            {
                return ValidationResult.Success; // "required" is a rule of its own
            }

            var names = NameProperties
                .Select(name => context.ObjectType.GetProperty(name)?.GetValue(context.ObjectInstance) as string)
                .ToArray();
            var problem = PasswordPolicy.Problem(password, names);
            return problem == null ? ValidationResult.Success : new ValidationResult(problem, new[] { context.MemberName ?? string.Empty });
        }
    }
}
