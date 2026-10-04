using System.Security.Cryptography;

namespace XFLCSMS.Services
{
    /// <summary>HMAC-SHA512 password hashing shared by every controller (same scheme the existing users were created with).</summary>
    public static class PasswordHasher
    {
        public static void Create(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512())
            {
                passwordSalt = hmac.Key;
                passwordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            }
        }

        public static bool Verify(string? password, byte[]? passwordHash, byte[]? passwordSalt)
        {
            if (string.IsNullOrEmpty(password) || passwordHash == null || passwordSalt == null || passwordSalt.Length == 0)
            {
                return false;
            }

            using (var hmac = new HMACSHA512(passwordSalt))
            {
                var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return CryptographicOperations.FixedTimeEquals(computedHash, passwordHash);
            }
        }
    }
}
