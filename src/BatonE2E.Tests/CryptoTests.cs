using System.Text;
using BatonE2E.Core.Crypto;

namespace BatonE2E.Tests;

internal static class CryptoTests
{
    private const int Iter = 100_000;   // テストの高速化（既定600000は相互運用テストで使う）

    public static void Run()
    {
        const string pw = "correct horse battery staple";
        using var enc = FrameEncryptor.Create(pw, Iter);
        var header = StreamHeader.Parse(enc.Header.ToJson())!;
        using var dec = FrameDecryptor.Create(pw, header);

        T.Check("ヘッダー: JSON往復", header.V == 1 && header.Kdf == "PBKDF2-SHA256" && header.Iter == Iter && header.Mime == "audio/mpeg");
        T.Check("確認ブロック: 正しいパスワードで検証成功", dec.VerifyConfirmation(enc.ConfirmationFrame()));
        T.Check("確認ブロック: 同じ暗号文を再送できる", enc.ConfirmationFrame().SequenceEqual(enc.ConfirmationFrame()));

        using var bad = FrameDecryptor.Create("wrong password", header);
        T.Check("確認ブロック: 誤パスワードは失敗", !bad.VerifyConfirmation(enc.ConfirmationFrame()));

        var a1 = Encoding.UTF8.GetBytes("audio-1");
        var f1 = enc.Encrypt(FrameType.Audio, a1);
        var f2 = enc.Encrypt(FrameType.Metadata, "{\"title\":\"曲名\"}"u8);
        var f3 = enc.Encrypt(FrameType.FormatChange, "audio/aac"u8);
        T.Check("暗号化: 長さ = 8+1+平文+16", f1.Length == 8 + 1 + a1.Length + 16);

        T.Check("復号: 音声", dec.TryDecrypt(f1, out var t1, out var p1) == DecryptStatus.Ok && t1 == FrameType.Audio && p1.SequenceEqual(a1));
        T.Check("復号: メタデータ", dec.TryDecrypt(f2, out var t2, out var p2) == DecryptStatus.Ok && t2 == FrameType.Metadata
                && Encoding.UTF8.GetString(p2) == "{\"title\":\"曲名\"}");
        T.Check("復号: 形式変更", dec.TryDecrypt(f3, out var t3, out var p3) == DecryptStatus.Ok && t3 == FrameType.FormatChange
                && Encoding.UTF8.GetString(p3) == "audio/aac");

        // 連番は1から単調増加、nonceが重複しない
        var seqs = new[] { f1, f2, f3 }.Select(f => System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(f)).ToArray();
        T.Check("連番: 1,2,3", seqs.SequenceEqual(new ulong[] { 1, 2, 3 }));

        // 改ざん・連番異常
        using var d2 = FrameDecryptor.Create(pw, header);
        var tampered = (byte[])f1.Clone(); tampered[10] ^= 1;
        T.Check("改ざん: 暗号文のビット反転で失敗", d2.TryDecrypt(tampered, out _, out _) == DecryptStatus.AuthFailed);
        var seqSwap = (byte[])f1.Clone(); seqSwap[7] = 9;
        T.Check("改ざん: 連番の書き換えで失敗（nonce/AAD）", d2.TryDecrypt(seqSwap, out _, out _) == DecryptStatus.AuthFailed);
        var tagCut = f1[..^1];
        T.Check("改ざん: 短縮で失敗", d2.TryDecrypt(tagCut, out _, out _) == DecryptStatus.AuthFailed);
        T.Check("途中参加: 最初の連番が2でも受理", d2.TryDecrypt(f2, out _, out _) == DecryptStatus.Ok);
        T.Check("連番: 飛びを検出", d2.TryDecrypt(enc.Encrypt(FrameType.Audio, a1), out _, out _) == DecryptStatus.SequenceError);
        T.Check("連番: 再送を検出", d2.TryDecrypt(f2, out _, out _) == DecryptStatus.SequenceError);
        T.Check("連番0を音声として渡すと拒否", d2.TryDecrypt(enc.ConfirmationFrame(), out _, out _) == DecryptStatus.BadFormat);

        // 別セッション（塩とsidが違う）の暗号文は復号できない
        using var enc2 = FrameEncryptor.Create(pw, Iter);
        using var d3 = FrameDecryptor.Create(pw, header);
        T.Check("別セッション: 塩・sidが毎回違う", enc2.Header.Salt != enc.Header.Salt && enc2.Header.Sid != enc.Header.Sid);
        T.Check("別セッション: 復号できない", d3.TryDecrypt(enc2.Encrypt(FrameType.Audio, a1), out _, out _) == DecryptStatus.AuthFailed);

        // ヘッダー検査
        Throws("ヘッダー: 反復回数が下限未満は拒否", () => FrameDecryptor.Create(pw, new StreamHeader { Iter = 1000, Salt = header.Salt, Sid = header.Sid }));
        Throws("ヘッダー: 未対応のkdfは拒否", () => FrameDecryptor.Create(pw, new StreamHeader { Kdf = "x", Iter = Iter, Salt = header.Salt, Sid = header.Sid }));
        Throws("ヘッダー: 未対応のバージョンは拒否", () => FrameDecryptor.Create(pw, new StreamHeader { V = 2, Iter = Iter, Salt = header.Salt, Sid = header.Sid }));
        Throws("ヘッダー: sid長が不正は拒否", () => FrameDecryptor.Create(pw, new StreamHeader { Iter = Iter, Salt = header.Salt, Sid = "AAAA" }));
        Throws("暗号化: 空パスワードは拒否", () => FrameEncryptor.Create("", Iter));
        Throws("暗号化: 確認種別は外から使えない", () => enc.Encrypt(FrameType.Confirm, a1));
        T.Check("ヘッダー: 不正JSONは null", StreamHeader.Parse("{broken") is null);

        // 日本語パスワード: NFC正規化（合成済みと結合文字で同じ鍵）
        var s1 = KeyDerivation.Derive("が", new byte[16], Iter);       // U+304C
        var s2 = KeyDerivation.Derive("が", new byte[16], Iter); // U+304B + U+3099
        T.Check("鍵導出: NFC正規化で同一", s1.SequenceEqual(s2));
    }

    private static void Throws(string name, Action a)
    {
        try { a(); T.Check(name, false); }
        catch (E2eCryptoException) { T.Check(name, true); }
    }
}
