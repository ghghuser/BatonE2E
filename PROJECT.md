# PROJECT.md（仕様の正）

## 目的
匿名掲示板にURLを貼り、パスワードを知る人だけがブラウザで聴ける、E2E暗号化の音声ライブ配信リレー。
**最優先事項: 安全性（暗号は標準AES-GCM/PBKDF2のみ、独自設計なし）。** 迷ったらこれを基準にする。

## 技術スタック（固定）
C# / .NET 8 / コンソールアプリ / win-x64 単一ファイル・自己完結。外部ライブラリは増やさない。
Core（UI・OS非依存の純ロジック）とアプリ本体を分離。コードは全て `src/`。

## 版数
ビルド番号 N → `1.0.(N÷100の商).(余り)`（Directory.Build.props）。手書き変更なし。

## 段階（詳細仕様は docs/BatonE2E.txt の依頼書）
- P0 骨組み・CI・ビルド ← 現在
- P1 暗号モジュールと相互運用テスト（C#→Node WebCrypto）
- P2 source受け口＋WebSocket配信＋視聴ページ
- P3 結合テスト・再生テスト
- P4 config自動生成・README・Releases
- P5 復号ヘルパー / P5.5 入力互換 / P6 GUI

## AIが確認できること / 人間にしか確認できないこと
- AI: コードの作成、push、Actionsの結果（ログの error 行）。この環境にdotnetはなく、ビルドはActionsのみ。
- 人間: exeの実機動作、oddcast接続、Tailscale Funnel経由の再生。

## 想定される問題と対策
- ビルド環境: ローカルにdotnetなし → Actionsで検証。失敗時はログの error 行を確認。
- Actionの一部がSHA未固定（取得手段がなかった）→ ci.yml の TODO を後で固定。
- Sonarワークフローは既存。slnを src/ に置いたため `dotnet build src/BatonE2E.sln` に変更済み。
- 成果物はフォルダのままupload-artifactへ渡す（ZIPの二重化防止）。直近2件のみ保持。
- 文言は `src/BatonE2E/Lang/ja.ini`。コード中に日本語文言を直書きしない（P0の自己テスト用ログを除く）。

## 現在の状態
P0: 骨組みと CI を作成。Actions の結果待ち。

## バックログ
- アイコン（assets/BatonE2E.ico）／英語 Lang/en.ini／設定のバックアップ機構／ダークテーマ（GUI段階）
