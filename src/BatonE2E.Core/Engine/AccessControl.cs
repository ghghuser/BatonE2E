using System.Net;
using System.Text.RegularExpressions;

namespace BatonE2E.Core.Engine;

/// <summary>視聴側のアクセス制御の設定。</summary>
public sealed class AccessOptions
{
    /// <summary>同一クライアント（IP）の同時接続数の上限。0なら無制限。</summary>
    public int MaxConnPerClient { get; set; } = 3;
    /// <summary>連打判定: この秒数の間に…</summary>
    public int RateWindowSec { get; set; } = 10;
    /// <summary>…この回数以上アクセスしたら拒否（0なら連打制限なし）。</summary>
    public int RateMaxRequests { get; set; } = 30;
    /// <summary>拒否する秒数。</summary>
    public int RateBanSec { get; set; } = 60;
    /// <summary>拒否リスト。1行1規則: ip:アドレスまたはCIDR / ua:正規表現 / host:正規表現。#で始まる行は注釈。</summary>
    public List<string> DenyRules { get; set; } = new();
    /// <summary>トンネル（Tailscale Funnel/ngrok）経由の接続元IPを X-Forwarded-For から得る（接続がこのPC内から来た場合のみ有効）。</summary>
    public bool UseProxyHeader { get; set; } = true;

    public bool RateEnabled => RateMaxRequests > 0 && RateWindowSec > 0 && RateBanSec > 0;
}

public enum AccessDecision { Allow, Denied, RateLimited }

public readonly record struct AccessResult(AccessDecision Decision, int RetryAfterSec, bool NewBan, bool LogIt);

/// <summary>接続元の取得（プロキシヘッダー対応）。</summary>
public static class ClientIp
{
    private static IPAddress Map(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    /// <summary>TCP接続元がこのPC内（トンネルのプロセス）で、かつヘッダーがある場合のみ、X-Forwarded-Forの「最も右」（信頼するプロキシが付けた値）を使う。</summary>
    public static string Resolve(IPAddress? remote, string? forwardedFor, bool useProxyHeader)
    {
        if (remote == null) return "?";
        if (useProxyHeader && IPAddress.IsLoopback(remote) && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var last = forwardedFor.Split(',')[^1].Trim();
            if (IPAddress.TryParse(last, out var ip)) return Map(ip).ToString();
        }
        return Map(remote).ToString();
    }
}

/// <summary>拒否リスト・連打制限・同一クライアントの接続数制限。スレッドセーフ。</summary>
public sealed class AccessControl
{
    private sealed class Entry
    {
        public readonly Queue<long> Hits = new();
        public long BanUntil;
        public int Conns;
        public long Last;
    }

    private const int MaxEntries = 50_000;
    private readonly AccessOptions _o;
    private readonly Func<long> _nowMs;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _map = new();
    private readonly Dictionary<string, long> _lastLog = new();
    private readonly List<(IPAddress Net, int Bits)> _ips = new();
    private readonly List<Regex> _ua = new();
    private readonly List<Regex> _host = new();
    private long _lastClean;

    /// <summary>連打で拒否を始めたとき（接続中の視聴者の切断に使う）。</summary>
    public event Action<string>? Banned;

    public AccessControl(AccessOptions o, Func<long>? nowMs = null)
    {
        _o = o;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        var err = ParseRules(o.DenyRules, _ips, _ua, _host);
        if (err != null) throw new ArgumentException(err);
    }

    /// <summary>規則を検証する。問題があれば「n行目: 理由」。</summary>
    public static string? ValidateRules(IEnumerable<string> rules) => ParseRules(rules, new(), new(), new());

