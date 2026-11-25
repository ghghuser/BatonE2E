using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BatonE2E.Core.Engine;
using BatonE2E.Core.Security;

namespace BatonE2E.Core.Config;

/// <summary>設定（config.json）。GUI・将来のコンソール版で共通。パスワードが空なら起動ごとに自動生成。</summary>
public sealed class AppSettings
{
    /// <summary>system（OSの言語に合わせる。日本語以外は英語）／ja／en。</summary>
    public string Language { get; set; } = "system";
    /// <summary>relay=配信ソフトから受けて中継する（通常）／mirror=別のBatonE2Eから暗号文のまま受けて中継する。</summary>
    public string Mode { get; set; } = "relay";
    /// <summary>ミラーの上流（元のBatonE2Eの視聴ページのURL）。</summary>
    public string UpstreamUrl { get; set; } = "";
    public bool IsMirror => Mode == "mirror";
    /// <summary>同じ元に複数のミラーを置くときの番号（1〜99）。</summary>
    public int MirrorNumber { get; set; } = 1;
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
    /// <summary>最小化したらタスクトレイへ入れる（タスクバーから消える）。</summary>
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>視聴者が接続したときに鳴らす音声ファイル（wav/mp3/wma）。空なら鳴らさない。PC固有のため全体の設定（プロファイル対象外）。</summary>
    public string NotifySound { get; set; } = "";
    /// <summary>接続通知音を鳴らすか（音のファイル指定とは別に、いつでも切れる）。</summary>
    public bool NotifySoundEnabled { get; set; } = true;
    /// <summary>画面の配色。auto=OSに合わせる / light / dark。</summary>
    public string Theme { get; set; } = "auto";
    /// <summary>いま使っているプロファイルの名前（全体の設定。config.json に保存）。空なら保存時に「既定」。</summary>
    public string CurrentProfile { get; set; } = "";
    public static readonly string[] SoundExtensions = { ".wav", ".mp3", ".wma" };

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
        if (Language != "ja" && Language != "en") Language = "system";
        if (Theme != "light" && Theme != "dark") Theme = "auto";
        Mode = Mode == "mirror" ? "mirror" : "relay";
        MirrorNumber = Math.Clamp(MirrorNumber, 1, 99);
        UpstreamUrl = (UpstreamUrl ?? "").Trim();
        if (UpstreamUrl.Length > 2048) UpstreamUrl = "";
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
        CurrentProfile = ProfileStore.CheckName(CurrentProfile) == null ? ProfileStore.Clean(CurrentProfile) : "";
        NotifySound = (NotifySound ?? "").Trim();
        if (NotifySound.Length > 260) NotifySound = "";
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
        if (IsMirror)
        {
            // ミラーは配信ソフトの受け口もパスワードも使わない（視聴者は元と同じパスワードで復号する）
            var up = MirrorUrl.ToWebSocketUri(UpstreamUrl);
            if (up == null) return Mk("gui.err.upstream", "UpstreamUrl");
            if (MirrorUrl.IsSelf(up, ViewPort)) return Mk("gui.err.upstreamself", "UpstreamUrl");
        }
        else
        {
            if (SourcePort == ViewPort) return Mk("gui.err.sameport", "SourcePort,ViewPort");
            if (ViewPassword.Length is > 0 and < MinViewPasswordLength) return Mk("gui.err.shortpass", "ViewPassword", MinViewPasswordLength);
            if (!EngineOptions.IsLoopbackBind(SourceBind))
            {
                if (SourceTls == "off") return Mk("gui.err.bindtls", "SourceTls");
                if (SourcePassword.Length is > 0 and < EngineOptions.MinExternalSourcePasswordLength)
                    return Mk("gui.err.bindpass", "SourcePassword", EngineOptions.MinExternalSourcePasswordLength);
            }
        }
        if (NotifySound.Length > 0 && !SoundExtensions.Contains(Path.GetExtension(NotifySound).ToLowerInvariant()))
            return Mk("gui.err.soundext", "NotifySound");
        var rerr = AccessControl.ValidateRules(DenyRuleLines());
        if (rerr != null) return Mk("gui.err.denyrule", "DenyRules", rerr);
        return null;
    }

    public List<string> DenyRuleLines() => (DenyRules ?? "").Replace("\r", "").Split('\n').ToList();

    /// <summary>セキュリティを下げる設定の一覧（Langの gui.sec.* のキー）。変更時は必ず警告する。</summary>
    public List<string> SecurityWarnings()
    {
        var w = new List<string>();
        if (!IsMirror)   // 配信ソフトの受け口・パスワードに関する項目は、ミラーでは使わない
        {
            if (SourceTls == "off") w.Add("gui.sec.tlsoff");
            if (AcceptAnySourceUser) w.Add("gui.sec.anyuser");
            if (IsWeakViewPassword) w.Add("gui.sec.weakview");
            if (SourcePassword.Length is > 0 and < 8) w.Add("gui.sec.weaksrc");
            if (!EngineOptions.IsLoopbackBind(SourceBind)) w.Add("gui.sec.extbind");
        }
        if (MaxConnPerClient == 0) w.Add("gui.sec.noconnlimit");
        if (RateMaxRequests == 0 || RateWindowSec == 0 || RateBanSec == 0) w.Add("gui.sec.norate");
        if (!UseProxyHeader) w.Add("gui.sec.noproxy");
        return w;
    }

    /// <summary>全体の設定として config.json に保存する項目（これ以外は profiles/名前.json に保存）。</summary>
    internal static readonly string[] GlobalKeys =
        { nameof(Language), nameof(Theme), nameof(LogToFile), nameof(MinimizeToTray), nameof(NotifySound), nameof(NotifySoundEnabled), nameof(CurrentProfile) };

    private JsonObject ToNode() => (JsonObject)JsonSerializer.SerializeToNode(this, Json)!;

    /// <summary>全体の設定（言語・テーマ・ログ・トレイ・通知音）を other の値にそろえる。プロファイルを読み込んだとき、全体の設定は今のまま保つために使う。</summary>
    public void CopyGlobalsFrom(AppSettings other)
    {
        Language = other.Language;
        LogToFile = other.LogToFile;
        MinimizeToTray = other.MinimizeToTray;
        NotifySound = other.NotifySound;
        NotifySoundEnabled = other.NotifySoundEnabled;
        Theme = other.Theme;
    }

    /// <summary>プロファイルに保存する内容（全体の設定を除く）。</summary>
    public string ProfileJson()
    {
        var o = ToNode();
        foreach (var k in GlobalKeys) o.Remove(k);
        return o.ToJsonString(Json);
    }

    /// <summary>config.json に保存する内容（全体の設定と現在のプロファイル名だけ）。</summary>
    public string GlobalJson()
    {
        var o = ToNode();
        var g = new JsonObject();
        foreach (var k in GlobalKeys)
            if (o.TryGetPropertyValue(k, out var v)) { o.Remove(k); g[k] = v; }
        return g.ToJsonString(Json);
    }

    private static JsonObject? ReadObject(string file, ref bool corrupted)
    {
        try { return JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new JsonException("not-object"); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            corrupted = true;
            try { File.Copy(file, file + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { /* 退避失敗は無視 */ }
            return null;
        }
    }

    /// <summary>読み込む。config.json（全体の設定＋現在のプロファイル名）と、現在のプロファイルを合わせた設定を返す。
    /// 無ければ既定値。壊れていれば壊れたファイルを退避して既定値（corrupted=true）。</summary>
    public static AppSettings Load(string path, out bool corrupted) => Load(path, out corrupted, out _);

    /// <summary>上と同じ。旧形式（config.json に全項目がある）なら「既定」プロファイルへ移して書き直し、その名前を migratedTo に返す。</summary>
    public static AppSettings Load(string path, out bool corrupted, out string? migratedTo)
    {
        corrupted = false;
        migratedTo = null;
        if (!File.Exists(path)) return new AppSettings();
        var root = ReadObject(path, ref corrupted);
        if (root == null) return new AppSettings();

        if (root["CurrentProfile"] is JsonValue cv && cv.TryGetValue<string>(out var cur) && ProfileStore.CheckName(cur) == null)
        {
            cur = ProfileStore.Clean(cur);
            var merged = new JsonObject();
            var pf = ProfileStore.FileOf(path, cur);
            if (File.Exists(pf) && ReadObject(pf, ref corrupted) is { } po) merged = po;
            foreach (var kv in root) merged[kv.Key] = kv.Value?.DeepClone();   // 全体の設定を優先
            var s = merged.Deserialize<AppSettings>(Json) ?? new AppSettings();
            s.CurrentProfile = cur;
            s.Normalize();
            return s;
        }

        // 旧形式: 全項目が config.json にある → 「既定」プロファイルへ移す（移行前の config.json は backup へ退避）
        var legacy = root.Deserialize<AppSettings>(Json) ?? new AppSettings();
        legacy.Normalize();
        legacy.CurrentProfile = ProfileStore.UniqueName(path, ProfileStore.DefaultName);
        try
        {
            var bdir = BackupDir(path);
            Directory.CreateDirectory(bdir);
            File.Copy(path, Path.Combine(bdir, "config-premigrate-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"), true);
            legacy.Save(path);
            migratedTo = legacy.CurrentProfile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 書けなくても設定は使える。次の保存で移る */ }
        return legacy;
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

    /// <summary>保存。現在のプロファイル（profiles/名前.json。名前が無ければ「既定」）へ中継・鏡の設定を、config.json へ全体の設定と現在のプロファイル名を書く。
    /// 既存と内容が違うときはそれぞれ backup へ複製（直近 BackupKeep 件）。原子的に書き込み。</summary>
    public void Save(string path)
    {
        Normalize();
        CurrentProfile = ProfileStore.CheckName(CurrentProfile) == null ? ProfileStore.Clean(CurrentProfile) : ProfileStore.DefaultName;
        WriteWithBackup(ProfileStore.FileOf(path, CurrentProfile), ProfileJson(), ProfileStore.BackupDirOf(path, CurrentProfile), "profile-");
        WriteWithBackup(path, GlobalJson(), BackupDir(path), "config-");
    }

    /// <summary>target へ原子的に書く。既存と内容が違えば、古い内容を bdir に prefix+時刻.json で残す（直近 BackupKeep 件）。</summary>
    internal static void WriteWithBackup(string target, string json, string bdir, string prefix)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        if (File.Exists(target))
        {
            string old = "";
            try { old = File.ReadAllText(target); } catch (IOException) { }
            if (old != json && old.Length > 0)
            {
                Directory.CreateDirectory(bdir);
                var at = DateTime.Now;
                string dest;
                do { dest = Path.Combine(bdir, prefix + at.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"); at = at.AddTicks(1); }
                while (File.Exists(dest));
                File.WriteAllText(dest, old);
                foreach (var f in Directory.GetFiles(bdir, prefix + "*.json").OrderByDescending(x => x).Skip(BackupKeep))
                    try { File.Delete(f); } catch (IOException) { }
            }
        }
        var tmp = target + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, target, true);
    }

    public static string BackupDir(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "backup");

    /// <summary>指定プロファイルの最新のバックアップの内容を読み出す（無ければ null）。ファイルは書き換えない。全体の設定は含まない。</summary>
    public static AppSettings? LoadLatestBackup(string path, string profileName)
    {
        if (ProfileStore.CheckName(profileName) != null) return null;
        var bdir = ProfileStore.BackupDirOf(path, ProfileStore.Clean(profileName));
        if (!Directory.Exists(bdir)) return null;
        foreach (var f in Directory.GetFiles(bdir, "profile-*.json").OrderByDescending(x => x))
        {
            var s = TryRead(f);
            if (s != null) return s;
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
            UpstreamUrl = IsMirror ? UpstreamUrl : "",
            MirrorNumber = MirrorNumber,
            SourcePassword = IsMirror ? "" : SourcePassword.Length > 0 ? SourcePassword : PassphraseGenerator.Generate(4, 4),
            ViewPassword = IsMirror ? "" : ViewPassword.Length > 0 ? ViewPassword : PassphraseGenerator.Generate(),
        };
    }
}
