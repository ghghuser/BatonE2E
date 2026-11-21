namespace BatonE2E.Core.Engine;

/// <summary>画面・コンソール表示用の統計。暗号化が正しく行われているかの自己検査結果も含む。</summary>
public sealed class EngineStats
{
    private long _bytesIn, _frames, _checkOk, _checkNg;

    public long BytesIn => Interlocked.Read(ref _bytesIn);
    public long Frames => Interlocked.Read(ref _frames);
    public long CheckOk => Interlocked.Read(ref _checkOk);
    public long CheckNg => Interlocked.Read(ref _checkNg);
    public volatile bool SourceActive;
    public volatile string SourceMime = "";

    internal void AddBytesIn(int n) => Interlocked.Add(ref _bytesIn, n);
    internal void AddFrame() => Interlocked.Increment(ref _frames);
    internal void AddCheck(bool ok)
    {
        if (ok) Interlocked.Increment(ref _checkOk);
        else Interlocked.Increment(ref _checkNg);
    }
}
