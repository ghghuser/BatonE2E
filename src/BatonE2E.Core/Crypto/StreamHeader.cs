using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatonE2E.Core.Crypto;

/// <summary>WebSocketの最初のメッセージ（JSON）。視聴側はこの値に従う。</summary>
public sealed class StreamHeader
{
    [JsonPropertyName("v")] public int V { get; set; } = CryptoConstants.Version;
    [JsonPropertyName("kdf")] public string Kdf { get; set; } = CryptoConstants.KdfName;
    [JsonPropertyName("iter")] public int Iter { get; set; } = CryptoConstants.DefaultIterations;
    [JsonPropertyName("salt")] public string Salt { get; set; } = "";
    [JsonPropertyName("sid")] public string Sid { get; set; } = "";
    [JsonPropertyName("mime")] public string Mime { get; set; } = "audio/mpeg";

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>解析に失敗したら null。</summary>
    public static StreamHeader? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<StreamHeader>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>仕様 v1 に合うか検査し、塩とセッションIDを返す。不正なら例外。</summary>
    internal (byte[] salt, byte[] sid) Validate(int minIterations)
    {
        if (V != CryptoConstants.Version) throw new E2eCryptoException("unsupported-version");
        if (Kdf != CryptoConstants.KdfName) throw new E2eCryptoException("unsupported-kdf");
        if (Iter < minIterations || Iter > CryptoConstants.MaxIterations)
            throw new E2eCryptoException("iterations-out-of-range");
        byte[] salt, sid;
        try { salt = Convert.FromBase64String(Salt); sid = Convert.FromBase64String(Sid); }
        catch (FormatException) { throw new E2eCryptoException("bad-base64"); }
        if (salt.Length < CryptoConstants.SaltLength || salt.Length > 64) throw new E2eCryptoException("bad-salt");
        if (sid.Length != CryptoConstants.SessionIdLength) throw new E2eCryptoException("bad-sid");
        return (salt, sid);
    }
}
