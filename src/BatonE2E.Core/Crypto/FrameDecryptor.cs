using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BatonE2E.Core.Crypto;

public enum DecryptStatus { Ok, BadFormat, AuthFailed, SequenceError }

/// <summary>
/// 視聴側（ヘルパーexe・テスト用）。ヘッダーに従って鍵導出し、確認ブロックの検証と、
/// 音声等フレームの復号・連番検査（最初に受けた値から+1ずつ）を行う。
/// </summary>
public sealed class FrameDecryptor : IDisposable
{
    private readonly AesGcm _aes;
    private readonly byte[] _sid;
    private long _lastSeq = -1;   // -1 = 未受信

    private FrameDecryptor(byte[] key, byte[] sid)
    {
        _aes = new AesGcm(key, CryptoConstants.TagLength);
        _sid = sid;
        CryptographicOperations.ZeroMemory(key);
    }

    public static FrameDecryptor Create(string password, StreamHeader header,
                                        int minIterations = CryptoConstants.MinIterations)
    {
        var (salt, sid) = header.Validate(minIterations);
        return new FrameDecryptor(KeyDerivation.Derive(password, salt, header.Iter), sid);
    }

    /// <summary>確認ブロック（連番0）の検証。false ならパスワード違い（または改ざん）。</summary>
    public bool VerifyConfirmation(ReadOnlySpan<byte> frame)
    {
        if (!TryOpen(frame, out var seq, out var plain) || seq != 0) return false;
        return plain.Length == 1 + CryptoConstants.ConfirmText.Length
               && plain[0] == (byte)FrameType.Confirm
               && plain.AsSpan(1).SequenceEqual(CryptoConstants.ConfirmText);
    }

    /// <summary>連番1以降のフレームを復号する。失敗時は理由を返し、それ以降の受信は止めること。</summary>
    public DecryptStatus TryDecrypt(ReadOnlySpan<byte> frame, out FrameType type, out byte[] payload)
    {
        type = FrameType.Audio; payload = Array.Empty<byte>();
        if (frame.Length < CryptoConstants.SeqLength + 1 + CryptoConstants.TagLength) return DecryptStatus.BadFormat;
        var seq = (long)BinaryPrimitives.ReadUInt64BigEndian(frame);
        if (seq < 1) return DecryptStatus.BadFormat;

        if (!TryOpen(frame, out _, out var plain)) return DecryptStatus.AuthFailed;
        // 連番は認証後に検査（改ざん・再送の区別のため、復号成功が前提）
        if (_lastSeq >= 0 && seq != _lastSeq + 1) return DecryptStatus.SequenceError;

        var t = plain[0];
        if (t < (byte)FrameType.Audio || t > (byte)FrameType.FormatChange) return DecryptStatus.BadFormat;
        _lastSeq = seq;
        type = (FrameType)t;
        payload = plain.AsSpan(1).ToArray();
        return DecryptStatus.Ok;
    }

    private bool TryOpen(ReadOnlySpan<byte> frame, out long seq, out byte[] plain)
    {
        seq = 0; plain = Array.Empty<byte>();
        var minLen = CryptoConstants.SeqLength + 1 + CryptoConstants.TagLength;
        if (frame.Length < minLen || frame.Length > minLen + CryptoConstants.MaxPayload) return false;
        seq = (long)BinaryPrimitives.ReadUInt64BigEndian(frame);

        var ctLen = frame.Length - CryptoConstants.SeqLength - CryptoConstants.TagLength;
        Span<byte> nonce = stackalloc byte[CryptoConstants.NonceLength];
        FrameFormat.FillNonce(nonce, _sid, frame);
        var aad = FrameFormat.BuildAad(_sid, frame);
        var buf = new byte[ctLen];
        try
        {
            _aes.Decrypt(nonce, frame.Slice(CryptoConstants.SeqLength, ctLen),
                         frame.Slice(CryptoConstants.SeqLength + ctLen, CryptoConstants.TagLength), buf, aad);
        }
        catch (CryptographicException) { return false; }
        plain = buf;
        return true;
    }

    public void Dispose() => _aes.Dispose();
}
