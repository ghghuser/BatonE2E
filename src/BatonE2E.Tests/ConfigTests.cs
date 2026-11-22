using BatonE2E.Core.Config;

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
            T.Check("設定: 受け口と視聴のポート重複を検出", new AppSettings { SourcePort = 8000, ViewPort = 8000 }.Check() != null);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
