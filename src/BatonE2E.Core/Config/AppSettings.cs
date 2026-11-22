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
    public int ViewPort { get; set; } = 8080;
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

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public const int BackupKeep = 10;

    /// <summary>範囲外・不正な値を安全な値へ直す。</summary>
    public void Normalize()
    {
        if (Language != "ja" && Language != "en") Language = "ja";
        if (SourcePort is < 1 or > 65535) SourcePort = 8000;
        if (ViewPort is < 1 or > 65535) ViewPort = 8080;
        var m = (Mount ?? "").Trim();
        if (m.Length == 0) m = "/stream";
        if (m[0] != '/') m = "/" + m;
        if (m.Any(c => c <= ' ' || c == '?' || c == '#')) m = "/stream";
        Mount = m;
        MaxViewers = Math.Clamp(MaxViewers, 1, 1000);
        var t = (SourceTls ?? "").ToLowerInvariant();
        SourceTls = t is "auto" or "off" or "on" ? t : "auto";
        if (string.IsNullOrWhiteSpace(PageTitle)) PageTitle = "ライブ配信";
        SourcePassword ??= "";
        ViewPassword ??= "";
    }

    /// <summary>設定の問題点（空なら問題なし）。キーは Lang の gui.err.* 。</summary>
    public string? Check()
    {
        if (SourcePort == ViewPort) return "gui.err.sameport";
        return null;
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
            ViewPort = ViewPort,
            Mount = Mount,
            MaxViewers = MaxViewers,
            PageTitle = PageTitle,
            AcceptAnySourceUser = AcceptAnySourceUser,
            SourceTls = Enum.Parse<SourceTlsMode>(SourceTls, true),
            SourcePassword = SourcePassword.Length > 0 ? SourcePassword : PassphraseGenerator.Generate(4, 4),
            ViewPassword = ViewPassword.Length > 0 ? ViewPassword : PassphraseGenerator.Generate(),
        };
    }
}
