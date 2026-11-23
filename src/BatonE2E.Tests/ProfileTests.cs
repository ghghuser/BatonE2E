using BatonE2E.Core.Config;

namespace BatonE2E.Tests;

internal static class ProfileTests
{
    public static void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "batone2e-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = Path.Combine(dir, "config.json");
        try
        {
            T.Check("プロファイル: 一覧は最初は空", ProfileStore.List(cfg).Count == 0);
            var a = new AppSettings { SourcePort = 9100, ViewPort = 9200, PageTitle = "夜の配信", Language = "en", LogToFile = true, MinimizeToTray = false, NotifySound = "x.wav" };
            ProfileStore.Save(cfg, "  夜の配信用  ", a);
            ProfileStore.Save(cfg, "Home", new AppSettings { SourcePort = 9300 });
            var names = ProfileStore.List(cfg);
            T.Check("プロファイル: 保存した名前が一覧に出る（前後の空白は除く）", names.Count == 2 && names.Contains("夜の配信用") && names.Contains("Home"));
            T.Check("プロファイル: 存在確認", ProfileStore.Exists(cfg, "Home") && !ProfileStore.Exists(cfg, "Nothing"));

            var cur = new AppSettings { Language = "ja", LogToFile = false, MinimizeToTray = true, NotifySound = "keep.mp3", NotifySoundEnabled = false, Theme = "light" };
            var l = ProfileStore.Load(cfg, "夜の配信用", cur);
            T.Check("プロファイル: 内容を読み込める", l != null && l.SourcePort == 9100 && l.ViewPort == 9200 && l.PageTitle == "夜の配信");
            T.Check("プロファイル: 言語・ログ出力・トレイ格納は現在の値のまま", l != null && l.Language == "ja" && !l.LogToFile && l.MinimizeToTray && l.NotifySound == "keep.mp3" && !l.NotifySoundEnabled && l.Theme == "light");
            T.Check("プロファイル: 無い名前は null", ProfileStore.Load(cfg, "Nothing", cur) == null);

            ProfileStore.Save(cfg, "Home", new AppSettings { SourcePort = 9400 });
            T.Check("プロファイル: 同名は上書き（件数は増えない）", ProfileStore.List(cfg).Count == 2 && ProfileStore.Load(cfg, "Home", cur)!.SourcePort == 9400);

            File.WriteAllText(Path.Combine(ProfileStore.Dir(cfg), "Broken.json"), "{ これはJSONではない");
            T.Check("プロファイル: 壊れたファイルは null（ファイルは残る）", ProfileStore.Load(cfg, "Broken", cur) == null && File.Exists(Path.Combine(ProfileStore.Dir(cfg), "Broken.json")));

            T.Check("プロファイル: 削除できる", ProfileStore.Delete(cfg, "Home") && !ProfileStore.Exists(cfg, "Home") && !ProfileStore.Delete(cfg, "Home"));

            string[] bad = { "", "   ", "a/b", "..\\x", "a:b", "con", "NUL.txt", "end.", "x\ty", new string('あ', 41) };
            T.Check("プロファイル: 使えない名前は拒否", bad.All(n => ProfileStore.CheckName(n) != null));
            string[] good = { "夜の配信", "Home 2", "a-b_c", new string('あ', 40) };
            T.Check("プロファイル: 普通の名前は許可", good.All(n => ProfileStore.CheckName(n) == null));
            var threw = false;
            try { ProfileStore.Save(cfg, "../evil", new AppSettings()); } catch (ArgumentException) { threw = true; }
            T.Check("プロファイル: フォルダ外への保存は例外", threw && !File.Exists(Path.Combine(dir, "evil.json")));
            T.Check("プロファイル: 保存フォルダ外へは読み書きしない", ProfileStore.Load(cfg, "../config", cur) == null && !ProfileStore.Delete(cfg, "../config"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
