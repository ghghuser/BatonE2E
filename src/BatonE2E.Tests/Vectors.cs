using System.Text;
using System.Text.Json;
using BatonE2E.Core.Crypto;

namespace BatonE2E.Tests;

/// <summary>C#で暗号化したデータをNode.js(WebCrypto)で検証するための試験データを出力する。</summary>
internal static class Vectors
{
    public static void Emit(string path)
    {
        const string pw = "テスト用パスフレーズ-Interop 123";   // 日本語を含めてNFC/UTF-8の一致も確認
        using var enc = FrameEncryptor.Create(pw, CryptoConstants.DefaultIterations);

        var audio = new byte[8192];
        new Random(42).NextBytes(audio);
        var items = new List<(string type, byte[] plain)>
        {
            ("audio", audio),
            ("audio", new byte[] { 0xFF, 0xFB, 0x90, 0x00 }),
            ("metadata", Encoding.UTF8.GetBytes("{\"title\":\"曲名テスト\"}")),
            ("format", Encoding.UTF8.GetBytes("audio/aac")),
        };
        var frames = new List<object>();
        foreach (var (type, plain) in items)
        {
            var ft = type switch { "audio" => FrameType.Audio, "metadata" => FrameType.Metadata, _ => FrameType.FormatChange };
            frames.Add(new { type = (int)ft, plain = Convert.ToBase64String(plain), frame = Convert.ToBase64String(enc.Encrypt(ft, plain)) });
        }
        using var headerDoc = JsonDocument.Parse(enc.Header.ToJson());
        var doc = new
        {
            password = pw,
            header = headerDoc.RootElement,
            confirm = Convert.ToBase64String(enc.ConfirmationFrame()),
            frames,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc));
        Console.WriteLine($"試験データを出力: {path}");
    }
}
