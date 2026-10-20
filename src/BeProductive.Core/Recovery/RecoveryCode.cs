using System.Security.Cryptography;
using System.Text;

namespace BeProductive.Core.Recovery;

/// <summary>
/// A recovery code is 16 Crockford base32 characters (80 random bits), shown as XXXX-XXXX-XXXX-XXXX.
/// The alphabet leaves out I, L, O and U so codes read aloud or handwritten don't get mixed up.
/// </summary>
public static class RecoveryCode
{
    public const int Length = 16;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int GroupSize = 4;

    /// <summary>Returns a new code in normalized form (no dashes).</summary>
    public static string Generate()
    {
        var characters = new char[Length];
        for (var i = 0; i < Length; i++)
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(characters);
    }

    public static string Format(string normalized) =>
        string.Join('-', normalized.Chunk(GroupSize).Select(group => new string(group)));

    /// <summary>
    /// Accepts any case, spaces and dashes, and the usual look-alikes (O for 0, I and L for 1).
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (input is null)
            return false;

        var builder = new StringBuilder(Length);
        foreach (var character in input)
        {
            if (character == '-' || char.IsWhiteSpace(character))
                continue;

            var upper = char.ToUpperInvariant(character) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            if (!Alphabet.Contains(upper))
                return false;
            builder.Append(upper);
        }

        if (builder.Length != Length)
            return false;

        normalized = builder.ToString();
        return true;
    }
}
