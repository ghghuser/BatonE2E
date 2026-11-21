using System.Text;

namespace BatonE2E.Core.Engine;

/// <summary>ストリームの先読み付きリーダー。ヘッダー行の読み取り後、残りのバイトをそのまま本文として渡せる。</summary>
internal sealed class BufferedSource
{
    private readonly Stream _s;
    private readonly byte[] _buf = new byte[8192];
    private int _pos, _len;

    public BufferedSource(Stream s) { _s = s; }

    public async ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken ct)
    {
        if (_pos < _len)
        {
            var n = Math.Min(dst.Length, _len - _pos);
            _buf.AsMemory(_pos, n).CopyTo(dst);
            _pos += n;
            return n;
        }
        return await _s.ReadAsync(dst, ct);
    }

    /// <summary>1行（CRLF/LF）を読む。EOFで未読データがなければ null。長すぎる行は例外。</summary>
    public async Task<string?> ReadLineAsync(int maxLen, CancellationToken ct)
    {
        var line = new List<byte>();
        while (true)
        {
            if (_pos >= _len)
            {
                _len = await _s.ReadAsync(_buf, ct);
                _pos = 0;
                if (_len == 0) return line.Count == 0 ? null : Encoding.Latin1.GetString(line.ToArray());
            }
            var b = _buf[_pos++];
            if (b == (byte)'\n')
            {
                if (line.Count > 0 && line[^1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                return Encoding.Latin1.GetString(line.ToArray());
            }
            line.Add(b);
            if (line.Count > maxLen) throw new InvalidDataException("line-too-long");
        }
    }
}
