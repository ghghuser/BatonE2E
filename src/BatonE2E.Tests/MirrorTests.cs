using System.Net.WebSockets;
using System.Text;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;
using static BatonE2E.Tests.IntegrationTests;

namespace BatonE2E.Tests;

/// <summary>ミラー（暗号文のまま中継）。元のBatonE2E → ミラー → 視聴者 を実際に動かして確かめる。</summary>
internal static class MirrorTests
{
    private const string SrcPw = "src-secret";

    public static async Task RunAsync()
    {
        Urls();
        Hub();
        await EndToEndAsync();
    }

    private static void Urls()
    {
        string? W(string? s) => MirrorUrl.ToWebSocketUri(s)?.AbsoluteUri;
        T.Check("ミラーURL: httpsの視聴ページ → wss の …/ws", W("https://a.ts.net/live-abc") == "wss://a.ts.net/live-abc/ws");
        T.Check("ミラーURL: ルートと末尾スラッシュ", W("http://127.0.0.1:8080/") == "ws://127.0.0.1:8080/ws" && W("https://h.example/x/") == "wss://h.example/x/ws");
        T.Check("ミラーURL: ws/wssもそのまま使える", W("wss://h.example/p") == "wss://h.example/p/ws" && W("ws://10.0.0.5:9/") == "ws://10.0.0.5:9/ws");
        T.Check("ミラーURL: 不正な指定は拒否", new[] { null, "", "  ", "ftp://x/", "not a url", "https://user:pw@h/", "file:///c:/x", "//h/x" }.All(s => W(s) == null));
        var self = MirrorUrl.ToWebSocketUri("http://127.0.0.1:8080/live")!;
        T.Check("ミラーURL: 自分自身（同じ視聴ポート・パス）は検出する", MirrorUrl.IsSelf(self, 8080, "/live") && MirrorUrl.IsSelf(MirrorUrl.ToWebSocketUri("http://localhost:8080/")!, 8080, "/"));
        T.Check("ミラーURL: ポート・パスが違えば自分自身ではない", !MirrorUrl.IsSelf(self, 8081, "/live") && !MirrorUrl.IsSelf(self, 8080, "/other")
            && !MirrorUrl.IsSelf(MirrorUrl.ToWebSocketUri("https://a.ts.net:8080/live")!, 8080, "/live"));

        T.Check("ミラー設定: 配信ソフト用のパスワードなしでも検査を通る", Throws(() => new EngineOptions { UpstreamUrl = "https://a.ts.net/x", ViewPort = 9000 }.Validate()) == null);
        T.Check("ミラー設定: 不正なURLは拒否", Throws(() => new EngineOptions { UpstreamUrl = "ftp://x" }.Validate()) == "bad-upstream-url");
        T.Check("ミラー設定: 自分自身を指すURLは拒否", Throws(() => new EngineOptions { UpstreamUrl = "http://127.0.0.1:9000/", ViewPort = 9000 }.Validate()) == "upstream-is-self");
        T.Check("ミラー設定: 通常モードは従来どおりパスワードが必須", Throws(() => new EngineOptions().Validate()) == "empty-source-password");
    }

    private static string? Throws(Action a)
    {
        try { a(); return null; }
        catch (E2eCryptoException ex) { return ex.Message; }
    }

