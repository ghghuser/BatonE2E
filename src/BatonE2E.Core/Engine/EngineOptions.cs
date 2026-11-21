using BatonE2E.Core.Crypto;

namespace BatonE2E.Core.Engine;

public sealed class EngineOptions
{
    // source受け口は常に127.0.0.1のみ（設定不可）
    public int SourcePort { get; set; } = 8000;
    public string ViewBind { get; set; } = "127.0.0.1";
    public int ViewPort { get; set; } = 8080;
    /// <summary>127.0.0.1以外での視聴待ち受けを許可（TLS使用時のみ有効化する想定）。</summary>
    public bool AllowExternalBind { get; set; }
    public string Mount { get; set; } = "/stream";
    public string SourcePassword { get; set; } = "";
    public string ViewPassword { get; set; } = "";
    public int MaxViewers { get; set; } = 20;
    public int ChunkSize { get; set; } = 8192;
    public int Iterations { get; set; } = CryptoConstants.DefaultIterations;
    public int ViewerQueue { get; set; } = 64;
    /// <summary>視聴ページの差し替えファイル。未指定なら exeと同じ場所の viewer.html、なければ埋め込み。</summary>
    public string? ViewerPageFile { get; set; }

    public void Validate()
    {
        if (string.IsNullOrEmpty(SourcePassword)) throw new E2eCryptoException("empty-source-password");
        if (string.IsNullOrEmpty(ViewPassword)) throw new E2eCryptoException("empty-view-password");
        ChunkSize = Math.Clamp(ChunkSize, 1024, 262_144);
        MaxViewers = Math.Clamp(MaxViewers, 1, 1000);
        ViewerQueue = Math.Clamp(ViewerQueue, 8, 4096);
    }
}
