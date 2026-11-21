using System.Security.Cryptography;
using System.Text;

namespace BatonE2E.Core.Security;

public static class PassphraseGenerator
{
    // 紛らわしい文字（I, O, 0, 1）を除いた32文字。1文字あたり5ビット
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>既定は 4文字×5組 = 100ビット。区切りは '-'。</summary>
    public static string Generate(int groups = 5, int perGroup = 4)
    {
        var sb = new StringBuilder();
        for (var g = 0; g < groups; g++)
        {
            if (g > 0) sb.Append('-');
            for (var i = 0; i < perGroup; i++) sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return sb.ToString();
    }
}
