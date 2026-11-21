using System.Security.Cryptography;
using System.Text;

namespace BatonE2E.Core.Crypto;

public static class KeyDerivation
{
    /// <summary>
    /// PBKDF2-HMAC-SHA256。パスワードは Unicode NFC 正規化後の UTF-8
    /// （ブラウザ側も normalize('NFC') + TextEncoder で同じ値にする）。
    /// </summary>
    public static byte[] Derive(string password, byte[] salt, int iterations)
    {
        var pw = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(pw, salt, iterations, HashAlgorithmName.SHA256, CryptoConstants.KeyLength);
        }
        finally { CryptographicOperations.ZeroMemory(pw); }
    }
}
