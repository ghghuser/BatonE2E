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
    private readonly FrameEncryptor? _enc;   // ミラー（暗号文のまま中継）のときは null
    private readonly bool _mirror;
    private string? _upHeader;
    private byte[]? _upConfirm;
    private string _upKey = "";
    private long _upSeq = -1;
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

    private StreamHub(int maxViewers, EngineStats stats)
    {
        _mirror = true; _max = maxViewers; _stats = stats;
    }

    /// <summary>ミラー用。上流から受け取ったヘッダー・確認ブロック・暗号フレームをそのまま視聴者へ渡す（鍵は持たない）。</summary>
    public static StreamHub CreateMirror(int maxViewers, EngineStats stats)
    {
        stats.Mirror = true;
        return new StreamHub(maxViewers, stats);
    }

    public EngineStats Stats => _stats;

    /// <summary>視聴者を受け付けられる状態か（ミラーは、上流のヘッダーを受け取るまで受け付けない）。</summary>
    public bool Ready { get { lock (_lock) return !_mirror || _upHeader != null; } }

    public int ViewerCount { get { lock (_lock) return _viewers.Count; } }

    public ViewerConnection? TryAddViewer(int queueSize, string remote)
    {
        lock (_lock)
        {
            if (_viewers.Count >= _max) return null;
            ViewerConnection v;
            if (_mirror)
            {
                if (_upHeader == null || _upConfirm == null) return null;
                v = new ViewerConnection(_upHeader, _upConfirm, remote, queueSize);
            }
            else
            {
                var h = new StreamHeader
                {
                    V = _enc!.Header.V, Kdf = _enc.Header.Kdf, Iter = _enc.Header.Iter,
                    Salt = _enc.Header.Salt, Sid = _enc.Header.Sid, Mime = _mime,
                };
                v = new ViewerConnection(h.ToJson(), _enc.ConfirmationFrame(), remote, queueSize);
            }
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

    /// <summary>指定の接続元の視聴者をすべて切断（連打で拒否した直後など）。</summary>
    public void KickRemote(string remote, string reason)
    {
        lock (_lock)
        {
            foreach (var v in _viewers.Where(x => x.Remote == remote).ToList()) { v.Kick(reason); _viewers.Remove(v); }
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
            var frame = _enc!.Encrypt(type, payload);
            _stats.AddFrame();
            SelfCheck(frame, type, payload);
            Fanout(frame);
        }
    }

    private void Fanout(byte[] frame)
    {
        for (var i = _viewers.Count - 1; i >= 0; i--)
        {
            var v = _viewers[i];
            if (v.TryEnqueue(frame)) continue;
            v.Kick("slow");
            _viewers.RemoveAt(i);
            AppLog.Warn(Loc.Get("log.viewer.slow", v.Remote));
        }
    }

    /// <summary>ミラー用: 上流のヘッダーと確認ブロックを設定する。以前と違う（元が再起動して別の配信になった）場合は、
    /// 視聴者を全員切断する（視聴ページが自動で再接続し、新しいヘッダーで始め直す）。</summary>
    public void SetUpstream(string headerJson, byte[] confirm)
    {
        if (!_mirror) throw new InvalidOperationException("not-mirror");
        var h = StreamHeader.Parse(headerJson) ?? throw new E2eCryptoException("bad-upstream-header");
        h.Validate(CryptoConstants.MinIterations);
        if (confirm.Length < CryptoConstants.SeqLength + 1 + CryptoConstants.TagLength
            || System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(confirm) != 0)
            throw new E2eCryptoException("bad-upstream-confirm");
        var key = $"{h.V}|{h.Kdf}|{h.Iter}|{h.Salt}|{h.Sid}";
        lock (_lock)
        {
            if (_upHeader != null && key != _upKey)
            {
                KickAll("upstream-changed");
                _upSeq = -1;
                AppLog.Warn(Loc.Get("log.mirror.newsession"));
            }
            _upHeader = headerJson; _upConfirm = confirm; _upKey = key;
        }
    }

    /// <summary>ミラー用: 上流から届いた暗号フレームを、中身を見ずにそのまま全視聴者へ渡す。
    /// 連番が途切れた（上流に再接続している間に元が先へ進んだ）ときは、視聴者を全員切断して再接続させる。</summary>
    public void PublishRaw(byte[] frame)
    {
        if (!_mirror) throw new InvalidOperationException("not-mirror");
        if (frame.Length < CryptoConstants.SeqLength + 1 + CryptoConstants.TagLength) throw new E2eCryptoException("bad-upstream-frame");
        var seq = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(frame);
        if (seq < 1) throw new E2eCryptoException("bad-upstream-frame");
        lock (_lock)
        {
            if (_upSeq >= 0 && seq != _upSeq + 1)
            {
                KickAll("gap");
                AppLog.Warn(Loc.Get("log.mirror.gap"));
            }
            _upSeq = seq;
            _stats.AddFrame();
            _stats.TouchFrame();
            Fanout(frame);
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
