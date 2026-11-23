namespace BatonE2E.Core.Engine;

public enum UpstreamStatus { None, Connecting, Connected, Waiting }

/// <summary>画面・コンソール表示用の統計。暗号化が正しく行われているかの自己検査結果も含む。</summary>
public sealed class EngineStats
{
    private long _bytesIn, _bytesOut, _frames, _checkOk, _checkNg, _totalViewers;
    private int _peakViewers;

    public long BytesIn => Interlocked.Read(ref _bytesIn);
    /// <summary>視聴者へ送った合計バイト数（全視聴者の合計）。</summary>
    public long BytesOut => Interlocked.Read(ref _bytesOut);
    /// <summary>この中継を開始してからの、同時接続数の最大。</summary>
    public int PeakViewers => Volatile.Read(ref _peakViewers);
    /// <summary>この中継を開始してからの、延べ接続数（同じ人の再接続も1回と数える）。</summary>
    public long TotalViewers => Interlocked.Read(ref _totalViewers);
    public long Frames => Interlocked.Read(ref _frames);
    public long CheckOk => Interlocked.Read(ref _checkOk);
    public long CheckNg => Interlocked.Read(ref _checkNg);
    private volatile bool _sourceActive;
    private volatile bool _mirror;
    private long _lastFrameTicks;

    /// <summary>ミラー（暗号文のまま中継。このPCは復号できず、自己検査もできない）として動いているか。</summary>
    public bool Mirror { get => _mirror; internal set => _mirror = value; }

    /// <summary>入力あり。ミラーでは「直近5秒以内に上流からフレームが届いた」こと。</summary>
    public bool SourceActive
    {
        get => _mirror
            ? (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastFrameTicks)) is >= 0 and < 5 * TimeSpan.TicksPerSecond
            : _sourceActive;
        set => _sourceActive = value;
    }

    /// <summary>ミラーの上流との接続の状態。</summary>
    public volatile UpstreamStatus Upstream;
    /// <summary>上流に接続できない・切れた理由（接続中は空）。</summary>
    public volatile string UpstreamError = "";
    internal void TouchFrame() => Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
    public volatile string SourceMime = "";

    internal void AddBytesIn(int n) => Interlocked.Add(ref _bytesIn, n);
    internal void AddBytesOut(int n) => Interlocked.Add(ref _bytesOut, n);

    /// <summary>視聴者の接続が確立したとき（引数は接続元）。別スレッドから呼ばれる。パスワードの正否はサーバーには分からない。</summary>
    public event Action<string>? ViewerJoined;
    internal void RaiseViewerJoined(string remote, int current)
    {
        Interlocked.Increment(ref _totalViewers);
        int p;
        while (current > (p = Volatile.Read(ref _peakViewers)) && Interlocked.CompareExchange(ref _peakViewers, current, p) != p) { }
        try { ViewerJoined?.Invoke(remote); }
        catch (Exception) { /* 通知側の失敗で配信を止めない */ }
    }

    internal void AddFrame() => Interlocked.Increment(ref _frames);
    internal void AddCheck(bool ok)
    {
        if (ok) Interlocked.Increment(ref _checkOk);
        else Interlocked.Increment(ref _checkNg);
    }
}
