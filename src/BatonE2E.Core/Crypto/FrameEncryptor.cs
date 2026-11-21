using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BatonE2E.Core.Crypto;

/// <summary>
/// 配信側。1セッション（起動ごと）につき1インスタンス。塩とセッションIDは毎回乱数。
/// フレーム = 連番8バイト(BE) + 暗号文 + GCMタグ16バイト。連番は0=確認、1以降は単調増加。
/// </summary>
public sealed class FrameEncryptor : IDisposable
{
    private readonly AesGcm _aes;
    private readonly byte[] _sid;
    private readonly object _lock = new();
    private long _seq;          // 最後に使った連番（0=確認ブロックのみ使用済み）
    private byte[]? _confirm;

    public StreamHeader Header { get; }

    private FrameEncryptor(byte[] key, byte[] sid, StreamHeader header)
    {
        _aes = new AesGcm(key, CryptoConstants.TagLength);
        _sid = sid;
        Header = header;
        CryptographicOperations.ZeroMemory(key);
    }

    public static FrameEncryptor Create(string password, int iterations = CryptoConstants.DefaultIterations,
                                        string mime = "audio/mpeg", int minIterations = CryptoConstants.MinIterations)
    {
        if (string.IsNullOrEmpty(password)) throw new E2eCryptoException("empty-password");
        var salt = RandomNumberGenerator.GetBytes(CryptoConstants.SaltLength);
        var sid = RandomNumberGenerator.GetBytes(CryptoConstants.SessionIdLength);
        var header = new StreamHeader
        {
            Iter = iterations, Mime = mime,
            Salt = Convert.ToBase64String(salt), Sid = Convert.ToBase64String(sid),
        };
        header.Validate(minIterations);
        return new FrameEncryptor(KeyDerivation.Derive(password, salt, iterations), sid, header);
    }

    /// <summary>連番0の確認ブロック。新規接続ごとに送る（同一平文なので同じ暗号文でよい）。</summary>
    public byte[] ConfirmationFrame()
    {
        lock (_lock)
        {
            return _confirm ??= Seal(0, FrameType.Confirm, CryptoConstants.ConfirmText);
        }
    }

    /// <summary>種別と平文を暗号化し、新しい連番のフレームを返す。</summary>
    public byte[] Encrypt(FrameType type, ReadOnlySpan<byte> payload)
    {
        if (type == FrameType.Confirm) throw new E2eCryptoException("confirm-type-reserved");
        if (payload.Length > CryptoConstants.MaxPayload) throw new E2eCryptoException("payload-too-large");
        lock (_lock)
        {
            if (_seq == long.MaxValue) throw new E2eCryptoException("sequence-exhausted");
            return Seal(++_seq, type, payload);
        }
    }

    private byte[] Seal(long seq, FrameType type, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[CryptoConstants.SeqLength + 1 + payload.Length + CryptoConstants.TagLength];
        BinaryPrimitives.WriteUInt64BigEndian(frame, (ulong)seq);

        var plain = new byte[1 + payload.Length];
        plain[0] = (byte)type;
        payload.CopyTo(plain.AsSpan(1));

        Span<byte> nonce = stackalloc byte[CryptoConstants.NonceLength];
        FrameFormat.FillNonce(nonce, _sid, frame);
        var aad = FrameFormat.BuildAad(_sid, frame);

        _aes.Encrypt(nonce, plain, frame.AsSpan(CryptoConstants.SeqLength, plain.Length),
                     frame.AsSpan(CryptoConstants.SeqLength + plain.Length, CryptoConstants.TagLength), aad);
        return frame;
    }

    public void Dispose() => _aes.Dispose();
}
