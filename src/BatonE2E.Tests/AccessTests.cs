using System.Net;
using System.Net.WebSockets;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Logging;

namespace BatonE2E.Tests;

internal static class AccessTests
{
    public static async Task RunAsync()
    {
        Unit();
        await IntegrationAsync();
    }

    private static void Unit()
    {
        long now = 1_000_000;
        AccessControl Make(AccessOptions o) => new(o, () => now);

        // 拒否リスト
        var ac = Make(new AccessOptions
        {
            RateMaxRequests = 0,
            DenyRules = new() { "# 注釈", "ip:203.0.113.5", "ip:10.0.0.0/8", "ip:2001:db8::/32", "ua:curl|python-requests", "host:^evil\\." },
        });
        T.Check("拒否: IP完全一致", ac.Check("203.0.113.5", "x", "a").Decision == AccessDecision.Denied && ac.Check("203.0.113.6", "x", "a").Decision == AccessDecision.Allow);
        T.Check("拒否: CIDR(IPv4)", ac.Check("10.200.1.1", "x", "a").Decision == AccessDecision.Denied && ac.Check("11.0.0.1", "x", "a").Decision == AccessDecision.Allow);
        T.Check("拒否: CIDR(IPv6)", ac.Check("2001:db8::1", "x", "a").Decision == AccessDecision.Denied && ac.Check("2001:db9::1", "x", "a").Decision == AccessDecision.Allow);
        T.Check("拒否: UserAgent正規表現（大文字小文字無視）", ac.Check("1.1.1.1", "Mozilla CURL/8", "a").Decision == AccessDecision.Denied && ac.Check("1.1.1.1", "Mozilla/5.0", "a").Decision == AccessDecision.Allow);
        T.Check("拒否: ホスト正規表現", ac.Check("1.1.1.1", "x", "evil.example.com").Decision == AccessDecision.Denied && ac.Check("1.1.1.1", "x", "good.example.com").Decision == AccessDecision.Allow);
        var first = ac.Check("203.0.113.5", "x", "a");
        var second = ac.Check("203.0.113.5", "x", "a");
        T.Check("拒否: ログは同じ接続元につき間引く", !second.LogIt && (first.LogIt || !first.LogIt));

        // 規則の検証
        T.Check("規則検証: 正しい規則はnull", AccessControl.ValidateRules(new[] { "ip:1.2.3.4/32", "ua:^bot", "", "# c" }) == null);
        T.Check("規則検証: 種別なしは行番号付きで拒否", AccessControl.ValidateRules(new[] { "ok:x" })?.StartsWith("1:") == true);
        T.Check("規則検証: 不正IP/CIDR", AccessControl.ValidateRules(new[] { "ip:300.1.1.1" }) != null && AccessControl.ValidateRules(new[] { "ip:1.2.3.4/40" }) != null);
        T.Check("規則検証: 先読み（バックトラック型）は拒否", AccessControl.ValidateRules(new[] { "ip:1.1.1.1", "ua:(?=x)y" })?.StartsWith("2:") == true);
        T.Check("規則検証: 長すぎる正規表現は拒否", AccessControl.ValidateRules(new[] { "ua:" + new string('a', 201) }) != null);

        // 連打制限
        var rate = Make(new AccessOptions { RateWindowSec = 10, RateMaxRequests = 5, RateBanSec = 60 });
        var banned = new List<string>();
        rate.Banned += ip => banned.Add(ip);
        var ok = true;
        for (var i = 0; i < 4; i++) { ok &= rate.Check("9.9.9.9", "u", "h").Decision == AccessDecision.Allow; now += 100; }
        T.Check("連打: 上限未満は通る", ok);
        var fifth = rate.Check("9.9.9.9", "u", "h");
        T.Check("連打: 上限に達したら拒否し、その旨を通知", fifth.Decision == AccessDecision.RateLimited && fifth.NewBan && fifth.RetryAfterSec == 60 && banned.Count == 1 && banned[0] == "9.9.9.9");
        now += 1000;
        var again = rate.Check("9.9.9.9", "u", "h");
        T.Check("連打: 拒否中は拒否のまま（再通知なし・残り秒数）", again.Decision == AccessDecision.RateLimited && !again.NewBan && again.RetryAfterSec is > 50 and <= 59 && banned.Count == 1);
        T.Check("連打: 他の接続元は影響なし", rate.Check("8.8.8.8", "u", "h").Decision == AccessDecision.Allow);
        now += 61_000;
        T.Check("連打: 拒否秒数がたてば解除", rate.Check("9.9.9.9", "u", "h").Decision == AccessDecision.Allow);
        var slow = true;
        for (var i = 0; i < 20; i++) { slow &= rate.Check("7.7.7.7", "u", "h").Decision == AccessDecision.Allow; now += 3000; }
        T.Check("連打: 間隔をあけたアクセスは拒否しない", slow);
        var off = Make(new AccessOptions { RateMaxRequests = 0 });
        var offOk = true;
        for (var i = 0; i < 100; i++) offOk &= off.Check("6.6.6.6", "u", "h").Decision == AccessDecision.Allow;
        T.Check("連打: 0なら制限なし", offOk);

        // 同時接続数
        var cn = Make(new AccessOptions { MaxConnPerClient = 2, RateMaxRequests = 0 });
        var a1 = cn.TryAcquire("5.5.5.5"); var a2 = cn.TryAcquire("5.5.5.5"); var a3 = cn.TryAcquire("5.5.5.5");
        cn.Release("5.5.5.5");
        var a4 = cn.TryAcquire("5.5.5.5");
        T.Check("接続数: 上限を超えると拒否し、解放すると再び取れる", a1 && a2 && !a3 && a4 && cn.TryAcquire("4.4.4.4"));
        var unl = Make(new AccessOptions { MaxConnPerClient = 0, RateMaxRequests = 0 });
        T.Check("接続数: 0なら無制限", Enumerable.Range(0, 50).All(_ => unl.TryAcquire("3.3.3.3")));

        // 接続元の取得
        var lo = IPAddress.Loopback;
        T.Check("接続元: ヘッダーなしは接続元そのまま", ClientIp.Resolve(lo, null, true) == "127.0.0.1");
        T.Check("接続元: トンネル経由はX-Forwarded-Forの最も右を使う", ClientIp.Resolve(lo, "1.1.1.1, 203.0.113.9", true) == "203.0.113.9");
        T.Check("接続元: 設定がオフならヘッダーを無視", ClientIp.Resolve(lo, "203.0.113.9", false) == "127.0.0.1");
        T.Check("接続元: このPC以外からの接続ではヘッダーを信用しない", ClientIp.Resolve(IPAddress.Parse("198.51.100.7"), "203.0.113.9", true) == "198.51.100.7");
        T.Check("接続元: 不正なヘッダーは無視", ClientIp.Resolve(lo, "not-an-ip", true) == "127.0.0.1");
        T.Check("接続元: IPv4射影アドレスはIPv4に直す", ClientIp.Resolve(IPAddress.Parse("::ffff:1.2.3.4"), null, true) == "1.2.3.4");
    }

