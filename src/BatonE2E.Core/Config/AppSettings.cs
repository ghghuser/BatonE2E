using System.Text.Encodings.Web;
using System.Text.Json;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Security;

namespace BatonE2E.Core.Config;

/// <summary>設定（config.json）。GUI・将来のコンソール版で共通。パスワードが空なら起動ごとに自動生成。</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "ja";
    public int SourcePort { get; set; } = 8000;
    /// <summary>sourceの待ち受けアドレス。127.0.0.1以外は外部公開（TLS必須・強いパスワード必須）。</summary>
    public string SourceBind { get; set; } = "127.0.0.1";
    public int ViewPort { get; set; } = 8080;
    /// <summary>視聴ページのURLパス（例: /live-abc）。</summary>
    public string ViewPath { get; set; } = "/";
    public string Mount { get; set; } = "/stream";
    /// <summary>空なら起動ごとに自動生成。</summary>
    public string SourcePassword { get; set; } = "";
    /// <summary>空なら起動ごとに自動生成。</summary>
    public string ViewPassword { get; set; } = "";
    public string PageTitle { get; set; } = "ライブ配信";
    public int MaxViewers { get; set; } = 20;
    /// <summary>auto / off / on</summary>
    public string SourceTls { get; set; } = "auto";
    public bool AcceptAnySourceUser { get; set; }
    /// <summary>ログをファイル（exeの横）にも保存する。</summary>
    public bool LogToFile { get; set; }

    // ---- 視聴側のアクセス制御 ----
    /// <summary>同一クライアント（IP）の同時接続数。0=無制限。</summary>
    public int MaxConnPerClient { get; set; } = 3;
    public int RateWindowSec { get; set; } = 10;
    /// <summary>RateWindowSec秒の間にこの回数以上アクセスしたら拒否。0=連打制限なし。</summary>
    public int RateMaxRequests { get; set; } = 30;
    public int RateBanSec { get; set; } = 60;
    /// <summary>拒否リスト（1行1規則: ip: / ua: / host:）。</summary>
    public string DenyRules { get; set; } = "";
    /// <summary>トンネル経由の接続元IP（X-Forwarded-For）を使う。</summary>
    public bool UseProxyHeader { get; set; } = true;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public const int BackupKeep = 10;
    /// <summary>視聴用パスフレーズの最小文字数（これ未満は保存・開始を拒否）。日本語も可（NFC正規化して使用）。</summary>
    public const int MinViewPasswordLength = 8;
    /// <summary>これ未満は警告（総当たりで破られやすい）。</summary>
    public const int RecommendedViewPasswordLength = 12;

    public bool IsWeakViewPassword => ViewPassword.Length is > 0 and < RecommendedViewPasswordLength;

    /// <summary>範囲外・不正な値を安全な値へ直す。</summary>
    public void Normalize()
    {
        if (Language != "ja" && Language != "en") Language = "ja";
        if (SourcePort is < 1 or > 65535) SourcePort = 8000;
        SourceBind = System.Net.IPAddress.TryParse((SourceBind ?? "").Trim(), out var bindIp) ? bindIp.ToString() : "127.0.0.1";
        if (ViewPort is < 1 or > 65535) ViewPort = 8080;
        var m = (Mount ?? "").Trim();
        if (m.Length == 0) m = "/stream";
        if (m[0] != '/') m = "/" + m;
        if (m.Any(c => c <= ' ' || c == '?' || c == '#')) m = "/stream";
        Mount = m;
        ViewPath = EngineOptions.NormalizeViewPath(ViewPath);
        MaxViewers = Math.Clamp(MaxViewers, 1, 1000);
        var t = (SourceTls ?? "").ToLowerInvariant();
        SourceTls = t is "auto" or "off" or "on" ? t : "auto";
        if (string.IsNullOrWhiteSpace(PageTitle)) PageTitle = "ライブ配信";
        MaxConnPerClient = Math.Clamp(MaxConnPerClient, 0, 1000);
        RateWindowSec = Math.Clamp(RateWindowSec, 0, 3600);
        RateMaxRequests = Math.Clamp(RateMaxRequests, 0, 100000);
        RateBanSec = Math.Clamp(RateBanSec, 0, 86400);
        DenyRules ??= "";
        SourcePassword ??= "";
        ViewPassword ??= "";
    }

    /// <summary>設定の問題点（Key=Langのキー、Field=問題のある項目名、Args=文言の引数）。</summary>
    public sealed record Issue(string Key, string Field, object[] Args);

    private static Issue Mk(string key, string field, params object[] args) => new(key, field, args);

    /// <summary>保存・開始できない問題。なければ null。</summary>
    public Issue? Check()
    {
        if (SourcePort == ViewPort) return Mk("gui.err.sameport", "SourcePort,ViewPort");
        if (ViewPassword.Length is > 0 and < MinViewPasswordLength) return Mk("gui.err.shortpass", "ViewPassword", MinViewPasswordLength);
        if (!EngineOptions.IsLoopbackBind(SourceBind))
        {
            if (SourceTls == "off") return Mk("gui.err.bindtls", "SourceTls");
            if (SourcePassword.Length is > 0 and < EngineOptions.MinExternalSourcePasswordLength)
                return Mk("gui.err.bindpass", "SourcePassword", EngineOptions.MinExternalSourcePasswordLength);
        }
        var rerr = AccessControl.ValidateRules(DenyRuleLines());
        if (rerr != null) return Mk("gui.err.denyrule", "DenyRules", rerr);
        return null;
    }

    public List<string> DenyRuleLines() => (DenyRules ?? "").Replace("\r", "").Split('\n').ToList();

    /// <summary>セキュリティを下げる設定の一覧（Langの gui.sec.* のキー）。変更時は必ず警告する。</summary>
    public List<string> SecurityWarnings()
    {
        var w = new List<string>();
        if (SourceTls == "off") w.Add("gui.sec.tlsoff");
        if (AcceptAnySourceUser) w.Add("gui.sec.anyuser");
        if (IsWeakViewPassword) w.Add("gui.sec.weakview");
        if (SourcePassword.Length is > 0 and < 8) w.Add("gui.sec.weaksrc");
        if (!EngineOptions.IsLoopbackBind(SourceBind)) w.Add("gui.sec.extbind");
        if (MaxConnPerClient == 0) w.Add("gui.sec.noconnlimit");
        if (RateMaxRequests == 0 || RateWindowSec == 0 || RateBanSec == 0) w.Add("gui.sec.norate");
        if (!UseProxyHeader) w.Add("gui.sec.noproxy");
        return w;
    }

    /// <summary>読み込む。無ければ既定値。壊れていれば壊れたファイルを退避して既定値（warnedに理由）。</summary>
    public static AppSettings Load(string path, out bool corrupted)
    {
        corrupted = false;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
            s.Normalize();
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            corrupted = true;
            try { File.Copy(path, path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { /* 退避失敗は無視 */ }
            return new AppSettings();
        }
    }

    /// <summary>読み込むだけ（ファイルは触らない）。無い・壊れていれば null。</summary>
    public static AppSettings? TryRead(string path)
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
            s?.Normalize();
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>保存。既存ファイルと内容が違うときは backup フォルダへ複製（直近 BackupKeep 件）。原子的に書き込み。</summary>
    public void Save(string path)
    {
        Normalize();
        var json = ToJson();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        if (File.Exists(path))
        {
            string old = "";
            try { old = File.ReadAllText(path); } catch (IOException) { }
            if (old != json && old.Length > 0)
            {
                var bdir = BackupDir(path);
                Directory.CreateDirectory(bdir);
                var at = DateTime.Now;
                string target;
                do { target = Path.Combine(bdir, "config-" + at.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"); at = at.AddTicks(1); }
                while (File.Exists(target));
                File.WriteAllText(target, old);
                foreach (var f in Directory.GetFiles(bdir, "config-*.json").OrderByDescending(x => x).Skip(BackupKeep))
                    try { File.Delete(f); } catch (IOException) { }
            }
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, true);
    }

    public static string BackupDir(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "backup");

    /// <summary>最新のバックアップの内容を読み出す（無ければ null）。ファイルは書き換えない。</summary>
    public static AppSettings? LoadLatestBackup(string path)
    {
        var bdir = BackupDir(path);
        if (!Directory.Exists(bdir)) return null;
        foreach (var f in Directory.GetFiles(bdir, "config-*.json").OrderByDescending(x => x))
        {
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(f), Json);
                if (s != null) { s.Normalize(); return s; }
            }
            catch (JsonException) { }
        }
        return null;
    }

    /// <summary>エンジン用の設定へ変換。空のパスワードはここで自動生成する。</summary>
    public EngineOptions ToEngineOptions()
    {
        Normalize();
        return new EngineOptions
        {
            SourcePort = SourcePort,
            SourceBind = SourceBind,
            ViewPort = ViewPort,
            Mount = Mount,
            ViewPath = ViewPath,
            Access = new AccessOptions
            {
                MaxConnPerClient = MaxConnPerClient, RateWindowSec = RateWindowSec, RateMaxRequests = RateMaxRequests,
                RateBanSec = RateBanSec, UseProxyHeader = UseProxyHeader, DenyRules = DenyRuleLines(),
            },
            MaxViewers = MaxViewers,
            PageTitle = PageTitle,
            AcceptAnySourceUser = AcceptAnySourceUser,
            SourceTls = Enum.Parse<SourceTlsMode>(SourceTls, true),
            SourcePassword = SourcePassword.Length > 0 ? SourcePassword : PassphraseGenerator.Generate(4, 4),
            ViewPassword = ViewPassword.Length > 0 ? ViewPassword : PassphraseGenerator.Generate(),
        };
    }
}
