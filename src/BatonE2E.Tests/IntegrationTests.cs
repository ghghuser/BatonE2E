using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;

namespace BatonE2E.Tests;

/// <summary>エンジンをプロセス内で起動し、PUT / SOURCE / ICE/1.0 で流し込んで、WebSocketで受信・復号して照合する。</summary>
internal static class IntegrationTests
{
    private const string ViewPw = "view-passphrase-0123456789";
    private const string SrcPw = "src-secret";

    internal sealed class Viewer
    {
        public readonly List<(FrameType type, byte[] data)> Frames = new();
        public string? Failure;
        public bool PasswordOk;
        public string HeaderJson = "";
        public readonly ClientWebSocket Ws = new();
        public readonly object Gate = new();

        public async Task RunAsync(int port, string password)
        {
            await Ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
            var buf = new byte[65536];
            FrameDecryptor? dec = null;
            var first = true;
            while (Ws.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await Ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close) break;
                var bytes = ms.ToArray();
                if (r.MessageType == WebSocketMessageType.Text)
                {
                    HeaderJson = Encoding.UTF8.GetString(bytes);
                    dec = FrameDecryptor.Create(password, StreamHeader.Parse(HeaderJson)!);
                }
                else if (first) { PasswordOk = dec!.VerifyConfirmation(bytes); first = false; }
                else
                {
                    var st = dec!.TryDecrypt(bytes, out var t, out var p);
                    lock (Gate) { if (st == DecryptStatus.Ok) Frames.Add((t, p)); else Failure = st.ToString(); }
                }
            }
        }

