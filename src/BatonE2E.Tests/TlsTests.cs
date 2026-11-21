using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;

namespace BatonE2E.Tests;

/// <summary>
/// oddcast（SChannel）の挙動を模擬: TLS 1.2限定、最初から暗号化、ホスト名（SAN）を検証、
/// 自己署名はSHA-256指紋を固定（ピン留め）して接続する。
/// </summary>
internal static class TlsTests
{
    private const string ViewPw = "tls-view-passphrase-123456";
    private const string SrcPw = "tls-src-secret";

    private static EngineOptions Opts(string dir) => new()
    {
        SourcePort = IntegrationTests.FreePort(), ViewPort = IntegrationTests.FreePort(),
        SourcePassword = SrcPw, ViewPassword = ViewPw, Iterations = 100_000, ChunkSize = 1024,
        Mount = "/m", CertDir = dir, PageTitle = "ラジオ",
    };

    private static string Basic(string user) => Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + SrcPw));

    public static async Task RunAsync()
    {
        AppLog.ToConsole = false;
        var dir = Path.Combine(Path.GetTempPath(), "baton-tls-" + Guid.NewGuid().ToString("N"));
        try
        {
            string fp;
            var o = Opts(dir);
            await using (var eng = new RelayEngine(o))
            {
                await eng.StartAsync();
                fp = eng.SourceCertFingerprintColon!;
                T.Check("TLS: 起動時にSHA-256指紋が得られる（32バイト）", fp.Split(':').Length == 32);
                T.Check("TLS: 証明書(.pfx)と公開部(.cer)が保存される",
                    File.Exists(Path.Combine(dir, "source.pfx")) && File.Exists(Path.Combine(dir, "source.cer")));

                var v = new IntegrationTests.Viewer();
                _ = Task.Run(() => v.RunAsync(o.ViewPort, ViewPw));
                await IntegrationTests.WaitAsync(() => v.PasswordOk);

                var rnd = new Random(11);
                byte[] Data(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }

                // TLS 1.2限定・IPでSAN検証・指紋固定（chunked PUT）
                var d1 = Data(12_000);
                var (r1, p1) = await SendTlsAsync(o.SourcePort, "127.0.0.1", fp, SslProtocols.Tls12,
                    $"PUT /m HTTP/1.1\r\nAuthorization: Basic {Basic("source")}\r\nContent-Type: audio/mpeg\r\nTransfer-Encoding: chunked\r\n\r\n",
                    IntegrationTests.Chunked(d1));
                T.Check("TLS: TLS 1.2・SAN(IP 127.0.0.1)検証・指紋固定でPUT 200", r1.StartsWith("HTTP/1.1 200") && p1 == SslProtocols.Tls12);
                await IntegrationTests.WaitAsync(() => v.Audio().Length >= d1.Length);
                T.Check("TLS: 暗号化された経路の音声が元と一致", v.Audio().SequenceEqual(d1));

                // ホスト名 localhost でSAN(DNS)を検証、SOURCE HTTP/1.0
                var d2 = Data(7_000);
                var (r2, _) = await SendTlsAsync(o.SourcePort, "localhost", fp, SslProtocols.Tls12,
                    $"SOURCE /m HTTP/1.0\r\nAuthorization: Basic {Basic("source")}\r\nContent-Type: audio/mpeg\r\n\r\n", d2);
                T.Check("TLS: SAN(DNS localhost)検証でSOURCE 200", r2.StartsWith("HTTP/1.0 200"));
                await IntegrationTests.WaitAsync(() => v.Audio().Length >= d1.Length + d2.Length);
                T.Check("TLS: 2本目の音声も一致", v.Audio().SequenceEqual(d1.Concat(d2)));

                // 指紋が違う（保存済みと違う証明書）は拒否
                var wrong = string.Join(':', Enumerable.Repeat("00", 32));
                var rejected = false;
                try { await SendTlsAsync(o.SourcePort, "127.0.0.1", wrong, SslProtocols.Tls12, "PUT /m HTTP/1.1\r\n\r\n", Array.Empty<byte>()); }
                catch (AuthenticationException) { rejected = true; }
                T.Check("TLS: 指紋の不一致は拒否される", rejected);

                // autoなので平文も受け付ける
                var d3 = Data(5_000);
                var r3 = await IntegrationTests.SendAsync(o.SourcePort,
                    $"PUT /m HTTP/1.1\r\nAuthorization: Basic {Basic("source")}\r\nContent-Type: audio/mpeg\r\n\r\n", d3);
                T.Check("TLS(auto): 平文のPUTも受け付ける", r3.StartsWith("HTTP/1.1 200"));
                await IntegrationTests.WaitAsync(() => v.Audio().Length >= d1.Length + d2.Length + d3.Length);
                T.Check("TLS(auto): 平文の音声も一致", v.Audio().SequenceEqual(d1.Concat(d2).Concat(d3)));

                // ユーザー名違いは401（既定）
                var r4 = await IntegrationTests.SendAsync(o.SourcePort, $"PUT /m HTTP/1.1\r\nAuthorization: Basic {Basic("admin")}\r\n\r\n", Array.Empty<byte>());
                T.Check("認証: ユーザー名違いは401（既定）", r4.StartsWith("HTTP/1.0 401"));

                // 視聴ページの題名を変更できる（アプリ名は出さない）
                using var http = new HttpClient();
                var page = await http.GetStringAsync($"http://127.0.0.1:{o.ViewPort}/");
                T.Check("設定: 視聴ページの題名を変更でき、アプリ名が出ない", page.Contains("<title>ラジオ</title>") && !page.Contains("BatonE2E"));
                T.Check("TLS: 自己検査の失敗0・視聴側の復号失敗なし", eng.Stats.CheckNg == 0 && v.Failure == null);
            }

            // 再起動しても同じ証明書（指紋が変わらない）
            var o2 = Opts(dir);
            await using (var eng2 = new RelayEngine(o2))
            {
                await eng2.StartAsync();
                T.Check("TLS: 再起動しても同じ証明書を使う（指紋が同じ）", eng2.SourceCertFingerprintColon == fp && !eng2.SourceCertRegenerated);
            }

            // --regen-cert でのみ作り直し（警告対象）。ユーザー名を問わない設定の確認も兼ねる
            var o3 = Opts(dir); o3.RegenCert = true; o3.AcceptAnySourceUser = true;
            await using (var eng3 = new RelayEngine(o3))
            {
                await eng3.StartAsync();
                T.Check("TLS: --regen-certで指紋が変わり、再生成フラグが立つ", eng3.SourceCertFingerprintColon != fp && eng3.SourceCertRegenerated);
                var r5 = await IntegrationTests.SendAsync(o3.SourcePort, $"PUT /m HTTP/1.1\r\nAuthorization: Basic {Basic("admin")}\r\n\r\n", Array.Empty<byte>());
                T.Check("認証: ユーザー名を問わない設定ではパスワードのみで200", r5.StartsWith("HTTP/1.1 200"));
                var bad = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong"));
                var r6 = await IntegrationTests.SendAsync(o3.SourcePort, $"PUT /m HTTP/1.1\r\nAuthorization: Basic {bad}\r\n\r\n", Array.Empty<byte>());
                T.Check("認証: ユーザー名を問わなくてもパスワード違いは401", r6.StartsWith("HTTP/1.0 401"));
            }

            // TLS必須（on）では平文を受け付けず、offではTLSを受け付けない
            var o4 = Opts(dir); o4.SourceTls = SourceTlsMode.On;
            await using (var eng4 = new RelayEngine(o4))
            {
                await eng4.StartAsync();
                var (r7, _) = await SendTlsAsync(o4.SourcePort, "127.0.0.1", eng4.SourceCertFingerprintColon!, SslProtocols.Tls12,
                    $"PUT /m HTTP/1.1\r\nAuthorization: Basic {Basic("source")}\r\n\r\n", Array.Empty<byte>());
                T.Check("TLS(on): TLS接続は受け付ける", r7.StartsWith("HTTP/1.1 200"));
            }
            var o5 = Opts(dir); o5.SourceTls = SourceTlsMode.Off;
            await using (var eng5 = new RelayEngine(o5))
            {
                await eng5.StartAsync();
                T.Check("TLS(off): 証明書を使わない", eng5.SourceCertFingerprintColon == null);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static bool Pin(object? cert, SslPolicyErrors e, string fp)
    {
        // 自己署名なのでチェーンエラーは許容。ホスト名（SAN）の不一致と証明書なしは不可。指紋は固定。
        if (cert is not System.Security.Cryptography.X509Certificates.X509Certificate c) return false;
        if ((e & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
        return string.Join(':', c.GetCertHash(HashAlgorithmName.SHA256).Select(b => b.ToString("X2"))) == fp;
    }

    private static async Task<(string resp, SslProtocols proto)> SendTlsAsync(
        int port, string host, string fp, SslProtocols protocols, string head, byte[] body)
    {
        using var c = new TcpClient();
        await c.ConnectAsync("127.0.0.1", port);
        using var ssl = new SslStream(c.GetStream(), false, (_, cert, _, errors) => Pin(cert, errors, fp));
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = protocols,
            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
        });
        await ssl.WriteAsync(Encoding.Latin1.GetBytes(head));
        var buf = new byte[1024];
        using var cts = new CancellationTokenSource(5000);
        var n = await ssl.ReadAsync(buf, cts.Token);
        var resp = Encoding.Latin1.GetString(buf, 0, n);
        if (body.Length > 0) { await ssl.WriteAsync(body); await ssl.FlushAsync(); }
        await Task.Delay(100);
        return (resp, ssl.SslProtocol);
    }
}
