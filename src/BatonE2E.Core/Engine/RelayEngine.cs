using System.Security.Cryptography.X509Certificates;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Logging;
using BatonE2E.Core.Security;

namespace BatonE2E.Core.Engine;

/// <summary>配信エンジン本体。UI（コンソール・GUI）はこのクラスを使うだけ。</summary>
public sealed class RelayEngine : IAsyncDisposable
{
    private readonly EngineOptions _o;
    private FrameEncryptor? _enc;
    private FrameDecryptor? _monitor;
    private SourceListener? _source;
    private ViewerServer? _viewer;
    private UpstreamClient? _upstream;
    private X509Certificate2? _cert;

    public EngineStats Stats { get; } = new();
    public StreamHub? Hub { get; private set; }
    public EngineOptions Options => _o;

    /// <summary>source受け口のTLS証明書の指紋（TLS無効なら null）。</summary>
    public string? SourceCertFingerprint { get; private set; }
    public string? SourceCertFingerprintColon { get; private set; }
    public string? SourceCertDir { get; private set; }
    /// <summary>既存の証明書を作り直した場合 true（oddcast側の保存済み指紋の削除が必要）。</summary>
    public bool SourceCertRegenerated { get; private set; }

    public RelayEngine(EngineOptions o) { _o = o; }

    public async Task StartAsync()
    {
        _o.Validate();
        try
        {
            if (_o.IsMirror)
            {
                // ミラー: 鍵を持たず、暗号文のまま中継する。視聴者は元と同じパスワードで復号する
                Hub = StreamHub.CreateMirror(_o.MaxViewers, Stats);
                _viewer = await ViewerServer.StartAsync(_o, Hub);
                var uri = MirrorUrl.ToWebSocketUri(_o.UpstreamUrl)!;
                _upstream = new UpstreamClient(uri, Hub, Stats, TimeSpan.FromMilliseconds(Math.Clamp(_o.UpstreamRetryMs, 50, 60_000)));
                _upstream.Start();
                return;
            }
            _enc = FrameEncryptor.Create(_o.ViewPassword, _o.Iterations);
            _monitor = FrameDecryptor.Create(_o.ViewPassword, _enc.Header);
            Hub = new StreamHub(_enc, _monitor, _o.MaxViewers, Stats);
            if (_o.SourceTls != SourceTlsMode.Off) LoadCertificate();
            _source = new SourceListener(_o, Hub, Stats, _cert);
            _source.Start();
            _viewer = await ViewerServer.StartAsync(_o, Hub);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private void LoadCertificate()
    {
        SourceCertDir = _o.CertDir ?? Path.Combine(AppContext.BaseDirectory, "tls");
        _cert = SourceCertificate.LoadOrCreate(SourceCertDir, _o.RegenCert, out var ev);
        SourceCertFingerprint = SourceCertificate.Fingerprint(_cert);
        SourceCertFingerprintColon = SourceCertificate.FingerprintColon(_cert);
        SourceCertRegenerated = ev is CertEvent.RegeneratedByRequest or CertEvent.RegeneratedExpired;
        switch (ev)
        {
            case CertEvent.Created: AppLog.Info(Loc.Get("log.tls.cert.created", SourceCertDir)); break;
            case CertEvent.Loaded: AppLog.Info(Loc.Get("log.tls.cert.loaded", _cert.NotAfter.ToString("yyyy-MM-dd"))); break;
            case CertEvent.RegeneratedExpired: AppLog.Warn(Loc.Get("log.tls.expired")); AppLog.Warn(Loc.Get("log.tls.regen")); break;
            default: AppLog.Warn(Loc.Get("log.tls.regen")); break;
        }
        AppLog.Info(Loc.Get("log.tls.fingerprint", SourceCertFingerprintColon));
    }

    public async ValueTask DisposeAsync()
    {
        Hub?.KickAll("stop");
        if (_upstream != null) { await _upstream.DisposeAsync(); _upstream = null; }
        if (_viewer != null) { await _viewer.DisposeAsync(); _viewer = null; }
        if (_source != null) { await _source.DisposeAsync(); _source = null; }
        _cert?.Dispose(); _cert = null;
        _monitor?.Dispose(); _monitor = null;
        _enc?.Dispose(); _enc = null;
    }
}
