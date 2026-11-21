using System.Security.Cryptography;
using System.Text;

namespace BatonE2E.Core.Engine;

/// <summary>source接続の先頭部（要求行とヘッダー）。PUT / SOURCE / 旧形式 SOURCE pass /mount ICE/1.0。</summary>
internal sealed class SourceRequest
{
    public string Method = "";
    public string Target = "";
    public string Proto = "";
    public bool IceStyle;
    public string? IcePassword;
    public bool Invalid;
    public string LastToken = "";
    public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<SourceRequest?> ReadAsync(BufferedSource src, CancellationToken ct)
    {
        var first = await src.ReadLineAsync(2048, ct);
        if (first == null) return null;
        var req = new SourceRequest();
        var p = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) { req.Invalid = true; return req; }
        req.Method = p[0].Length > 16 ? p[0][..16] : p[0];
        req.LastToken = p[^1].Length > 16 ? p[^1][..16] : p[^1];

        if (p.Length >= 4 && p[0] == "SOURCE" && p[^1].StartsWith("ICE/", StringComparison.Ordinal))
        {
            req.IceStyle = true;
            req.IcePassword = string.Join(' ', p[1..^2]);
            req.Target = p[^2];
            req.Proto = p[^1];
        }
        else if (p.Length == 3 && p[2].StartsWith("HTTP/", StringComparison.Ordinal))
        {
            req.Target = p[1]; req.Proto = p[2];
        }
        else { req.Invalid = true; return req; }

        for (var i = 0; i < 100; i++)
        {
            var h = await src.ReadLineAsync(4096, ct);
            if (h == null) return req;
            if (h.Length == 0) return req;
            var c = h.IndexOf(':');
            if (c > 0) req.Headers[h[..c].Trim()] = h[(c + 1)..].Trim();
        }
        req.Invalid = true;   // ヘッダーが多すぎる
        return req;
    }

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    /// <summary>Basic認証（ユーザー名 source）の検証。</summary>
    public bool CheckBasic(string expectedPassword)
    {
        var h = Header("Authorization");
        if (h == null || !h.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        string text;
        try { text = Encoding.UTF8.GetString(Convert.FromBase64String(h[6..].Trim())); }
        catch (FormatException) { return false; }
        var i = text.IndexOf(':');
        if (i < 0) return false;
        // ユーザー名とパスワードの両方を、判定結果が時間差に出ない比較で検査
        var userOk = SecretEquals(text[..i], "source");
        var passOk = SecretEquals(text[(i + 1)..], expectedPassword);
        return userOk & passOk;
    }

    public static bool SecretEquals(string a, string b)
    {
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}
