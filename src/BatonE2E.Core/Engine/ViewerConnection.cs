using System.Threading.Channels;

namespace BatonE2E.Core.Engine;

/// <summary>視聴者1人分。暗号化済みフレームの有限キューを持ち、溢れたら切断する。</summary>
public sealed class ViewerConnection
{
    private readonly Channel<byte[]> _q;
    private volatile bool _kicked;

    public string HeaderJson { get; }
    public byte[] Confirm { get; }
    public string Remote { get; }
    public string? KickReason { get; private set; }
    public bool Kicked => _kicked;
    public ChannelReader<byte[]> Reader => _q.Reader;

    internal ViewerConnection(string headerJson, byte[] confirm, string remote, int queueSize)
    {
        HeaderJson = headerJson; Confirm = confirm; Remote = remote;
        _q = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(queueSize)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false,
        });
    }

    internal bool TryEnqueue(byte[] frame) => !_kicked && _q.Writer.TryWrite(frame);

    public void Kick(string reason)
    {
        KickReason = reason; _kicked = true;
        _q.Writer.TryComplete();
    }
}