    private static string? ParseRules(IEnumerable<string> rules, List<(IPAddress, int)> ips, List<Regex> ua, List<Regex> host)
    {
        var n = 0;
        foreach (var raw in rules)
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var i = line.IndexOf(':');
            if (i <= 0) return $"{n}: ip: / ua: / host: のどれかで始めてください";
            var kind = line[..i].Trim().ToLowerInvariant();
            var body = line[(i + 1)..].Trim();
            if (body.Length == 0) return $"{n}: 内容が空です";
            switch (kind)
            {
                case "ip":
                    if (!TryParseNet(body, out var net, out var bits)) return $"{n}: IPアドレス/CIDRとして読めません";
                    ips.Add((net, bits));
                    break;
                case "ua":
                case "host":
                    if (body.Length > 200) return $"{n}: 正規表現が長すぎます（200文字まで）";
                    try
                    {
                        var re = new Regex(body, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
                        (kind == "ua" ? ua : host).Add(re);
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    {
                        return $"{n}: 正規表現が使えません（先読み・後方参照は不可）";
                    }
                    break;
                default:
                    return $"{n}: ip: / ua: / host: のどれかで始めてください";
            }
        }
        return null;
    }

    private static bool TryParseNet(string s, out IPAddress net, out int bits)
    {
        net = IPAddress.None; bits = 0;
        var slash = s.IndexOf('/');
        var addr = slash < 0 ? s : s[..slash];
        if (!IPAddress.TryParse(addr, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var max = ip.GetAddressBytes().Length * 8;
        bits = max;
        if (slash >= 0 && (!int.TryParse(s[(slash + 1)..], out bits) || bits < 0 || bits > max)) return false;
        net = ip;
        return true;
    }

    private static bool InNet(IPAddress ip, IPAddress net, int bits)
    {
        var a = ip.GetAddressBytes();
        var b = net.GetAddressBytes();
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (0xFF << (8 - bits)) & 0xFF;
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }

    private static bool Matches(List<Regex> list, string? s)
    {
        if (list.Count == 0 || string.IsNullOrEmpty(s)) return false;
        if (s.Length > 512) s = s[..512];
        foreach (var re in list)
        {
            try { if (re.IsMatch(s)) return true; }
            catch (RegexMatchTimeoutException) { return true; } // 判定できないものは拒否側に倒す
        }
        return false;
    }

    private bool IsDenied(string ip, string? ua, string? host)
    {
        if (_ips.Count > 0 && IPAddress.TryParse(ip, out var a))
        {
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            foreach (var (net, bits) in _ips) if (InNet(a, net, bits)) return true;
        }
        return Matches(_ua, ua) || Matches(_host, host);
    }

    private Entry Get(string ip)
    {
        if (!_map.TryGetValue(ip, out var e)) _map[ip] = e = new Entry();
        return e;
    }

    private void Clean(long now)
    {
        if (now - _lastClean < 30_000 && _map.Count < MaxEntries) return;
        _lastClean = now;
        var idle = now - Math.Max(1, _o.RateWindowSec) * 1000L;
        foreach (var k in _map.Where(kv => kv.Value.Conns == 0 && kv.Value.BanUntil <= now && kv.Value.Last < idle).Select(kv => kv.Key).ToList())
            _map.Remove(k);
        foreach (var k in _lastLog.Where(kv => now - kv.Value > 60_000).Select(kv => kv.Key).ToList()) _lastLog.Remove(k);
    }

    /// <summary>リクエスト1件の可否を判定（アクセス1回として数える）。</summary>
    public AccessResult Check(string ip, string? userAgent, string? host)
    {
        var now = _nowMs();
        if (IsDenied(ip, userAgent, host))
        {
            lock (_lock)
            {
                var log = !_lastLog.TryGetValue("d|" + ip, out var t) || now - t > 60_000;
                if (log) _lastLog["d|" + ip] = now;
                return new AccessResult(AccessDecision.Denied, 0, false, log);
            }
        }
        if (!_o.RateEnabled) return new AccessResult(AccessDecision.Allow, 0, false, false);
        var banned = false;
        AccessResult res;
        lock (_lock)
        {
            Clean(now);
            var e = Get(ip);
            if (e.BanUntil > now) return new AccessResult(AccessDecision.RateLimited, (int)Math.Ceiling((e.BanUntil - now) / 1000.0), false, false);
            e.Last = now;
            var cutoff = now - _o.RateWindowSec * 1000L;
            while (e.Hits.Count > 0 && e.Hits.Peek() <= cutoff) e.Hits.Dequeue();
            e.Hits.Enqueue(now);
            if (e.Hits.Count >= _o.RateMaxRequests)
            {
                e.BanUntil = now + _o.RateBanSec * 1000L;
                e.Hits.Clear();
                banned = true;
                res = new AccessResult(AccessDecision.RateLimited, _o.RateBanSec, true, true);
            }
            else res = new AccessResult(AccessDecision.Allow, 0, false, false);
        }
        if (banned) Banned?.Invoke(ip);
        return res;
    }

    /// <summary>同時接続の枠を取る。上限なら false。</summary>
    public bool TryAcquire(string ip)
    {
        if (_o.MaxConnPerClient <= 0) return true;
        lock (_lock)
        {
            var e = Get(ip);
            if (e.Conns >= _o.MaxConnPerClient) return false;
            e.Conns++;
            e.Last = _nowMs();
            return true;
        }
    }

    public void Release(string ip)
    {
        if (_o.MaxConnPerClient <= 0) return;
        lock (_lock) { if (_map.TryGetValue(ip, out var e) && e.Conns > 0) e.Conns--; }
    }
}
