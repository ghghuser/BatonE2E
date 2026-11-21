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
- P0 骨組み・CI・ビルド（完了）
- P1 暗号モジュールと相互運用テスト（C#→Node WebCrypto）← 現在
- P2 source受け口＋WebSocket配信＋視聴ページ
- P3 結合テスト・再生テスト
- P3.5 GUI（WPF。かがみんv2のように操作・管理。エンジンはUIを知らない作りにする）
- P4 config自動生成・README・Releases
- P5 復号ヘルパー / P5.5 入力互換 / P6 視聴者数表示など
- 任意拡張: Argon2id（ヘッダーのkdfで切替。利用者が別途用意する部品の形も可）、iPhone Safari（ManagedMediaSource）対応

## AIが確認できること / 人間にしか確認できないこと
- AI: コードの作成、push、Actionsの結果（ログの error 行）。この環境にdotnetはなく、ビルドはActionsのみ。
- 人間: exeの実機動作、oddcast接続、Tailscale Funnel経由の再生。

## 想定される問題と対策
- ビルド環境: ローカルにdotnetなし → Actionsで検証。失敗時はログの error 行を確認。
- Actionの一部がSHA未固定（取得手段がなかった）→ ci.yml の TODO を後で固定。
- Sonarワークフローは既存。slnを src/ に置いたため `dotnet build src/BatonE2E.sln` に変更済み。
- 成果物はフォルダのままupload-artifactへ渡す（ZIPの二重化防止）。直近2件のみ保持。
- 文言は `src/BatonE2E/Lang/ja.ini`。コード中に日本語文言を直書きしない（P0の自己テスト用ログを除く）。

## 暗号仕様 v1（実装: src/BatonE2E.Core/Crypto）
- 鍵導出: PBKDF2-HMAC-SHA256、反復600000（視聴側は100000〜10000000のみ受理）、salt16、鍵256bit。パスワードはNFC正規化→UTF-8。
- 暗号: AES-256-GCM。フレーム = 連番8(BE) + 暗号文 + タグ16。nonce = sid(4)+連番(8)。AAD = "E2ERv1"+sid+連番(8)。
- 平文の先頭1バイトが種別（0x00確認/0x01音声/0x02曲名/0x03形式変更）。種別は暗号文の内側にありGCMで改ざん検知される（AADには入れない）。
- 連番0 = 確認ブロック（0x00 + "E2ERADIO1"）。1以降は全種別で共通の単調増加。受信側は最初に受けた値から+1を要求。
- 塩とsidはセッション（起動）ごとに乱数。

## 現在の状態
P0完了（実機確認済み）。P1: 暗号モジュール・自己テスト・C#→Node相互運用テストを作成。Actionsの結果待ち。

## バックログ
- アイコン（assets/BatonE2E.ico）／英語 Lang/en.ini／設定のバックアップ機構／ダークテーマ（GUI段階）
