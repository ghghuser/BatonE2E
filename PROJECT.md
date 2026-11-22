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
- P1 暗号モジュールと相互運用テスト（C#→Node WebCrypto）（完了）
- P2 source受け口＋WebSocket配信＋視聴ページ （P2a: 平文source 完了／P2b: source受け口のTLS ← 現在）
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

## エンジン構成（src/BatonE2E.Core/Engine）
- SourceListener: 127.0.0.1のみ、生TCP。PUT / SOURCE / ICE/1.0、chunked、Expect: 100-continue、曲名GET。後から繋いだ方に切替（takeover）。
- StreamHub: 入力を暗号化して全視聴者へ配信（暗号化と配布は同一ロック）。暗号化結果を自分で復号して照合する自己検査あり（Stats.CheckOk/CheckNg）。
- ViewerServer: Kestrel（共有フレームワーク）。GET / = 視聴ページ（exeの横のviewer.htmlがあれば優先）、GET /ws = WebSocket。
- 視聴者は有限キュー。溢れた（遅い）視聴者は切断。上限人数超過は503。
- 暗号化の確認表示: コンソールの統計行に「暗号化の自己検査 OK/NG」を10秒ごとに出力。

## source受け口のTLS（P2b）
- `SourceTls`: auto（既定。最初の1バイトが0x16ならTLS）/ on / off。TLS 1.2・1.3、最初から暗号化する方式のみ。
- 証明書: RSA2048+SHA-256 自己署名、SAN=IP:127.0.0.1+DNS:localhost、10年。`tls/source.pfx`（秘密鍵、パスワードなし）と `source.cer`（公開部）に保存し、毎回は作り直さない。作り直しは期限切れまたは `--regen-cert` のみ（oddcast側の ServerTLSTrustedCert 行の削除が必要と警告）。起動時にSHA-256指紋を表示。
- oddcast（Icecast2形式）の設定画面にユーザー名欄がない。通例は `source` 固定で送られる想定。認証失敗時は受信したユーザー名（パスワードは出さない）をログに出す。ユーザー名が違う場合の逃げ道として `--accept-any-source-user`（パスワードは必須のまま）。
- 視聴ページの題名・見出しは `--title`（既定「ライブ配信」。アプリ名は出さない）。

## 現在の状態
P0・P1・P2a・P2b完了。P3完了（CI緑）: 実アプリを起動し、curl(PUT/chunked)・ffmpeg(icecast)・生ソケット(SOURCE HTTP/1.0、SOURCE pw /m ICE/1.0、SOURCE /m ICE/1.0)・TLS1.2(指紋固定+SAN確認)で送信 → Node WebSocket受信側が復号 → 元MP3とバイト一致。Chromiumで視聴ページ実再生（currentTime進行、誤パスワード表示、題名にアプリ名なし）。
- 実機で判明: oddcastは `SOURCE /stream ICE/1.0`（3語・Basic認証）を送る → P2b後に対応済み（実機再確認待ち）。oddcastのServer IP欄はIPのみ（ポートは別欄）。
- 実機確認: oddcast（ICE/1.0）接続・視聴・自己検査OK。コンソール版は「停止」操作が弱い（Ctrl+C相当）→GUIに停止/開始ボタンを設ける。
- P3.5 実装済み・CI緑（実機確認待ち）: src/BatonE2E.Gui（WPF、exe名BatonE2E）。配信/ログ/設定の3画面、開始停止、ダークテーマ、日英(Lang/ja.ini,en.ini)、config.json＋backup/（直近10件）。ログはGUI内保持（既定でファイル出力なし）。コンソール版は src/BatonE2E（AssemblyName=BatonE2E.Console、CI結合試験用・成果物には含めない）。
- P3.5 実機確認OK。追加対応済み: 視聴ページの停止ボタン（停止→再生で最新から再開、CIで検証）、GUIのログを右側の開閉パネルに変更、視聴パスフレーズは最小8文字（推奨12文字、日本語可・NFC正規化）。
- 追加: ボタン名「中継を開始/停止」、視聴URLのパス設定（ViewPath。設定外は404、WebSocketは「パス/ws」、CIで検証）、ウィンドウ位置・大きさ・ログパネル開閉の保存復元（window.json、gitignore済み）。マウントは配信ソフト→本アプリ側の名前で、視聴URLとは無関係（不一致は警告のみで受理）。
- 追加: タブ化（中継/設定）、ログは右へ引き出すドロワー式、sourceの待ち受けアドレス設定（127.0.0.1以外はTLS必須・sourceパスワード12文字以上）。
- ①アクセス制御 実装済み・CI緑: 1人あたり接続数、連打制限（N秒にM回以上→K秒拒否＋接続中も切断）、拒否リスト（ip:/ua:/host:、正規表現はNonBacktracking）、X-Forwarded-For（最も右、接続元がこのPCの時のみ）。
- 設定画面刷新: 見出し：入力欄＋ⓘ、未保存表示、保存結果（緑✓/赤✗）、問題欄を赤枠、セキュリティを下げる変更は保存前に確認ダイアログ＋中継画面に常時警告。視聴ページ: viewer.css（見た目のみ）/viewer.html（全体差し替え）、設定画面のボタンでひな形出力。pdbは埋め込み。
- 次: ②プリセット登録・タスクトレイ → ③通知音・帯域グラフ → ④ミラー。
- 次(旧): 実機確認の結果を反映 → P4（README・Releases・config整備）。視聴URLのパス自由設定は未対応（バックログ）。

## 作業メモ（中断・再開用。着手前に1行書き、完了で消す）
- （現在なし）

## バックログ
### GUI/運用機能（ユーザー要望 2026-11-22。実装順の案: ①アクセス制御 ②プリセット・トレイ ③接続音・帯域グラフ ④ミラー）
- ①アクセス制御（視聴側）: 同一クライアントの同時接続数制限／連打アクセスのキック（N秒にM回以上→K秒拒否）／拒否リスト（IP・UserAgent・ホスト、正規表現可。ReDoS対策でNonBacktracking＋タイムアウト）。注意: Tailscale Funnel/ngrok経由だと接続元が127.0.0.1に見えるため、「信頼するプロキシのX-Forwarded-Forを使う」設定が必須。
- ②プリセット登録（名前付き設定セットの保存・読込・削除）／タスクトレイアイコン（最小化・常駐、中継状態表示）
- ③視聴者接続時の通知音（音声ファイル指定可、wav/mp3）／帯域使用率の折れ線グラフ（画面最下部。入力・出力の転送量を1秒ごとに記録）
- ④ミラー: 暗号文のまま別の中継（BatonE2E）へ転送し視聴者を分散・予備にする（ミラー側はパスワード不要）。P6候補。
- 視聴側の待ち受けアドレス（ViewBind）のGUI設定は未対応。外部公開にはhttpsが必要（http+非localhostではWebCrypto不可）なので、現状はTailscale Funnel/ngrok前提。
- 生のコンソール表示オプションは不要（GUIのログ欄＋コピー/保存で代替）。
- ログはGUI内に保持し、ログ表示ウィンドウで閲覧できるようにする（ファイル出力は併用）
- パス設定: source側のマウント(--mount、現状も変更可)・視聴URLのパス・パスワード（視聴/source）を自由に設定可能にする（config.json＋GUI）
- アイコン（assets/BatonE2E.ico）／英語 Lang/en.ini／設定のバックアップ機構／ダークテーマ（GUI段階）
