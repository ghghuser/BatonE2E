namespace BatonE2E.Core.Engine;

/// <summary>入力量・出力量の累計バイト数から、直近の転送速度（kbps）の推移を作る（グラフ用）。</summary>
public sealed class BandwidthMeter
{
    /// <summary>保持する点の数（1秒ごとなら直近2分）。</summary>
    public const int Capacity = 120;

    private readonly List<double> _in = new(), _out = new();
    private long _lastIn, _lastOut;
    private DateTime? _lastAt;

    public IReadOnlyList<double> In => _in;
    public IReadOnlyList<double> Out => _out;
    public double LastIn => _in.Count > 0 ? _in[^1] : 0;
    public double LastOut => _out.Count > 0 ? _out[^1] : 0;

    public void Reset()
    {
        _in.Clear(); _out.Clear(); _lastAt = null; _lastIn = _lastOut = 0;
    }

    /// <summary>累計値を記録する。最初の1回は基準になるだけで、点は増えない。</summary>
    public void Sample(long bytesIn, long bytesOut, DateTime now)
    {
        if (_lastAt == null) { _lastIn = bytesIn; _lastOut = bytesOut; _lastAt = now; return; }
        var dt = (now - _lastAt.Value).TotalSeconds;
        if (dt < 0.05) return;
        _in.Add(Math.Max(0, bytesIn - _lastIn) * 8 / 1000.0 / dt);
        _out.Add(Math.Max(0, bytesOut - _lastOut) * 8 / 1000.0 / dt);
        _lastIn = bytesIn; _lastOut = bytesOut; _lastAt = now;
        if (_in.Count > Capacity) { _in.RemoveAt(0); _out.RemoveAt(0); }
    }

    /// <summary>縦軸の上限。1・2・5×10のべきの区切りのよい値（最小10）。</summary>
    public static double NiceMax(double v)
    {
        if (double.IsNaN(v) || v <= 10) return 10;
        var p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 }) if (v <= m * p) return m * p;
        return v;
    }
}
