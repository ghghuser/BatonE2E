using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BatonE2E.Core.Crypto;
using BatonE2E.Core.Logging;

namespace BatonE2E.Core.Engine;

/// <summary>
/// 入力（source）から受けた平文を暗号化し、全視聴者へ配る。入力の種類は知らない。
/// 暗号化と配布は同じロックの中で行い、連番と配信順を一致させる。
/// </summary>
public sealed class StreamHub
{
    private readonly FrameEncryptor _enc;
    private readonly FrameDecryptor? _monitor;   // 自己検査用（暗号化結果を自分で復号して照合）
    private readonly int _max;
    private readonly EngineStats _stats;
    private readonly object _lock = new();
    private readonly List<ViewerConnection> _viewers = new();
    private string _mime = "audio/mpeg";
    private string? _lastTitle;
    private static readonly JsonSerializerOptions MetaJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public StreamHub(FrameEncryptor enc, FrameDecryptor? monitor, int maxViewers, EngineStats stats)
    {
        _enc = enc; _monitor = monitor; _max = maxViewers; _stats = stats;
    }

    public int ViewerCount { get { lock (_lock) return _viewers.Count; } }

    public ViewerConnection? TryAddViewer(int queueSize, string remote)
    {
        lock (_lock)
        {
            if (_viewers.Count >= _max) return null;
            var h = new StreamHeader
            {
                V = _enc.Header.V, Kdf = _enc.Header.Kdf, Iter = _enc.Header.Iter,
                Salt = _enc.Header.Salt, Sid = _enc.Header.Sid, Mime = _mime,
            };
            var v = new ViewerConnection(h.ToJson(), _enc.ConfirmationFrame(), remote, queueSize);
            _viewers.Add(v);
            return v;
        }
    }

    /// <summary>全視聴者を切断（終了時）。</summary>
    public void KickAll(string reason)
    {
        lock (_lock)
        {
            foreach (var v in _viewers) v.Kick(reason);
            _viewers.Clear();
        }
    }

    public void RemoveViewer(ViewerConnection v)
    {
        lock (_lock) _viewers.Remove(v);
    }

    public void SetMime(string mime)
    {
        lock (_lock)
        {
            if (_mime == mime) return;
            _mime = mime;
            Publish(FrameType.FormatChange, Encoding.UTF8.GetBytes(mime));
        }
    }

    public void PublishMetadata(string title)
    {
        if (title.Length > 500) title = title[..500];
        lock (_lock)
        {
            if (_lastTitle == title) return;
            _lastTitle = title;
            Publish(FrameType.Metadata, JsonSerializer.SerializeToUtf8Bytes(new { title }, MetaJson));
        }
    }

    public void Publish(FrameType type, ReadOnlySpan<byte> payload)
    {
        lock (_lock)
        {
            if (_viewers.Count == 0) return;   // 視聴者なしなら暗号化も省略（連番は飛んでも問題なし）
            var frame = _enc.Encrypt(type, payload);
            _stats.AddFrame();
            SelfCheck(frame, type, payload);
            for (var i = _viewers.Count - 1; i >= 0; i--)
            {
                var v = _viewers[i];
                if (v.TryEnqueue(frame)) continue;
                v.Kick("slow");
                _viewers.RemoveAt(i);
                AppLog.Warn(Loc.Get("log.viewer.slow", v.Remote));
            }
        }
    }

    private void SelfCheck(byte[] frame, FrameType type, ReadOnlySpan<byte> payload)
    {
        if (_monitor == null) return;
        var st = _monitor.TryDecrypt(frame, out var t, out var p);
        var ok = st == DecryptStatus.Ok && t == type && payload.SequenceEqual(p);
        // 平文がそのまま暗号文に出ていないこと（8バイト以上の平文で確認）
        if (ok && payload.Length >= 8)
            ok = !frame.AsSpan(CryptoConstants.SeqLength + 1, payload.Length).SequenceEqual(payload);
        _stats.AddCheck(ok);
        if (!ok) AppLog.Error(Loc.Get("log.check.fail"));
    }
}
