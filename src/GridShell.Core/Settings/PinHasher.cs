using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GridShell.Core.Settings;

/// <summary>Hashes the Kiosk Mode PIN before it's persisted - unlike every other secret in
/// AppSettings (RealDebridApiToken, IGDB credentials, RetroAchievementsToken), which get stored
/// in plain JSON, a PIN directly guards turning a lockdown off, so it shouldn't sit in
/// plaintext. Random salt + SHA256(salt + pin), stored as "&lt;saltBase64&gt;:&lt;hashBase64&gt;".</summary>
public static class PinHasher
{
    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = ComputeHash(salt, pin);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var actualHash = ComputeHash(salt, pin);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ComputeHash(byte[] salt, string pin) =>
        SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(pin)).ToArray());
}
