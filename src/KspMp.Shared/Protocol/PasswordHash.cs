using System;
using System.Security.Cryptography;
using System.Text;

namespace KspMp.Shared.Protocol
{
    /// <summary>
    /// Turns a server password into the value sent over the wire.
    ///
    /// The point of hashing is narrow and worth being clear about: it keeps the password itself off the
    /// network, which matters because people reuse them. It is not a strong authentication scheme. There is no
    /// challenge from the server, so anyone who can read one join packet can replay it and get in. Treat a
    /// server password as a lock on the door rather than a guarantee about who is behind it.
    /// </summary>
    public static class PasswordHash
    {
        /// <summary>Hex SHA-256 of the password, or an empty string when there is no password.</summary>
        public static string Of(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                var text = new StringBuilder(bytes.Length * 2);
                for (var i = 0; i < bytes.Length; i++) text.Append(bytes[i].ToString("x2"));
                return text.ToString();
            }
        }

        /// <summary>
        /// Compares two hashes without giving away how much of the prefix matched. The timing of a join is
        /// not really attackable, but a constant-time compare costs nothing and avoids having to argue it.
        /// </summary>
        public static bool Matches(string expectedPassword, string offeredHash)
        {
            var expected = Of(expectedPassword);
            if (expected.Length == 0) return true;
            if (string.IsNullOrEmpty(offeredHash) || offeredHash.Length != expected.Length) return false;
            var difference = 0;
            for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ offeredHash[i];
            return difference == 0;
        }
    }
}