    private static void Hub()
    {
        var enc = FrameEncryptor.Create("hub-passphrase-12345", CryptoConstants.MinIterations);
        var hub = StreamHub.CreateMirror(2, new EngineStats());
        T.Check("ミラーハブ: 上流が未接続の間は視聴を受け付けない", !hub.Ready && hub.TryAddViewer(8, "a") == null);

        var header = enc.Header.ToJson();
        hub.SetUpstream(header, enc.ConfirmationFrame());
        T.Check("ミラーハブ: 上流のヘッダーを受け取ると受付開始", hub.Ready);
        var v = hub.TryAddViewer(8, "a")!;
        T.Check("ミラーハブ: ヘッダーと確認ブロックが元のまま視聴者へ渡る", v.HeaderJson == header && v.Confirm.SequenceEqual(enc.ConfirmationFrame()));

        var f1 = enc.Encrypt(FrameType.Audio, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var f2 = enc.Encrypt(FrameType.Audio, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        hub.PublishRaw(f1);
        hub.PublishRaw(f2);
        var got = new List<byte[]>();
        while (v.Reader.TryRead(out var x)) got.Add(x);
        T.Check("ミラーハブ: 暗号フレームが一切変わらず順に渡る", got.Count == 2 && got[0].SequenceEqual(f1) && got[1].SequenceEqual(f2));

        var f3 = enc.Encrypt(FrameType.Audio, new byte[8]);
        var f4 = enc.Encrypt(FrameType.Audio, new byte[8]);
        hub.PublishRaw(f4); // f3を飛ばす
        T.Check("ミラーハブ: 連番が飛んだら視聴者を切断（再接続で復帰）", v.Kicked && v.KickReason == "gap");
        _ = f3;

        var v2 = hub.TryAddViewer(8, "b")!;
        var enc2 = FrameEncryptor.Create("another-pass-1234567", CryptoConstants.MinIterations);
        hub.SetUpstream(enc2.Header.ToJson(), enc2.ConfirmationFrame());
        T.Check("ミラーハブ: 上流が別の配信になったら視聴者を切断", v2.Kicked && v2.KickReason == "upstream-changed");

        var v3 = hub.TryAddViewer(8, "c")!;
        hub.SetUpstream(enc2.Header.ToJson(), enc2.ConfirmationFrame());
        T.Check("ミラーハブ: 同じ配信への再接続では視聴者を切らない", !v3.Kicked);
        var g1 = enc2.Encrypt(FrameType.Audio, new byte[8]);
        hub.PublishRaw(g1);
        T.Check("ミラーハブ: 新しい配信のフレームは連番が1から始まっても通る", !v3.Kicked && v3.Reader.TryRead(out var gx) && gx.SequenceEqual(g1));

        T.Check("ミラーハブ: 視聴者数の上限が効く", hub.TryAddViewer(8, "d") != null && hub.TryAddViewer(8, "e") == null);
        T.Check("ミラーハブ: 不正なヘッダー・確認ブロック・フレームは拒否",
            Throws(() => hub.SetUpstream("{bad", enc.ConfirmationFrame())) == "bad-upstream-header"
            && Throws(() => hub.SetUpstream(header, new byte[40])) == "bad-upstream-confirm"
            && Throws(() => hub.SetUpstream(header, new byte[] { 1, 2, 3 })) == "bad-upstream-confirm"
            && Throws(() => hub.PublishRaw(new byte[5])) == "bad-upstream-frame"
            && Throws(() => hub.PublishRaw(new byte[40])) == "bad-upstream-frame");
    }

    private static EngineOptions Origin(string pw) => new()
    {
        SourcePort = FreePort(), ViewPort = FreePort(), SourcePassword = SrcPw, ViewPassword = pw,
        Iterations = CryptoConstants.MinIterations, ChunkSize = 1024, MaxViewers = 5, Mount = "/m",
        Access = new AccessOptions { RateMaxRequests = 0, MaxConnPerClient = 0 },
        CertDir = Path.Combine(Path.GetTempPath(), "baton-mir-" + Guid.NewGuid().ToString("N")),
    };

    private static async Task PutAsync(EngineOptions o, byte[] data)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("source:" + SrcPw));
        await SendAsync(o.SourcePort, $"PUT /m HTTP/1.1\r\nAuthorization: Basic {basic}\r\nContent-Type: audio/mpeg\r\nTransfer-Encoding: chunked\r\n\r\n", Chunked(data));
    }

