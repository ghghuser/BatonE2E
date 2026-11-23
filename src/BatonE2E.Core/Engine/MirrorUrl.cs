namespace BatonE2E.Core.Engine;

/// <summary>ミラーの上流（元のBatonE2E）の指定。視聴ページのURLから、WebSocketの接続先を作る。</summary>
public static class MirrorUrl
{
    /// <summary>視聴ページのURL（http / https / ws / wss）から、上流のWebSocket接続先（…/ws）を作る。不正なら null。</summary>
    public static Uri? ToWebSocketUri(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Length > 2048) return null;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u)) return null;
        var scheme = u.Scheme.ToLowerInvariant() switch { "http" or "ws" => "ws", "https" or "wss" => "wss", _ => null };
        if (scheme == null || string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0) return null;
        // 元の側の経路は「視聴パス/ws」。問い合わせ・断片は使わない
        var path = u.AbsolutePath.TrimEnd('/');
        return Uri.TryCreate($"{scheme}://{u.Authority}{path}/ws", UriKind.Absolute, out var r) ? r : null;
    }

    /// <summary>上流が自分自身（このPCの、自分の視聴ポート）を指していないか。自分のポートへ接続すると、配信が空回りする。</summary>
    public static bool IsSelf(Uri upstream, int viewPort)
    {
        if (upstream.Port != viewPort) return false;
        var h = upstream.Host.Trim('[', ']');
        return h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || (System.Net.IPAddress.TryParse(h, out var ip) && (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.Any)));
    }

    /// <summary>ミラーの視聴ページのパス。上流のパス + "_mirror-番号"（例: /live-abc → /live-abc_mirror-1）。
    /// 利用者が決められず、どの配信のミラーかがURLから分かる。使えない文字を含む等で作れなければ /_mirror-番号。</summary>
    public static string MirrorViewPath(string? upstreamUrl, int number)
    {
        var n = Math.Clamp(number, 1, 99);
        var suffix = "_mirror-" + n;
        var up = ToWebSocketUri(upstreamUrl);
        var basePath = up == null ? "" : up.AbsolutePath[..^"/ws".Length];
        var p = EngineOptions.NormalizeViewPath(basePath + suffix);
        return p == "/" ? "/" + suffix : p;
    }

    /// <summary>上流の視聴ページ自体のURL（題名の取得用）。http / https のまま、ws / wss は http / https へ。</summary>
    public static Uri? ToPageUri(string? text)
    {
        var up = ToWebSocketUri(text);
        if (up == null) return null;
        var scheme = up.Scheme == "wss" ? "https" : "http";
        var path = up.AbsolutePath[..^"/ws".Length];
        return Uri.TryCreate($"{scheme}://{up.Authority}{(path.Length == 0 ? "/" : path)}", UriKind.Absolute, out var r) ? r : null;
    }

    /// <summary>上流の視聴ページの題名（&lt;title&gt;）を取得する。取れなければ null（短い時間で諦める）。</summary>
    public static async Task<string?> FetchTitleAsync(Uri pageUri, CancellationToken ct, int timeoutMs = 4000)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BatonE2E-Mirror");
            using var res = await http.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return null;
            await using var st = await res.Content.ReadAsStreamAsync(ct);
            var buf = new byte[65536];
            var n = 0;
            while (n < buf.Length)
            {
                var r = await st.ReadAsync(buf.AsMemory(n), ct);
                if (r == 0) break;
                n += r;
            }
            return ParseTitle(System.Text.Encoding.UTF8.GetString(buf, 0, n));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>HTMLから&lt;title&gt;の文字を取り出す（実体参照は戻し、前後の空白を除き、長すぎれば切る）。無ければ null。</summary>
    public static string? ParseTitle(string html)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>(.*?)</title>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
        if (!m.Success) return null;
        var t = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        if (t.Length == 0) return null;
        return t.Length > 100 ? t[..100] : t;
    }
}
