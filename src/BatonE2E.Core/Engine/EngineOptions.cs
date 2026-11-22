using BatonE2E.Core.Crypto;

namespace BatonE2E.Core.Engine;

public enum SourceTlsMode { Auto, Off, On }

public sealed class EngineOptions
{
    public int SourcePort { get; set; } = 8000;
    /// <summary>source受け口の待ち受けアドレス。既定は127.0.0.1（このPC内のみ）。それ以外は外部から接続可能になるため、TLSと強いパスワードを必須とする（Validate）。</summary>
    public string SourceBind { get; set; } = "127.0.0.1";
    /// <summary>auto: 最初の1バイトで平文/TLSを判定。off: 平文のみ。on: TLSのみ。</summary>
    public SourceTlsMode SourceTls { get; set; } = SourceTlsMode.Auto;
    /// <summary>証明書の保存先。未指定なら exe と同じ場所の tls フォルダ。</summary>
    public string? CertDir { get; set; }
    /// <summary>証明書を作り直す（oddcast側の保存済み指紋の削除が必要）。</summary>
    public bool RegenCert { get; set; }
    /// <summary>ユーザー名を問わずに受け付ける（配信ソフトにユーザー名欄がない場合の逃げ道。パスワードは必ず検査）。</summary>
    public bool AcceptAnySourceUser { get; set; }
    /// <summary>視聴ページの題名・見出し。既定ではアプリ名を出さない。</summary>
    public string PageTitle { get; set; } = "ライブ配信";
    public string ViewBind { get; set; } = "127.0.0.1";
    public int ViewPort { get; set; } = 8080;
    /// <summary>視聴ページのURLパス（例: /live-abc）。既定は "/"。WebSocketは「パス/ws」。他のパスは404。</summary>
    public string ViewPath { get; set; } = "/";
    /// <summary>127.0.0.1以外での視聴待ち受けを許可（TLS使用時のみ有効化する想定）。</summary>
    public bool AllowExternalBind { get; set; }
    public string Mount { get; set; } = "/stream";
    /// <summary>視聴側のアクセス制御（拒否リスト・連打制限・同一クライアントの接続数）。</summary>
    public AccessOptions Access { get; set; } = new();
    public string SourcePassword { get; set; } = "";
    public string ViewPassword { get; set; } = "";
    public int MaxViewers { get; set; } = 20;
    public int ChunkSize { get; set; } = 8192;
    public int Iterations { get; set; } = CryptoConstants.DefaultIterations;
    public int ViewerQueue { get; set; } = 64;
    /// <summary>視聴ページの差し替えファイル。未指定なら exeと同じ場所の viewer.html、なければ埋め込み。</summary>
    public string? ViewerPageFile { get; set; }

    /// <summary>視聴URLのパスを正規化。使える文字は英数字と - _ . ~ / のみ。不正なら "/"。</summary>
    public static string NormalizeViewPath(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return "/";
        if (p[0] != '/') p = "/" + p;
        p = p.TrimEnd('/');
        if (p.Length == 0) return "/";
        foreach (var c in p)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or '~' or '/')) return "/";
        var segs = p[1..].Split('/');
        if (segs.Any(s => s.Length == 0 || s == "." || s == "..")) return "/";
        return p;
    }

    public static bool IsLoopbackBind(string bind) => System.Net.IPAddress.TryParse(bind, out var ip) && System.Net.IPAddress.IsLoopback(ip);

    /// <summary>外部から接続できる設定のときのsourceパスワード最小文字数。</summary>
    public const int MinExternalSourcePasswordLength = 12;

    public void Validate()
    {
        if (!System.Net.IPAddress.TryParse(SourceBind, out _)) throw new E2eCryptoException("bad-source-bind");
        if (!IsLoopbackBind(SourceBind))
        {
            if (SourceTls == SourceTlsMode.Off) throw new E2eCryptoException("source-bind-needs-tls");
            if (SourcePassword.Length < MinExternalSourcePasswordLength) throw new E2eCryptoException("source-bind-needs-strong-password");
        }
        if (string.IsNullOrEmpty(SourcePassword)) throw new E2eCryptoException("empty-source-password");
        if (string.IsNullOrEmpty(ViewPassword)) throw new E2eCryptoException("empty-view-password");
        ViewPath = NormalizeViewPath(ViewPath);
        Access.MaxConnPerClient = Math.Clamp(Access.MaxConnPerClient, 0, 1000);
        Access.RateWindowSec = Math.Clamp(Access.RateWindowSec, 0, 3600);
        Access.RateMaxRequests = Math.Clamp(Access.RateMaxRequests, 0, 100000);
        Access.RateBanSec = Math.Clamp(Access.RateBanSec, 0, 86400);
        var rerr = AccessControl.ValidateRules(Access.DenyRules);
        if (rerr != null) throw new E2eCryptoException("bad-deny-rule: " + rerr);
        if (string.IsNullOrWhiteSpace(PageTitle)) PageTitle = "ライブ配信";
        ChunkSize = Math.Clamp(ChunkSize, 1024, 262_144);
        MaxViewers = Math.Clamp(MaxViewers, 1, 1000);
        ViewerQueue = Math.Clamp(ViewerQueue, 8, 4096);
    }
}
