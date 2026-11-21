namespace BatonE2E.Core.Crypto;

/// <summary>nonce・AADの組み立て（暗号側・復号側で共通）。</summary>
internal static class FrameFormat
{
    /// <summary>nonce(12) = sid(4) + 連番(8, BE)。frame先頭8バイトが連番。</summary>
    internal static void FillNonce(Span<byte> nonce, byte[] sid, ReadOnlySpan<byte> frame)
    {
        sid.CopyTo(nonce);
        frame[..CryptoConstants.SeqLength].CopyTo(nonce[CryptoConstants.SessionIdLength..]);
    }

    /// <summary>AAD = "E2ERv1" + sid + 連番(8, BE)。</summary>
    internal static byte[] BuildAad(byte[] sid, ReadOnlySpan<byte> frame)
    {
        var aad = new byte[CryptoConstants.AadPrefix.Length + sid.Length + CryptoConstants.SeqLength];
        CryptoConstants.AadPrefix.CopyTo(aad, 0);
        sid.CopyTo(aad, CryptoConstants.AadPrefix.Length);
        frame[..CryptoConstants.SeqLength].CopyTo(aad.AsSpan(CryptoConstants.AadPrefix.Length + sid.Length));
        return aad;
    }
}
