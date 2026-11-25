using System.Text;
using BatonE2E.Core.Config;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Engine;

namespace BatonE2E.Tests;

internal static class ConfigTests
{
    public static void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "batone2e-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "config.json");
        try
        {
            var d = AppSettings.Load(p, out var bad);
            T.Check("設定: 無ければ既定値", d.SourcePort == 8000 && d.Mount == "/stream" && !bad);

            var s = new AppSettings { Mount = "live", SourcePort = 99999, SourceTls = "BOGUS", PageTitle = "日本語の題名", MaxViewers = 0 };
            s.Save(p);
            var r = AppSettings.Load(p, out bad);
            T.Check("設定: 保存して読み戻せる（値は安全な範囲へ補正）",
                r.Mount == "/live" && r.SourcePort == 8000 && r.SourceTls == "auto" && r.PageTitle == "日本語の題名" && r.MaxViewers == 1 && !bad);
            T.Check("設定: 日本語がエスケープされず保存", File.ReadAllText(Path.Combine(dir, "profiles", "既定.json")).Contains("日本語の題名") && !File.ReadAllText(p).Contains("PageTitle"));

            r.ViewPort = 9090; r.Save(p);
            r.ViewPort = 9091; r.Save(p);
            var pbd = Path.Combine(AppSettings.BackupDir(p), "profile-既定");
            T.Check("設定: 変更のたびにプロファイルのバックアップが作られる", Directory.GetFiles(pbd, "profile-*.json").Length == 2);
            r.Save(p);
            T.Check("設定: 内容が同じならバックアップを増やさない", Directory.GetFiles(pbd, "profile-*.json").Length == 2);
            var b = AppSettings.LoadLatestBackup(p, "既定");
            T.Check("設定: 最新のバックアップを読み出せる", b != null && b.ViewPort == 9090);

            for (var i = 0; i < 14; i++) { r.ViewPort = 10000 + i; r.Save(p); }
            T.Check("設定: バックアップは直近10件まで", Directory.GetFiles(pbd, "profile-*.json").Length == AppSettings.BackupKeep);

            File.WriteAllText(p, "{ これはJSONではない");
            var c = AppSettings.Load(p, out bad);
            T.Check("設定: 壊れていれば既定値で起動し退避する", bad && c.SourcePort == 8000 && Directory.GetFiles(dir, "config.json.broken-*").Length == 1);

            RunMigration(dir);

            var o = new AppSettings().ToEngineOptions();
            T.Check("設定: パスワード空なら自動生成", o.SourcePassword.Length >= 16 && o.ViewPassword.Length >= 20);
            var o2 = new AppSettings { ViewPassword = "固定" }.ToEngineOptions();
            T.Check("設定: パスワード指定ならそのまま使う", o2.ViewPassword == "固定");
            var mir = new AppSettings { Mode = "mirror", UpstreamUrl = "https://a.ts.net/live", ViewPort = 9000, SourcePort = 9000, ViewPassword = "x", SourceTls = "off", SourceBind = "0.0.0.0" };
            T.Check("ミラー設定: 配信ソフト側の設定（同じポート・短いパスワード等）は検査しない", mir.Check() == null && mir.SecurityWarnings().Count == 0);
            T.Check("ミラー設定: 上流URLが空・不正なら保存不可", new AppSettings { Mode = "mirror" }.Check()?.Key == "gui.err.upstream"
                && new AppSettings { Mode = "mirror", UpstreamUrl = "ftp://x" }.Check()?.Field == "UpstreamUrl");
            T.Check("ミラー設定: 自分自身を指すURLは保存不可", new AppSettings { Mode = "mirror", UpstreamUrl = "http://127.0.0.1:8080/", ViewPort = 8080 }.Check()?.Key == "gui.err.upstreamself");
            T.Check("テーマ: 既定はauto、不正な値はautoに戻る。light/darkはそのまま", new AppSettings().Theme == "auto" && NormTheme("blue") == "auto" && NormTheme("light") == "light" && NormTheme("dark") == "dark");
            var pq = Path.Combine(dir, "snd.json");
            new AppSettings { NotifySoundEnabled = false, Theme = "dark" }.Save(pq);
            var rq = AppSettings.TryRead(pq);
            T.Check("通知音のオンオフ・テーマ: 既定はオン/auto。保存して読み戻しても保たれる", new AppSettings().NotifySoundEnabled && rq != null && !rq.NotifySoundEnabled && rq.Theme == "dark");
            T.Check("言語: 既定はsystem、不正な値もsystemに戻る。ja/enはそのまま", new AppSettings().Language == "system" && Norm("fr") == "system" && Norm("ja") == "ja" && Norm("en") == "en");
            T.Check("言語: systemはOSの言語が日本語ならja、それ以外はen", BatonE2E.Core.Loc.Resolve("system", "ja") == "ja" && BatonE2E.Core.Loc.Resolve("system", "JA") == "ja"
                && BatonE2E.Core.Loc.Resolve("system", "en") == "en" && BatonE2E.Core.Loc.Resolve("system", "fr") == "en" && BatonE2E.Core.Loc.Resolve("ja", "en") == "ja" && BatonE2E.Core.Loc.Resolve("en", "ja") == "en");
            T.Check("ミラー設定: ミラー番号は1〜99に収まる", Num(0) == 1 && Num(5) == 5 && Num(500) == 99);
            T.Check("ミラー設定: 通常モードではURLが空でも問題なし・不正なモード名は通常に戻る",
                new AppSettings { UpstreamUrl = "" }.Check() == null && new AppSettings { Mode = "???" }.IsMirror == false);
            var mo = mir.ToEngineOptions();
            T.Check("ミラー設定: エンジン設定へ変換するとミラーになり、パスワードは作らない", mo.IsMirror && mo.UpstreamUrl == mir.UpstreamUrl && mo.SourcePassword == "" && mo.ViewPassword == "");
            T.Check("ミラー設定: 通常モードではミラーにならない", !new AppSettings { UpstreamUrl = "https://a.ts.net/x" }.ToEngineOptions().IsMirror);
            T.Check("パスワード: 7文字は拒否・8文字は許可", new AppSettings { ViewPassword = "abcdefg" }.Check() != null && new AppSettings { ViewPassword = "abcdefgh" }.Check() == null);
            T.Check("パスワード: 日本語8文字は許可・12文字未満は警告", new AppSettings { ViewPassword = "ぱすわーどです" + "ね" }.Check() == null && new AppSettings { ViewPassword = "ぱすわーどですね" }.IsWeakViewPassword);
            // 日本語（濁点の合成/分解違い）でも同じ鍵になること（NFC正規化）
            var nfc = "ぱすわーど".Normalize(NormalizationForm.FormC);
            var nfd = "ぱすわーど".Normalize(NormalizationForm.FormD);
            using var jenc = FrameEncryptor.Create(nfc, CryptoConstants.DefaultIterations);
            var jhdr = StreamHeader.Parse(jenc.Header.ToJson())!;
            using var jdec = FrameDecryptor.Create(nfd, jhdr);
            T.Check("パスワード: 日本語（NFC/NFD違い）でも確認ブロックを検証できる", nfc != nfd && jdec.VerifyConfirmation(jenc.ConfirmationFrame()));
            T.Check("視聴パス: 正規化", EngineOptions.NormalizeViewPath("") == "/" && EngineOptions.NormalizeViewPath("live") == "/live"
                && EngineOptions.NormalizeViewPath("/a/b/") == "/a/b" && EngineOptions.NormalizeViewPath("/a b") == "/"
                && EngineOptions.NormalizeViewPath("/a/../b") == "/" && EngineOptions.NormalizeViewPath("/a?x") == "/" && EngineOptions.NormalizeViewPath("/日本") == "/"
                && EngineOptions.NormalizeViewPath("//") == "/" && EngineOptions.NormalizeViewPath("/a//b") == "/");
            T.Check("sourceの待ち受け: 外部アドレスはTLSなし・弱いパスワードを拒否",
                new AppSettings { SourceBind = "0.0.0.0", SourceTls = "off" }.Check() != null
                && new AppSettings { SourceBind = "0.0.0.0", SourcePassword = "short" }.Check() != null
                && new AppSettings { SourceBind = "0.0.0.0", SourcePassword = "long-enough-pass" }.Check() == null
                && new AppSettings { SourceBind = "127.0.0.1", SourceTls = "off", SourcePassword = "a" }.Check() == null);
            var nb = new AppSettings { SourceBind = "不正" }; nb.Normalize();
            T.Check("sourceの待ち受け: 不正な値は127.0.0.1へ戻す", nb.SourceBind == "127.0.0.1");
            var bad1 = new AppSettings { DenyRules = "ip:1.1.1.1\nfoo:bar" }.Check();
            T.Check("設定: 拒否リストの誤りは行番号付きで検出", bad1 != null && bad1.Key == "gui.err.denyrule" && bad1.Field == "DenyRules" && bad1.Args[0].ToString()!.StartsWith("2:"));
            T.Check("設定: 正しい拒否リストは通る", new AppSettings { DenyRules = "# memo\nip:1.1.1.1\nua:bot" }.Check() == null);
            var sw = new AppSettings { SourceTls = "off", AcceptAnySourceUser = true, MaxConnPerClient = 0, RateMaxRequests = 0, UseProxyHeader = false, ViewPassword = "abcdefgh", SourcePassword = "abc" }.SecurityWarnings();
            T.Check("セキュリティ警告: 下がる設定を全て検出", new[] { "gui.sec.tlsoff", "gui.sec.anyuser", "gui.sec.weakview", "gui.sec.weaksrc", "gui.sec.noconnlimit", "gui.sec.norate", "gui.sec.noproxy" }.All(sw.Contains));
            T.Check("セキュリティ警告: 既定の設定では出ない", new AppSettings().SecurityWarnings().Count == 0);
            T.Check("設定: 受け口と視聴のポート重複を検出", new AppSettings { SourcePort = 8000, ViewPort = 8000 }.Check() != null);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static void RunMigration(string dir)
    {
        var d1 = Path.Combine(dir, "mig");
        Directory.CreateDirectory(d1);
        var m = Path.Combine(d1, "config.json");
        File.WriteAllText(m, new AppSettings { SourcePort = 9555, Theme = "dark", PageTitle = "旧" }.ToJson());   // 旧形式（全項目が config.json）
        var mg = AppSettings.Load(m, out var mbad, out var mto);
        T.Check("移行: 旧形式のconfig.jsonは「既定」プロファイルへ移る", mto == "既定" && mg.CurrentProfile == "既定" && mg.SourcePort == 9555 && mg.Theme == "dark" && !mbad);
        var cfgText = File.ReadAllText(m);
        T.Check("移行: config.jsonは全体設定と現在のプロファイル名だけになる", cfgText.Contains("\"CurrentProfile\"") && cfgText.Contains("\"Theme\"") && !cfgText.Contains("SourcePort") && !cfgText.Contains("PageTitle"));
        var prof = Path.Combine(d1, "profiles", "既定.json");
        var profText = File.Exists(prof) ? File.ReadAllText(prof) : "";
        T.Check("移行: プロファイルに中継の設定が入り、全体設定は入らない", profText.Contains("9555") && profText.Contains("旧") && !profText.Contains("Theme") && !profText.Contains("CurrentProfile"));
        T.Check("移行: 移行前のconfig.jsonはbackupへ退避", Directory.GetFiles(AppSettings.BackupDir(m), "config-premigrate-*.json").Length == 1);
        var again = AppSettings.Load(m, out _, out var mto2);
        T.Check("移行: 2回目の起動では移行しない", mto2 == null && again.SourcePort == 9555 && again.CurrentProfile == "既定" && again.Theme == "dark");

        again.CurrentProfile = "夜"; again.SourcePort = 9777; again.Save(m);
        var l2 = AppSettings.Load(m, out _);
        T.Check("プロファイル: 現在のプロファイル名のファイルを読む（他のプロファイルは変わらない）",
            l2.CurrentProfile == "夜" && l2.SourcePort == 9777 && ProfileStore.Load(m, "既定", l2)!.SourcePort == 9555);

        var d2 = Path.Combine(dir, "mig2");
        Directory.CreateDirectory(Path.Combine(d2, "profiles"));
        File.WriteAllText(Path.Combine(d2, "profiles", "既定.json"), new AppSettings { SourcePort = 9001 }.ProfileJson());
        File.WriteAllText(Path.Combine(d2, "config.json"), new AppSettings { SourcePort = 9002 }.ToJson());
        var mg2 = AppSettings.Load(Path.Combine(d2, "config.json"), out _, out var mto3);
        T.Check("移行: 「既定」が既にあれば別名にし、既存のプロファイルは上書きしない",
            mto3 == "既定 (2)" && mg2.SourcePort == 9002 && ProfileStore.Load(Path.Combine(d2, "config.json"), "既定", mg2)!.SourcePort == 9001);

        var d3 = Path.Combine(dir, "mig3");
        Directory.CreateDirectory(d3);
        File.WriteAllText(Path.Combine(d3, "config.json"), "{ \"CurrentProfile\": \"../evil\", \"SourcePort\": 9003 }");
        var mg3 = AppSettings.Load(Path.Combine(d3, "config.json"), out _);
        T.Check("設定: config.jsonの不正なプロファイル名はフォルダ外を読まず「既定」へ", mg3.CurrentProfile == "既定" && !File.Exists(Path.Combine(d3, "evil.json")));
    }

    private static string NormTheme(string t) { var s = new AppSettings { Theme = t }; s.Normalize(); return s.Theme; }

    private static string Norm(string lang) { var s = new AppSettings { Language = lang }; s.Normalize(); return s.Language; }
    private static int Num(int n) { var s = new AppSettings { MirrorNumber = n }; s.Normalize(); return s.MirrorNumber; }
}
