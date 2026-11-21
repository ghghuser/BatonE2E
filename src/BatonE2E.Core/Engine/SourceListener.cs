using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Logging;

namespace BatonE2E.Core.Engine;

/// <summary>
/// Icecast式のsource受け口（生のTCP。127.0.0.1のみ）。PUT / SOURCE / 旧形式 ICE/1.0 に対応。
/// 後から繋いだsourceに切り替える（takeover）。
/// </summary>
internal sealed partial class SourceListener : IAsyncDisposable
{
    private const int FlushMs = 300;
    private readonly EngineOptions _o;
    private readonly StreamHub _hub;
    private readonly EngineStats _st;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private TcpListener? _listener;
    private Task? _loop;
    private CancellationTokenSource? _active;

    private readonly X509Certificate2? _cert;

    public SourceListener(EngineOptions o, StreamHub hub, EngineStats st, X509Certificate2? cert)
    { _o = o; _hub = hub; _st = st; _cert = cert; }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _o.SourcePort);
        _listener.Start();
        AppLog.Info(Loc.Get("log.source.listen", _o.SourcePort));
        _loop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var c = await _listener!.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleAsync(c));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            using var raw = client.GetStream();
            var stream = await NegotiateAsync(client, raw);
            if (stream == null) return;
            try { await ServeAsync(stream); }
            finally { if (!ReferenceEquals(stream, raw)) await stream.DisposeAsync(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            AppLog.Debug("source: " + ex.Message);
        }
    }

    /// <summary>
    /// 接続の最初の1バイトで平文/TLSを判定（auto）。TLSなら最初から暗号化する方式（TLS 1.2/1.3）で待ち受ける。
    /// 戻り値が null なら切断または失敗（理由はログに出力済み）。
    /// </summary>
    private async Task<Stream?> NegotiateAsync(TcpClient client, NetworkStream raw)
    {
        using var hc = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        hc.CancelAfter(TimeSpan.FromSeconds(15));
        bool tls;
        if (_o.SourceTls == SourceTlsMode.On) tls = true;
        else
        {
            var b = new byte[1];
            var n = await client.Client.ReceiveAsync(b, SocketFlags.Peek, hc.Token);
            if (n == 0) return null;
            tls = b[0] == 0x16;
            if (tls && _o.SourceTls == SourceTlsMode.Off)
            {
                AppLog.Warn(Loc.Get("log.tls.off.hint"));
                return null;
            }
        }
        if (!tls) return raw;
        if (_cert == null) return null;
        var ssl = new SslStream(raw, false);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _cert,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, hc.Token);
            AppLog.Info(Loc.Get("log.tls.connect", ssl.SslProtocol));
            return ssl;
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException)
        {
            AppLog.Warn(Loc.Get("log.tls.handshake.fail", Sanitize(ex.InnerException?.Message ?? ex.Message)));
            await ssl.DisposeAsync();
            return null;
        }
    }

    private async Task ServeAsync(Stream stream)
    {
        CancellationTokenSource? mine = null;
        try
        {
            var src = new BufferedSource(stream);

            SourceRequest? req;
            using (var hc = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                hc.CancelAfter(TimeSpan.FromSeconds(15));
                req = await SourceRequest.ReadAsync(src, hc.Token);
            }
            if (req == null) return;
            var ct = _cts.Token;

            if (req.Invalid || (req.Method != "PUT" && req.Method != "SOURCE" && req.Method != "GET"))
            {
                // パスワードを含みうる行全体は出さず、方式と末尾の語だけを記録する
                AppLog.Warn(Loc.Get("log.source.badrequest", Sanitize(req.Method), Sanitize(req.LastToken)));
                await WriteAsync(stream, "HTTP/1.0 400 Bad Request\r\n\r\n", ct);
                return;
            }
            if (req.Method == "GET") { await HandleAdminAsync(req, stream, ct); return; }

            var user = "";
            var ar = req.IceStyle
                ? (SourceRequest.SecretEquals(req.IcePassword ?? "", _o.SourcePassword) ? AuthResult.Ok : AuthResult.BadPassword)
                : req.CheckBasic(_o.SourcePassword, _o.AcceptAnySourceUser, out user);
            if (ar != AuthResult.Ok)
            {
                LogAuthFail(ar, user, req);
                await WriteAsync(stream, "HTTP/1.0 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"BatonE2E\"\r\n\r\n", ct);
                return;
            }

            if (!string.IsNullOrEmpty(_o.Mount) && !string.Equals(req.Target, _o.Mount, StringComparison.Ordinal))
                AppLog.Warn(Loc.Get("log.source.mount", Sanitize(req.Target), _o.Mount));

            mine = BeginSession(ct);
            var sct = mine.Token;

            if (req.Header("Expect")?.Contains("100-continue", StringComparison.OrdinalIgnoreCase) == true)
                await WriteAsync(stream, "HTTP/1.1 100 Continue\r\n\r\n", sct);
            await WriteAsync(stream, req.Proto.StartsWith("HTTP/1.1", StringComparison.Ordinal)
                ? "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n" : "HTTP/1.0 200 OK\r\n\r\n", sct);

            var mime = NormalizeMime(req.Header("Content-Type"));
            var agent = Sanitize(req.Header("User-Agent") ?? req.Header("ice-name") ?? "-");
            var via = stream is SslStream ssl ? $" [TLS {ssl.SslProtocol}]" : "";
            AppLog.Info(Loc.Get("log.source.connect", (req.IceStyle ? "ICE/1.0" : req.Method + " " + req.Proto) + via, mime, agent));
            if (mime != "audio/mpeg") AppLog.Info(Loc.Get("log.source.nonmp3", mime));
            _st.SourceActive = true; _st.SourceMime = mime;
            _hub.SetMime(mime);

            IBodyReader body = req.Header("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true
                ? new ChunkedBody(src) : new RawBody(src);
            var total = await PumpAsync(body, sct);
            AppLog.Info(Loc.Get("log.source.disconnect", total));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or ObjectDisposedException)
        {
            AppLog.Debug("source: " + ex.Message);
        }
        catch (Exception ex)
        {
            AppLog.Error(Loc.Get("log.error", ex.Message));
        }
        finally { EndSession(mine); }
    }

    /// <summary>認証失敗の理由別ログ。受け取ったユーザー名は出すが、パスワードは出さない。</summary>
    private static void LogAuthFail(AuthResult r, string user, SourceRequest req)
    {
        var key = r switch
        {
            AuthResult.BadUser => "log.source.auth.user",
            AuthResult.BadPassword => "log.source.auth.pass",
            _ => "log.source.auth.none",
        };
        AppLog.Warn(Loc.Get(key, Sanitize(user), Sanitize(req.HeaderNames())));
    }

    private async Task<long> PumpAsync(IBodyReader body, CancellationToken ct)
    {
        long total = 0;
        var buf = new byte[_o.ChunkSize];
        var fill = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var n = await body.ReadAsync(buf.AsMemory(fill), ct);
                if (n == 0) break;
                total += n; _st.AddBytesIn(n); fill += n;
                if (fill == buf.Length || sw.ElapsedMilliseconds >= FlushMs)
                {
                    _hub.Publish(FrameType.Audio, buf.AsSpan(0, fill));
                    fill = 0; sw.Restart();
                }
            }
        }
        finally
        {
            if (fill > 0 && !ct.IsCancellationRequested) _hub.Publish(FrameType.Audio, buf.AsSpan(0, fill));
        }
        return total;
    }

    /// <summary>曲名更新（GET /admin/metadata?mode=updinfo&amp;song=...）。Basic認証を要求。</summary>
    private async Task HandleAdminAsync(SourceRequest req, Stream stream, CancellationToken ct)
    {
        if (!req.Target.StartsWith("/admin/metadata", StringComparison.Ordinal))
        {
            await WriteAsync(stream, "HTTP/1.0 404 Not Found\r\n\r\n", ct);
            return;
        }
        var ar = req.CheckBasic(_o.SourcePassword, _o.AcceptAnySourceUser, out var au);
        if (ar != AuthResult.Ok)
        {
            LogAuthFail(ar, au, req);
            await WriteAsync(stream, "HTTP/1.0 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"BatonE2E\"\r\n\r\n", ct);
            return;
        }
        var q = req.Target.IndexOf('?');
        if (q >= 0)
        {
            foreach (var kv in req.Target[(q + 1)..].Split('&'))
            {
                var e = kv.IndexOf('=');
                if (e <= 0 || kv[..e] != "song") continue;
                var song = Uri.UnescapeDataString(kv[(e + 1)..].Replace('+', ' '));
                AppLog.Info(Loc.Get("log.meta"));
                _hub.PublishMetadata(song);
            }
        }
        await WriteAsync(stream, "HTTP/1.0 200 OK\r\nContent-Type: text/xml\r\n\r\n" +
            "<?xml version=\"1.0\"?><iceresponse><message>Metadata update successful</message><return>1</return></iceresponse>", ct);
    }

    private CancellationTokenSource BeginSession(CancellationToken outer)
    {
        var s = CancellationTokenSource.CreateLinkedTokenSource(outer);
        lock (_lock)
        {
            if (_active != null)
            {
                AppLog.Info(Loc.Get("log.source.takeover"));
                try { _active.Cancel(); } catch (ObjectDisposedException) { }
            }
            _active = s;
        }
        return s;
    }

    private void EndSession(CancellationTokenSource? s)
    {
        if (s == null) return;
        lock (_lock)
        {
            if (ReferenceEquals(_active, s)) { _active = null; _st.SourceActive = false; }
        }
        s.Dispose();
    }

    private static string NormalizeMime(string? raw)
    {
        var m = (raw ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (m.Length == 0 || m == "audio/mp3") return "audio/mpeg";
        if (MimeRegex().IsMatch(m)) return m;
        AppLog.Warn(Loc.Get("log.source.mime", Sanitize(m)));
        return "audio/mpeg";
    }

    /// <summary>ログ出力用に、制御文字を除き長さを制限する。</summary>
    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (sb.Length >= 64) break;
            sb.Append(char.IsControl(ch) ? '?' : ch);
        }
        return sb.ToString();
    }

    private static Task WriteAsync(Stream s, string text, CancellationToken ct)
        => s.WriteAsync(Encoding.Latin1.GetBytes(text), ct).AsTask();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.+-]*/[a-z0-9][a-z0-9.+-]*$")]
    private static partial Regex MimeRegex();

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener?.Stop();
        if (_loop != null) { try { await _loop; } catch (Exception) { /* 停止時の例外は無視 */ } }
        _cts.Dispose();
    }
}
