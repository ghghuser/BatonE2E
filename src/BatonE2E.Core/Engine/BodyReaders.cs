using System.Globalization;

namespace BatonE2E.Core.Engine;

internal interface IBodyReader
{
    ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken ct);
}

/// <summary>接続が閉じるまで続く生の本文。</summary>
internal sealed class RawBody : IBodyReader
{
    private readonly BufferedSource _src;
    public RawBody(BufferedSource src) { _src = src; }
    public ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken ct) => _src.ReadAsync(dst, ct);
}

/// <summary>Transfer-Encoding: chunked の本文。</summary>
internal sealed class ChunkedBody : IBodyReader
{
    private readonly BufferedSource _src;
    private int _remaining;
    private bool _needCrlf, _done;

    public ChunkedBody(BufferedSource src) { _src = src; }

    public async ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken ct)
    {
        while (true)
        {
            if (_remaining == 0)
            {
                if (_done) return 0;
                if (_needCrlf)
                {
                    await _src.ReadLineAsync(16, ct);   // チャンク末尾のCRLF
                    _needCrlf = false;
                }
                var line = await _src.ReadLineAsync(64, ct);
                if (line == null) return 0;
                var semi = line.IndexOf(';');
                var hex = (semi >= 0 ? line[..semi] : line).Trim();
                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
                    throw new InvalidDataException("bad-chunk-size");
                if (size == 0)
                {
                    _done = true;
                    while (!string.IsNullOrEmpty(await _src.ReadLineAsync(1024, ct))) { /* トレーラー読み捨て */ }
                    return 0;
                }
                _remaining = size;
                _needCrlf = true;
            }
            var n = await _src.ReadAsync(dst[..Math.Min(dst.Length, _remaining)], ct);
            if (n == 0) return 0;
            _remaining -= n;
            return n;
        }
    }
}