    private static async Task EndToEndAsync()
    {
        AppLog.ToConsole = false;
        const string pw1 = "origin-passphrase-0123456789";
        const string pw2 = "second-session-pass-9876543";
        var o1 = Origin(pw1);
        var o = new RelayEngine(o1);
        await o.StartAsync();

        var mo = new EngineOptions
        {
            ViewPort = FreePort(), UpstreamUrl = $"http://127.0.0.1:{o1.ViewPort}/", MaxViewers = 5, UpstreamRetryMs = 200,
            Access = new AccessOptions { RateMaxRequests = 0, MaxConnPerClient = 0 },
        };
        await using var m = new RelayEngine(mo);
        await m.StartAsync();
        await WaitAsync(() => m.Hub!.Ready);
        T.Check("ミラー結合: 上流に接続して視聴を受け付ける", m.Hub!.Ready && m.Stats.Upstream == UpstreamStatus.Connected && m.Stats.Mirror);

        var rnd = new Random(11);
        byte[] Data(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }

        var v = new Viewer();
        _ = Task.Run(() => v.RunAsync(mo.ViewPort, pw1));
        await WaitAsync(() => v.PasswordOk);
        T.Check("ミラー結合: ミラーに繋いでも、元のパスワードで確認ブロックが検証できる", v.PasswordOk);

        var bad = new Viewer();
        _ = Task.Run(() => bad.RunAsync(mo.ViewPort, "wrong-passphrase-000000"));
        await WaitAsync(() => bad.HeaderJson.Length > 0);

        var d1 = Data(30_000);
        await PutAsync(o1, d1);
        await WaitAsync(() => v.Audio().Length >= d1.Length);
        T.Check("ミラー結合: ミラー経由で、元と同じ音声が復号できる", v.Audio().SequenceEqual(d1));
        await WaitAsync(() => bad.Failure != null, 3000);
        T.Check("ミラー結合: 違うパスワードでは復号できない（ミラーは鍵を持たない）", !bad.PasswordOk && bad.Failure != null && bad.Audio().Length == 0);
        T.Check("ミラー結合: ミラー自身は復号も自己検査もしていない", m.Stats.CheckOk == 0 && m.Stats.CheckNg == 0 && m.Stats.Frames > 0 && m.Stats.BytesIn > 0 && m.Stats.SourceActive);
        T.Check("ミラー結合: 元から見た視聴者はミラー1つだけ", o.Stats.PeakViewers == 1);

        // 元が停止→別の配信（新しいパスワード）として再開。ミラーの視聴者はいったん切られ、新しい配信を受け取れる
        await o.DisposeAsync();
        await WaitAsync(() => m.Stats.Upstream == UpstreamStatus.Waiting, 5000);
        T.Check("ミラー結合: 元が止まると待機状態になり、理由が記録される", m.Stats.Upstream == UpstreamStatus.Waiting && m.Stats.UpstreamError.Length > 0);
        var o2o = Origin(pw2);
        o2o.ViewPort = o1.ViewPort;
        await using var o2 = new RelayEngine(o2o);
        await o2.StartAsync();
        await WaitAsync(() => v.Ws.State != WebSocketState.Open, 30_000);
        T.Check("ミラー結合: 元が別の配信になったら、ミラーの視聴者はいったん切断される", v.Ws.State != WebSocketState.Open);
        await WaitAsync(() => m.Stats.Upstream == UpstreamStatus.Connected, 10_000);

        var v2 = new Viewer();
        _ = Task.Run(() => v2.RunAsync(mo.ViewPort, pw2));
        await WaitAsync(() => v2.PasswordOk);
        var d2 = Data(20_000);
        await PutAsync(o2o, d2);
        await WaitAsync(() => v2.Audio().Length >= d2.Length);
        T.Check("ミラー結合: 再接続後、新しい配信の音声が復号できる", v2.PasswordOk && v2.Audio().SequenceEqual(d2));

        try { await v2.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch (WebSocketException) { }
        foreach (var d in new[] { o1.CertDir, o2o.CertDir }) try { Directory.Delete(d!, true); } catch (IOException) { }
    }
}
