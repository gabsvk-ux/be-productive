using System.Security.Cryptography;

namespace BeProductive.Core.Recovery;

public static class RecoveryCodeHasher
{
    // Codes carry 80 random bits, so guessing is hopeless even against a fast hash.
    // PBKDF2 with a per-code salt is defence in depth, not the main protection.
    private const int Iterations = 10_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static StoredCode Hash(string normalizedCode)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return new StoredCode
        {
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(normalizedCode, salt, Iterations)),
            Iterations = Iterations,
        };
    }

    public static bool Matches(string normalizedCode, StoredCode stored)
    {
        var expected = Convert.FromBase64String(stored.Hash);
        var actual = Derive(normalizedCode, Convert.FromBase64String(stored.Salt), stored.Iterations);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
