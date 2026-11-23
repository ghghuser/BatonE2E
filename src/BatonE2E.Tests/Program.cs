using BatonE2E.Tests;

// 自己テスト（外部ライブラリなし）。失敗があれば終了コード1。
// 使い方: BatonE2E.Tests [--ini <ja.iniのパス>] [--emit-vectors <出力先>]
string? Opt(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

var ini = Opt("--ini") ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BatonE2E", "Lang", "ja.ini");
CoreTests.Run(ini);
ConfigTests.Run();
ProfileTests.Run();
NotifyTests.Run();
BandwidthTests.Run();
CryptoTests.Run();
await IntegrationTests.RunAsync();
await TlsTests.RunAsync();
await AccessTests.RunAsync();
await MirrorTests.RunAsync();

var vec = Opt("--emit-vectors");
if (vec != null) Vectors.Emit(vec);

Console.WriteLine(T.Failed == 0 ? "全テスト成功" : $"失敗: {T.Failed}件");
return T.Failed == 0 ? 0 : 1;
