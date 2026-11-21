# BatonE2E

E2E暗号化の音声ライブ配信リレー。URLとパスワードを知る人だけが、ブラウザで聴ける配信を目指す。

**現在の状態: P0（骨組み）。配信機能は未実装。**

## ライセンス
商用利用禁止・再配布禁止。詳細は [LICENSE](LICENSE)。

---

## 開発者向け
- 仕様の正: [PROJECT.md](PROJECT.md)、依頼書 docs/BatonE2E.txt
- 構成: `src/BatonE2E.Core`（純ロジック）、`src/BatonE2E`（exe）、`src/BatonE2E.Tests`（自己テスト）
- ビルドはGitHub Actionsのみ。`CI`ワークフローが test → build の順に実行。
- 版数: ビルド番号 N → `1.0.(N÷100).(N%100)`