        public byte[] Audio() { lock (Gate) return Frames.Where(f => f.type == FrameType.Audio).SelectMany(f => f.data).ToArray(); }
        public bool Has(FrameType t, string text) { lock (Gate) return Frames.Any(f => f.type == t && Encoding.UTF8.GetString(f.data).Contains(text)); }
    }

    public static async Task RunAsync()
    {
        AppLog.ToConsole = false;
        var o = new EngineOptions
        {
            SourcePort = FreePort(), ViewPort = FreePort(), SourcePassword = SrcPw, ViewPassword = ViewPw,
            Iterations = 100_000, ChunkSize = 1024, MaxViewers = 2, Mount = "/m",
            CertDir = Path.Combine(Path.GetTempPath(), "baton-it-" + Guid.NewGuid().ToString("N")),
        };
        await using var eng = new RelayEngine(o);
        await eng.StartAsync();

        var v = new Viewer();
        var vtask = Task.Run(() => v.RunAsync(o.ViewPort, ViewPw));
        await WaitAsync(() => v.PasswordOk);
        T.Check("結合: 確認ブロックが検証できる", v.PasswordOk);
        T.Check("結合: ヘッダーのmimeはaudio/mpeg", v.HeaderJson.Contains("audio/mpeg"));

        var rnd = new Random(7);
        byte[] Data(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("source:" + SrcPw));

        // (a) PUT + chunked
        var d1 = Data(20_000);
        var r1 = await SendAsync(o.SourcePort, $"PUT /m HTTP/1.1\r\nAuthorization: Basic {basic}\r\nContent-Type: audio/mpeg\r\nTransfer-Encoding: chunked\r\n\r\n", Chunked(d1));
        T.Check("結合: PUT/chunked 応答200", r1.StartsWith("HTTP/1.1 200"));
        await WaitAsync(() => v.Audio().Length >= d1.Length);
        T.Check("結合: PUT/chunked 受信・復号した音声が元と一致", v.Audio().SequenceEqual(d1));

        // (b) SOURCE HTTP/1.0（形式変更もあり）
        var d2 = Data(15_000);
        var r2 = await SendAsync(o.SourcePort, $"SOURCE /m HTTP/1.0\r\nAuthorization: Basic {basic}\r\nContent-Type: audio/aac\r\n\r\n", d2);
        T.Check("結合: SOURCE 応答200", r2.StartsWith("HTTP/1.0 200"));
        await WaitAsync(() => v.Audio().Length >= d1.Length + d2.Length);
        T.Check("結合: SOURCE 音声が一致", v.Audio().SequenceEqual(d1.Concat(d2)));
        T.Check("結合: 形式変更(audio/aac)が通知される", v.Has(FrameType.FormatChange, "audio/aac"));

        // (c) 旧形式 ICE/1.0
        var d3 = Data(9_000);
        var r3 = await SendAsync(o.SourcePort, $"SOURCE {SrcPw} /m ICE/1.0\r\ncontent-type: audio/mpeg\r\nice-name: test\r\n\r\n", d3);
        T.Check("結合: ICE/1.0 応答200", r3.StartsWith("HTTP/1.0 200"));
        await WaitAsync(() => v.Audio().Length >= d1.Length + d2.Length + d3.Length);
        T.Check("結合: ICE/1.0 音声が一致", v.Audio().SequenceEqual(d1.Concat(d2).Concat(d3)));
        T.Check("結合: 形式がmp3へ戻る通知", v.Has(FrameType.FormatChange, "audio/mpeg"));

        // 曲名更新
        var m = await SendAsync(o.SourcePort, $"GET /admin/metadata?mode=updinfo&mount=/m&song=%E6%9B%B2%E5%90%8D+A HTTP/1.0\r\nAuthorization: Basic {basic}\r\n\r\n", Array.Empty<byte>());
        T.Check("結合: メタデータ更新 応答200", m.StartsWith("HTTP/1.0 200"));
        await WaitAsync(() => v.Has(FrameType.Metadata, "曲名 A"));
        T.Check("結合: 曲名が暗号化されて視聴者へ届く", v.Has(FrameType.Metadata, "曲名 A"));

        // 認証失敗・不明形式
        var bad = Convert.ToBase64String(Encoding.UTF8.GetBytes("source:wrong"));
        var a1 = await SendAsync(o.SourcePort, $"PUT /m HTTP/1.1\r\nAuthorization: Basic {bad}\r\n\r\n", Array.Empty<byte>());
        T.Check("結合: 誤パスワードは401", a1.StartsWith("HTTP/1.0 401"));
        var a2 = await SendAsync(o.SourcePort, "SOURCE wrong /m ICE/1.0\r\n\r\n", Array.Empty<byte>());
        T.Check("結合: ICE/1.0の誤パスワードは401", a2.StartsWith("HTTP/1.0 401"));
        var a3 = await SendAsync(o.SourcePort, "HELLO WORLD\r\n\r\n", Array.Empty<byte>());
        T.Check("結合: 不明形式は400", a3.StartsWith("HTTP/1.0 400"));

        // 暗号化の自己検査・全体の健全性
        T.Check("結合: 全フレームで復号・連番検査が成功（失敗なし）", v.Failure == null);
        T.Check("結合: 自己検査が実行され、失敗0", eng.Stats.CheckOk > 0 && eng.Stats.CheckNg == 0);

        // 視聴者数の上限（MaxViewers=2: 1人目=v、2人目は可、3人目は拒否）
        var v2 = new Viewer();
        _ = Task.Run(() => v2.RunAsync(o.ViewPort, ViewPw));
        await WaitAsync(() => v2.PasswordOk);
        T.Check("結合: 2人目の視聴者は接続できる", v2.PasswordOk);
        var rejected = false;
        try { using var v3 = new ClientWebSocket(); await v3.ConnectAsync(new Uri($"ws://127.0.0.1:{o.ViewPort}/ws"), CancellationToken.None); }
        catch (WebSocketException) { rejected = true; }
        T.Check("結合: 上限を超える接続は拒否", rejected);

        // 視聴ページ
        using var http = new HttpClient();
        var page = await http.GetStringAsync($"http://127.0.0.1:{o.ViewPort}/");
        T.Check("結合: 視聴ページが配信される", page.Contains("<title>ライブ配信</title>") && !page.Contains("BatonE2E") && page.Contains("E2ERADIO1"));

        try { Directory.Delete(o.CertDir!, true); } catch (IOException) { }
        try { await v.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch (WebSocketException) { }
        _ = vtask;
    }

    internal static byte[] Chunked(byte[] d)
    {
        var ms = new MemoryStream();
        var sizes = new[] { 1, 700, 4096, 333, 8000 };
        var pos = 0; var i = 0;
        while (pos < d.Length)
        {
            var n = Math.Min(sizes[i++ % sizes.Length], d.Length - pos);
            ms.Write(Encoding.ASCII.GetBytes($"{n:x}\r\n")); ms.Write(d, pos, n); ms.Write("\r\n"u8);
            pos += n;
        }
        ms.Write("0\r\n\r\n"u8);
        return ms.ToArray();
    }

    /// <summary>ヘッダーを送り、応答の先頭を読み、本文を送って接続を閉じる。応答の文字列を返す。</summary>
    internal static async Task<string> SendAsync(int port, string head, byte[] body)
    {
        using var c = new TcpClient();
        await c.ConnectAsync("127.0.0.1", port);
        var s = c.GetStream();
        await s.WriteAsync(Encoding.Latin1.GetBytes(head));
        var buf = new byte[1024];
        using var cts = new CancellationTokenSource(5000);
        var n = await s.ReadAsync(buf, cts.Token);
        var resp = Encoding.Latin1.GetString(buf, 0, n);
        if (body.Length > 0) { await s.WriteAsync(body); await s.FlushAsync(); }
        await Task.Delay(100);
        return resp;
    }

    internal static async Task WaitAsync(Func<bool> cond, int ms = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < end) await Task.Delay(50);
    }

    internal static int FreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
