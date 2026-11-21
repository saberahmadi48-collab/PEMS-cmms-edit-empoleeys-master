using System.Security.Cryptography;

namespace PEMS.Shared.Security;

/// <summary>
/// One-time passwords for accounts created on someone else's behalf (for example a user created from an employee form).
/// Every call returns a new random value, so no account ever starts with a password that is known in advance or
/// written in the source code (SCRUM-17). Callers must set MustChangePassword and show the value once.
/// </summary>
public static class TemporaryPasswordGenerator
{
    // visually ambiguous characters (0/O, 1/l/I) are left out because the value is read out or typed by a person
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "#$%&*";

    public const int Length = 16;

    /// <summary>Returns a password with at least one upper-case letter, lower-case letter, digit and symbol, so it satisfies any configured policy.</summary>
    public static string Generate()
    {
        var all = Upper + Lower + Digits;
        var chars = new List<char>(Length)
        {
            Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols)
        };
        while (chars.Count < Length) chars.Add(Pick(all));

        // Fisher-Yates so the four guaranteed characters are not always at the front
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
