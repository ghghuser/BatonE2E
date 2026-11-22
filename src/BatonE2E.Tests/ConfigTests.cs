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
            T.Check("設定: 日本語がエスケープされず保存", File.ReadAllText(p).Contains("日本語の題名"));

            r.ViewPort = 9090; r.Save(p);
            r.ViewPort = 9091; r.Save(p);
            T.Check("設定: 変更のたびにバックアップが作られる", Directory.GetFiles(AppSettings.BackupDir(p), "config-*.json").Length == 2);
            r.Save(p);
            T.Check("設定: 内容が同じならバックアップを増やさない", Directory.GetFiles(AppSettings.BackupDir(p), "config-*.json").Length == 2);
            var b = AppSettings.LoadLatestBackup(p);
            T.Check("設定: 最新のバックアップを読み出せる", b != null && b.ViewPort == 9090);

            for (var i = 0; i < 14; i++) { r.ViewPort = 10000 + i; r.Save(p); }
            T.Check("設定: バックアップは直近10件まで", Directory.GetFiles(AppSettings.BackupDir(p), "config-*.json").Length == AppSettings.BackupKeep);

            File.WriteAllText(p, "{ これはJSONではない");
            var c = AppSettings.Load(p, out bad);
            T.Check("設定: 壊れていれば既定値で起動し退避する", bad && c.SourcePort == 8000 && Directory.GetFiles(dir, "config.json.broken-*").Length == 1);

            var o = new AppSettings().ToEngineOptions();
            T.Check("設定: パスワード空なら自動生成", o.SourcePassword.Length >= 16 && o.ViewPassword.Length >= 20);
            var o2 = new AppSettings { ViewPassword = "固定" }.ToEngineOptions();
            T.Check("設定: パスワード指定ならそのまま使う", o2.ViewPassword == "固定");
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
}
