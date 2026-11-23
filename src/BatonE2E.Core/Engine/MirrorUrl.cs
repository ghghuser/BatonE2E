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

    /// <summary>上流が自分自身（同じPCの自分の視聴ポート・視聴パス）を指していないか。自分を指すと接続が空回りする。</summary>
    public static bool IsSelf(Uri upstream, int viewPort, string viewPath)
    {
        if (upstream.Port != viewPort) return false;
        var h = upstream.Host.Trim('[', ']');
        var local = h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || (System.Net.IPAddress.TryParse(h, out var ip) && (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.Any)));
        if (!local) return false;
        var vp = EngineOptions.NormalizeViewPath(viewPath);
        var own = (vp == "/" ? "" : vp) + "/ws";
        return string.Equals(upstream.AbsolutePath, own, StringComparison.Ordinal);
    }
}
