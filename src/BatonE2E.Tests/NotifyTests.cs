using BatonE2E.Core.Config;
using BatonE2E.Core.Engine;

namespace BatonE2E.Tests;

internal static class NotifyTests
{
    public static void Run()
    {
        var n = new JoinNotifier();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        T.Check("通知音: 最初の接続は鳴らす", n.ShouldNotify("1.1.1.1", t0));
        T.Check("通知音: 全体で3秒以内の接続は鳴らさない", !n.ShouldNotify("2.2.2.2", t0.AddSeconds(2)));
        T.Check("通知音: 3秒たてば別の接続元は鳴らす", n.ShouldNotify("2.2.2.2", t0.AddSeconds(4)));
        T.Check("通知音: 同じ接続元は60秒以内は鳴らさない（再接続の連打対策）", !n.ShouldNotify("1.1.1.1", t0.AddSeconds(30)));
        T.Check("通知音: 同じ接続元も60秒たてば鳴らす", n.ShouldNotify("1.1.1.1", t0.AddSeconds(65)));

        var big = new JoinNotifier();
        var t = t0;
        var okAll = true;
        for (var i = 0; i < 3000; i++) { t = t.AddSeconds(4); okAll &= big.ShouldNotify("host" + i, t); }
        T.Check("通知音: 接続元が大量でも記録が膨らまず動き続ける", okAll);

        T.Check("通知音: 設定の既定は空（鳴らさない）", new AppSettings().NotifySound == "" && new AppSettings().Check() == null);
        T.Check("通知音: wav/mp3/wmaは許可（大文字も可）", new[] { "a.wav", @"C:\x\b.MP3", "c.wma" }.All(f => new AppSettings { NotifySound = f }.Check() == null));
        var bad = new AppSettings { NotifySound = "evil.exe" }.Check();
        T.Check("通知音: 音声以外の拡張子は保存不可", bad != null && bad.Key == "gui.err.soundext" && bad.Field == "NotifySound");
    }
}