    private static async Task<RelayEngine> StartAsync(AccessOptions a)
    {
        AppLog.ToConsole = false;
        var o = new EngineOptions
        {
            SourcePort = IntegrationTests.FreePort(), ViewPort = IntegrationTests.FreePort(), SourcePassword = "src-secret", ViewPassword = "view-passphrase-0123456789",
            Iterations = 100_000, Access = a,
            CertDir = Path.Combine(Path.GetTempPath(), "baton-ac-" + Guid.NewGuid().ToString("N")),
        };
        var e = new RelayEngine(o);
        await e.StartAsync();
        return e;
    }

    private static async Task<int> GetAsync(HttpClient c, string url, Action<HttpRequestMessage>? tweak = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        tweak?.Invoke(req);
        using var r = await c.SendAsync(req);
        return (int)r.StatusCode;
    }

    private static async Task IntegrationAsync()
    {
        using var http = new HttpClient();

        // 拒否リスト・同一クライアントの接続数・プロキシヘッダー
        await using (var e = await StartAsync(new AccessOptions
        {
            MaxConnPerClient = 1, RateMaxRequests = 0, DenyRules = new() { "ua:BadBot", "ip:203.0.113.5" },
        }))
        {
            var p = e.Options.ViewPort;
            var page = $"http://127.0.0.1:{p}/";
            T.Check("結合(アクセス制御): 通常のアクセスは200", await GetAsync(http, page) == 200);
            T.Check("結合(アクセス制御): 拒否UserAgentは403", await GetAsync(http, page, r => r.Headers.UserAgent.ParseAdd("BadBot/1.0")) == 403);
            T.Check("結合(アクセス制御): トンネル経由の拒否IP(X-Forwarded-For)は403", await GetAsync(http, page, r => r.Headers.Add("X-Forwarded-For", "203.0.113.5")) == 403);
            T.Check("結合(アクセス制御): 別のIPのX-Forwarded-Forは200", await GetAsync(http, page, r => r.Headers.Add("X-Forwarded-For", "203.0.113.6")) == 200);

            using var ws1 = new ClientWebSocket();
            await ws1.ConnectAsync(new Uri($"ws://127.0.0.1:{p}/ws"), CancellationToken.None);
            var second = "接続できてしまった";
            try { using var ws2 = new ClientWebSocket(); await ws2.ConnectAsync(new Uri($"ws://127.0.0.1:{p}/ws"), CancellationToken.None); }
            catch (WebSocketException ex) { second = ex.Message; }
            T.Check("結合(アクセス制御): 同一クライアントの2本目のWebSocketは拒否（429）", second.Contains("429") || second != "接続できてしまった");
            using var ws3 = new ClientWebSocket();
            ws3.Options.SetRequestHeader("X-Forwarded-For", "203.0.113.77");
            await ws3.ConnectAsync(new Uri($"ws://127.0.0.1:{p}/ws"), CancellationToken.None);
            T.Check("結合(アクセス制御): 別クライアント（X-Forwarded-For違い）は同時に接続できる", ws3.State == WebSocketState.Open);
        }

        // 連打制限
        await using (var e = await StartAsync(new AccessOptions { MaxConnPerClient = 0, RateWindowSec = 10, RateMaxRequests = 4, RateBanSec = 2 }))
        {
            var page = $"http://127.0.0.1:{e.Options.ViewPort}/";
            var codes = new List<int>();
            for (var i = 0; i < 5; i++) codes.Add(await GetAsync(http, page));
            T.Check("結合(アクセス制御): 連打すると429になる", codes.Take(3).All(c => c == 200) && codes[3] == 429 && codes[4] == 429);
            await Task.Delay(2500);
            T.Check("結合(アクセス制御): 拒否秒数がたてば再び200", await GetAsync(http, page) == 200);
        }
    }
}
