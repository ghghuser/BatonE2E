namespace BatonE2E.Core.Crypto;

/// <summary>暗号仕様の定数（仕様 v1）。視聴側（ブラウザ・ヘルパー）と一致させること。</summary>
public static class CryptoConstants
{
    public const int Version = 1;
    public const string KdfName = "PBKDF2-SHA256";
    public const int DefaultIterations = 600_000;
    public const int MinIterations = 100_000;      // 視聴側が受け入れる下限（弱化・誤設定の防止）
    public const int MaxIterations = 10_000_000;   // 視聴側が受け入れる上限（過負荷の防止）
    public const int SaltLength = 16;
    public const int SessionIdLength = 4;
    public const int KeyLength = 32;               // AES-256
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int SeqLength = 8;
    public const int MaxPayload = 1 << 20;         // 1チャンクの平文上限（1MiB）

    /// <summary>AADの先頭に付ける固定文字列。</summary>
    public static readonly byte[] AadPrefix = "E2ERv1"u8.ToArray();

    /// <summary>確認ブロックの平文（先頭の種別 0x00 を除く部分）。</summary>
    public static readonly byte[] ConfirmText = "E2ERADIO1"u8.ToArray();
}

/// <summary>平文の先頭1バイト（暗号文の内側にあり、GCMで改ざん検知される）。</summary>
public enum FrameType : byte
{
    Confirm = 0x00,
    Audio = 0x01,
    Metadata = 0x02,
    FormatChange = 0x03,
}

public sealed class E2eCryptoException : Exception
{
    public E2eCryptoException(string message) : base(message) { }
}
