using System.Buffers.Binary;
using System.Security.Cryptography;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Password hashing shared by every controller.
    ///
    /// New passwords: PBKDF2 with HMAC-SHA512, a random 16 byte salt and <see cref="Iterations"/> rounds. The stored
    /// hash is 1 byte format + 4 bytes rounds + 64 bytes key, so the number of rounds can be raised later without
    /// breaking the passwords saved before.
    ///
    /// Passwords saved before version 3.2 are one single HMAC-SHA512 (64 bytes): fast to compute, and so fast to
    /// guess for anybody who gets hold of the database. They still verify; <see cref="NeedsUpgrade"/> tells the
    /// sign-in to save the password again in the new form, which is the only moment the clear password is at hand.
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>Rounds for new passwords (OWASP's figure for PBKDF2-HMAC-SHA512).</summary>
        public const int Iterations = 210_000;

        private const byte Format = 1;
        private const int SaltSize = 16;
        private const int KeySize = 64;
        private const int LegacySize = 64;

        public static void Create(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            passwordSalt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, passwordSalt, Iterations, HashAlgorithmName.SHA512, KeySize);

            passwordHash = new byte[1 + 4 + KeySize];
            passwordHash[0] = Format;
            BinaryPrimitives.WriteInt32BigEndian(passwordHash.AsSpan(1, 4), Iterations);
            key.CopyTo(passwordHash, 5);
        }

        public static bool Verify(string? password, byte[]? passwordHash, byte[]? passwordSalt)
        {
            if (string.IsNullOrEmpty(password) || passwordHash == null || passwordSalt == null || passwordSalt.Length == 0)
            {
                return false;
            }

            if (IsCurrentFormat(passwordHash))
            {
                var iterations = BinaryPrimitives.ReadInt32BigEndian(passwordHash.AsSpan(1, 4));
                if (iterations < 1)
                {
                    return false;
                }

                var key = Rfc2898DeriveBytes.Pbkdf2(password, passwordSalt, iterations, HashAlgorithmName.SHA512, KeySize);
                return CryptographicOperations.FixedTimeEquals(key, passwordHash.AsSpan(5, KeySize));
            }

            if (passwordHash.Length != LegacySize)
            {
                return false; // neither form: an empty or damaged row
            }

            using (var hmac = new HMACSHA512(passwordSalt))
            {
                var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return CryptographicOperations.FixedTimeEquals(computedHash, passwordHash);
            }
        }

        /// <summary>True for a password in the old form, or saved with fewer rounds than new passwords get today.</summary>
        public static bool NeedsUpgrade(byte[]? passwordHash)
        {
            if (passwordHash == null || !IsCurrentFormat(passwordHash))
            {
                return true;
            }

            return BinaryPrimitives.ReadInt32BigEndian(passwordHash.AsSpan(1, 4)) < Iterations;
        }

        /// <summary>
        /// Does the same amount of work as checking a password, for a name that has no account. Without it the
        /// answer for an unknown name comes back faster than for a real one, which tells a stranger who is registered.
        /// </summary>
        public static void SpendTime(string? password)
        {
            Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, DummySalt, Iterations, HashAlgorithmName.SHA512, KeySize);
        }

        private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(SaltSize);

        private static bool IsCurrentFormat(byte[] passwordHash)
        {
            return passwordHash.Length == 1 + 4 + KeySize && passwordHash[0] == Format;
        }
    }
}
