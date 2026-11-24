using System.Net.WebSockets;
using System.Text;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Logging;

namespace BatonE2E.Core.Engine;

/// <summary>ミラーの上流側。元のBatonE2Eの視聴用WebSocketへ「ふつうの視聴者」として接続し、
/// 受け取ったヘッダー・確認ブロック・暗号フレームを、中身を見ずにそのままハブへ渡す（パスワード・鍵は使わない）。
/// 切れたら間隔を倍々にして（最大60秒）再接続し続ける。</summary>
public sealed class UpstreamClient : IAsyncDisposable
{
    private const int MaxMessage = CryptoConstants.MaxPayload + 256;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private readonly Uri _uri;
    private readonly StreamHub _hub;
    private readonly EngineStats _stats;
    private readonly TimeSpan _initialDelay;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public UpstreamClient(Uri uri, StreamHub hub, EngineStats stats, TimeSpan? initialDelay = null)
    {
        _uri = uri; _hub = hub; _stats = stats;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(2);
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token), _cts.Token);

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = _initialDelay;
        string? lastLogged = null;
        while (!ct.IsCancellationRequested)
        {
            _stats.Upstream = UpstreamStatus.Connecting;
            var gotData = false;
            string reason;
            try
            {
                AppLog.Info(Loc.Get("log.mirror.connecting", _uri.GetLeftPart(UriPartial.Path)));
                gotData = await ConnectAndReceiveAsync(ct);
                reason = Loc.Get("log.mirror.closed");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // 通信まわりの失敗は種類が多く（名前解決・TLS・HTTP応答・切断・不正データ）、どれも「待って再接続」で扱う
                reason = ex.InnerException?.Message is { Length: > 0 } inner ? ex.Message + " / " + inner : ex.Message;
            }
            if (gotData) delay = _initialDelay;
            _stats.Upstream = UpstreamStatus.Waiting;
            _stats.UpstreamError = reason;
            if (reason != lastLogged || gotData) AppLog.Warn(Loc.Get("log.mirror.lost", reason, (int)Math.Ceiling(delay.TotalSeconds)));
            lastLogged = reason;
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
            if (!gotData) delay = delay * 2 > MaxDelay ? MaxDelay : delay * 2;
        }
        _stats.Upstream = UpstreamStatus.None;
    }

    /// <summary>1回分の接続。フレームを1つでも渡せたら true（再接続の間隔を戻す目安）。正常に閉じられたら例外なしで戻る。</summary>
    private async Task<bool> ConnectAndReceiveAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        ws.Options.SetRequestHeader("User-Agent", "BatonE2E-Mirror");
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));
            try { await ws.ConnectAsync(_uri, connectCts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("connect-timeout"); }
        }

        var buf = new byte[16384];
        using var ms = new MemoryStream();
        string? header = null;
        var haveConfirm = false;
        var gotFrame = false;
        while (ws.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) return gotFrame;
                await ms.WriteAsync(buf.AsMemory(0, r.Count), ct);
                if (ms.Length > MaxMessage) throw new E2eCryptoException("upstream-message-too-large");
            } while (!r.EndOfMessage);

            var bytes = ms.ToArray();
            if (r.MessageType == WebSocketMessageType.Text)
            {
                // 元が新しいヘッダーを送り直した。次の確認ブロックまで受け付けない
                if (bytes.Length > 4096) throw new E2eCryptoException("bad-upstream-header");
                header = Encoding.UTF8.GetString(bytes);
                haveConfirm = false;
            }
            else if (header == null) throw new E2eCryptoException("upstream-binary-before-header");
            else if (!haveConfirm)
            {
                _hub.SetUpstream(header, bytes);
                haveConfirm = true;
                _stats.Upstream = UpstreamStatus.Connected;
                _stats.UpstreamError = "";
                AppLog.Info(Loc.Get("log.mirror.connected"));
            }
            else
            {
                _hub.PublishRaw(bytes);
                _stats.AddBytesIn(bytes.Length);
                gotFrame = true;
            }
        }
        return gotFrame;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_loop != null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* 終了時は待ち切らない */ }
        }
        _cts.Dispose();
    }
}
