using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BatonE2E.Core.Security;

public enum CertEvent { Loaded, Created, RegeneratedByRequest, RegeneratedExpired }

/// <summary>
/// source受け口用の自己署名証明書。oddcast（SChannel）との互換を優先し、RSA 2048 + SHA-256。
/// SANは IP:127.0.0.1 と DNS:localhost。初回に1度だけ作成して保存し、毎回は作り直さない
/// （指紋が変わると、配信ソフト側が拒否するため）。有効期間は10年。
/// </summary>
public static class SourceCertificate
{
    public const string PfxName = "source.pfx";
    public const string CerName = "source.cer";

    public static X509Certificate2 LoadOrCreate(string dir, bool regenerate, out CertEvent ev)
    {
        Directory.CreateDirectory(dir);
        var pfx = Path.Combine(dir, PfxName);
        var existed = File.Exists(pfx);
        var expired = false;
        if (existed && !regenerate)
        {
            try
            {
                var c = new X509Certificate2(pfx, (string?)null);
                if (c.HasPrivateKey && c.NotAfter > DateTime.Now.AddDays(1))
                {
                    ev = CertEvent.Loaded;
                    EnsureCer(dir, c);
                    return c;
                }
                c.Dispose();
                expired = true;
            }
            catch (CryptographicException) { expired = true; }   // 壊れている場合も作り直し
        }
        ev = !existed ? CertEvent.Created
           : regenerate ? CertEvent.RegeneratedByRequest
           : CertEvent.RegeneratedExpired;
        return Create(dir);
    }

    private static X509Certificate2 Create(string dir)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        using var self = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfxBytes = self.Export(X509ContentType.Pfx);   // 秘密鍵つき（パスワードなし）。保管先は利用者のPC内
        WriteAtomic(Path.Combine(dir, PfxName), pfxBytes);
        WriteAtomic(Path.Combine(dir, CerName), self.Export(X509ContentType.Cert));   // 公開部のみ。Windowsへの信頼登録用
        return new X509Certificate2(pfxBytes, (string?)null);
    }

    private static void EnsureCer(string dir, X509Certificate2 c)
    {
        var cer = Path.Combine(dir, CerName);
        if (!File.Exists(cer)) WriteAtomic(cer, c.Export(X509ContentType.Cert));
    }

    /// <summary>一時ファイルへ書いてから置換（書込み途中の破損を防ぐ）。</summary>
    private static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>SHA-256指紋（16進・大文字・区切りなし）。</summary>
    public static string Fingerprint(X509Certificate2 c) => Convert.ToHexString(c.GetCertHash(HashAlgorithmName.SHA256));

    /// <summary>コロン区切り（AA:BB:...）。oddcastの確認画面と見比べる用。</summary>
    public static string FingerprintColon(X509Certificate2 c)
        => string.Join(':', Fingerprint(c).Chunk(2).Select(p => new string(p)));
}
