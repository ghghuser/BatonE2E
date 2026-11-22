using BatonE2E.Core.Engine;

namespace BatonE2E.Tests;

internal static class BandwidthTests
{
    public static void Run()
    {
        var m = new BandwidthMeter();
        var t = new DateTime(2026, 1, 1);
        m.Sample(0, 0, t);
        T.Check("帯域: 最初の1回は基準で、点は増えない", m.In.Count == 0);
        m.Sample(16_000, 32_000, t.AddSeconds(1));
        T.Check("帯域: 1秒で16000バイトは128kbps、出力32000バイトは256kbps", Math.Abs(m.LastIn - 128) < 1e-9 && Math.Abs(m.LastOut - 256) < 1e-9);
        m.Sample(16_000, 32_000, t.AddSeconds(3));
        T.Check("帯域: 増えなければ0kbps（経過時間で割る）", m.LastIn == 0 && m.LastOut == 0 && m.In.Count == 2);
        m.Sample(10, 10, t.AddSeconds(4));
        T.Check("帯域: 累計が減っても負にならない", m.LastIn == 0 && m.LastOut == 0);
        for (var i = 0; i < 300; i++) m.Sample(1000L * (i + 100), 0, t.AddSeconds(10 + i));
        T.Check("帯域: 保持は直近の120点まで", m.In.Count == BandwidthMeter.Capacity && m.Out.Count == BandwidthMeter.Capacity);
        m.Reset();
        T.Check("帯域: リセットで空に戻る", m.In.Count == 0 && m.LastIn == 0);
        T.Check("帯域: 縦軸の上限は区切りのよい値", BandwidthMeter.NiceMax(0) == 10 && BandwidthMeter.NiceMax(128) == 200
            && BandwidthMeter.NiceMax(300) == 500 && BandwidthMeter.NiceMax(501) == 1000 && BandwidthMeter.NiceMax(double.NaN) == 10);
    }
}
