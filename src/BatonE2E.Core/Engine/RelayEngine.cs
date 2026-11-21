using BatonE2E.Core.Crypto;

namespace BatonE2E.Core.Engine;

/// <summary>配信エンジン本体。UI（コンソール・GUI）はこのクラスを使うだけ。</summary>
public sealed class RelayEngine : IAsyncDisposable
{
    private readonly EngineOptions _o;
    private FrameEncryptor? _enc;
    private FrameDecryptor? _monitor;
    private SourceListener? _source;
    private ViewerServer? _viewer;

    public EngineStats Stats { get; } = new();
    public StreamHub? Hub { get; private set; }
    public EngineOptions Options => _o;

    public RelayEngine(EngineOptions o) { _o = o; }

    public async Task StartAsync()
    {
        _o.Validate();
        try
        {
            _enc = FrameEncryptor.Create(_o.ViewPassword, _o.Iterations);
            _monitor = FrameDecryptor.Create(_o.ViewPassword, _enc.Header);
            Hub = new StreamHub(_enc, _monitor, _o.MaxViewers, Stats);
            _source = new SourceListener(_o, Hub, Stats);
            _source.Start();
            _viewer = await ViewerServer.StartAsync(_o, Hub);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_viewer != null) { await _viewer.DisposeAsync(); _viewer = null; }
        if (_source != null) { await _source.DisposeAsync(); _source = null; }
        _monitor?.Dispose(); _monitor = null;
        _enc?.Dispose(); _enc = null;
    }
}
