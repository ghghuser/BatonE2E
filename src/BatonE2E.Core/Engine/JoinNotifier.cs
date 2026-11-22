namespace BatonE2E.Core.Engine;

/// <summary>視聴者の接続通知（音）を出す頻度の制限。自動再接続や連打で鳴り続けないようにする。
/// 同じ接続元は <see cref="PerClient"/> に1回、全体でも <see cref="Global"/> に1回まで。</summary>
public sealed class JoinNotifier
{
    public static readonly TimeSpan PerClient = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Global = TimeSpan.FromSeconds(3);
    private const int MaxTracked = 1000;

    private readonly Dictionary<string, DateTime> _last = new();
    private DateTime _lastAny = DateTime.MinValue;
    private readonly object _lock = new();

    /// <summary>今、通知してよければ true（その場合は記録も更新する）。</summary>
    public bool ShouldNotify(string remote, DateTime now)
    {
        lock (_lock)
        {
            if (now - _lastAny < Global) return false;
            if (_last.TryGetValue(remote, out var t) && now - t < PerClient) return false;
            if (_last.Count >= MaxTracked)
                foreach (var k in _last.Where(kv => now - kv.Value >= PerClient).Select(kv => kv.Key).ToList()) _last.Remove(k);
            if (_last.Count >= MaxTracked) _last.Clear();
            _last[remote] = now;
            _lastAny = now;
            return true;
        }
    }
}
