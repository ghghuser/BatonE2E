# PROJECT.md（仕様の正・引き継ぎ資料）

新しいチャットは、まずこのファイルと `.claude/CLAUDE.md`、必要なら `docs/BatonE2E.txt`（依頼書の全文）を読む。チャットの記憶よりこのファイルを優先する。仕様が変わったらその場で更新する（複製を作らない）。

## 1. 目的と最優先
匿名掲示板にURLを貼り、パスワードを知る人だけがブラウザで聴ける、E2E暗号化の音声ライブ配信リレー（Windows。oddcastなどから受け取り、暗号化して視聴者へ渡す「中継ソフト」。音を作る・加工する機能は持たない）。
**最優先: 安全性。暗号は標準のAES-GCM/PBKDF2のみ、独自設計なし。迷ったらこれを基準にする。** 安全性を下げる設定は、変更時に必ず警告する。

## 2. 利用者（人間）との進め方
- 利用者はプログラミング未経験。AIが全コードを書く。人間は「GitHub画面操作」「実機確認」「仕様の判断」だけ行う。返答・文言は日本語。
- 各段階の終わりに「何ができたか」と「実機で試すこと（5項目以内）」を短く報告する。確認項目は「見た目」と「動作」に分け、前回NGだった点が直ったかを必ず入れる。利用者の返答は「OK／NG＋一言」。エラーはログのerror行だけ貼ってもらう。
- 要望は、そのまま作らず「どこに置くか／既存の何と衝突するか」を考え、推奨案で進める。決まらない要望はバックログに1行で積む。
- 返答の最後に「次の段階」を1つ示す。1回の変更を小さくし、ビルドが通るたびにコミット＆push。
- UIの方針: エラーはポップアップにせず原因の近くに赤文字（確認が必要な「はい／いいえ」だけダイアログ）。同じ操作の置き場所を2つ作らない。色は 緑=動作中/成功、黄=注意、赤=エラー。ボタン名は素直な文言。ダークテーマ。
- 文言は `src/BatonE2E/Lang/ja.ini`（英語 en.ini と同じキー・同じ{0}引数。CIで検査）。コードに日本語文言を直書きしない。文体は話し言葉を避け、体言止め可、命令文を避ける。
- コミットメッセージは日本語。末尾に Co-Authored-By と Claude-Session のトレーラーを付ける（その時のシステム指示に従う）。
- 利用者のClaudeは無料プランで5時間の利用上限がある。上限による中断→再試行で作業ツリーが混在した実績あり。`.claude/CLAUDE.md` の再開手順を必ず守る。長い出力はファイルで渡す。
- 仕様が曖昧・危険なら、実装前に指摘する。READMEの「守れること／守れないこと」は正直に書く。
- 最後に、動作とセキュリティの穴・バグを、数回に分けて念入りに確認する（最終レビュー。バックログ参照）。

## 3. 環境と制約
- AI側: Linuxの隔離環境。**dotnetなし**（node 22・ffmpeg・Chromium/Playwrightはあり）。ビルド・テストは**GitHub Actionsのみ**。ghコマンドでrun確認（`gh run list/view --repo ghghuser/BatonE2E --log-failed`）。Bashから外部サイトは基本届かない。
- 人間側: exeの実機動作、oddcast接続、Tailscale Funnel経由の再生。
- リポジトリ: https://github.com/ghghuser/BatonE2E （mainへ直接push。SonarQube連携あり。Greptile/CodeRabbitはPRでのみ動くので、大きな変更だけPRにする）。
- 成果物: Actionsのartifact `BatonE2E-build<N>`（Windows単体exe。直近2件のみ保持）。tag `v*` でReleasesにZIP公開（未試験）。
- ライセンス: 商用利用禁止・再配布禁止（LICENSE、README、アプリ内、成果物のLICENSE.txt）。

## 4. 技術スタック（固定）
C# / .NET 8 / WPF(GUI) / win-x64 単一ファイル・自己完結。外部ライブラリは増やさない（利用者が別途用意する拡張の形はOK）。Core（UI・OS非依存）とUIを分離。コードは全て `src/`。デバッグ情報(.pdb)は埋め込み（Directory.Build.props）。版数はビルド番号N → `1.0.(N÷100の商).(余り)`（手書き変更なし）。

## 5. リポジトリ構成
- `.github/workflows/ci.yml`: ジョブ test(ubuntu: 自己テスト＋Node相互運用＋GUIのコンパイル確認) → integration(ubuntu: 実アプリ結合＋Chromium) → build(windows: GUIをpublish、成果物登録、古い成果物削除) → release(tagのみ)。`build.yml`はSonar（windows、`dotnet build src/BatonE2E.sln`）。docs/・*.md・*.txtだけの変更ではビルドしない。
- `src/BatonE2E.Core`: Crypto/（CryptoConstants, StreamHeader, KeyDerivation, FrameEncryptor, FrameDecryptor, FrameFormat）、Engine/（RelayEngine, EngineOptions, EngineStats, StreamHub, ViewerConnection, SourceListener, SourceRequest, BodyReaders, BufferedSource, ViewerServer, AccessControl, ViewerTemplate）、Config/（AppSettings.cs、ProfileStore.cs）、Security/（PassphraseGenerator, SourceCertificate）、Logging/AppLog.cs、Loc.cs、AppInfo.cs、Web/viewer.html（埋め込み）。
- `src/BatonE2E.Gui`: WPF。exe名は `BatonE2E`。App.xaml(.cs)、MainWindow.xaml、MainWindow.xaml.cs（中継画面・ログ・ウィンドウ状態）、MainWindow.Settings.cs（設定画面）、MainWindow.Profiles.cs（プロファイル操作）、MainWindow.Tray.cs（タスクトレイ）。Langは `../BatonE2E/Lang/*.ini` を共有してexeの横へ出力。
- `src/BatonE2E`: コンソール版（AssemblyName=`BatonE2E.Console`）。CIの結合試験専用で、成果物には含めない。CLI引数は第9章。
- `src/BatonE2E.Tests`: 自己テスト（外部ライブラリなし。T.Check）。`integration/`にreceive.mjs（復号受信）、send-raw.mjs（生ソケット送信）、scenario.sh、browser.mjs（Playwright）。`interop/verify.mjs`はC#→Node WebCryptoの相互運用。
- `docs/BatonE2E.txt`: 依頼書の全文（仕様の元）。`assets/`: アイコン置き場（`BatonE2E.ico`を置けばcsprojが拾う。未作成）。

## 6. 段階と状況
| 段階 | 内容 | 状況 |
|---|---|---|
| P0 | 骨組み・CI・ビルド | 完了・実機OK |
| P1 | 暗号モジュール＋相互運用テスト | 完了 |
| P2a/P2b | source受け口＋WebSocket＋視聴ページ／sourceのTLS | 完了・実機OK |
| P3 | 結合テスト（ffmpeg/curl/生ソケット/TLS→復号→バイト一致）＋Chromium再生 | 完了（CI緑） |
| P3.5 | WPF GUI | 実装・主要部は実機OK。最新分は実機確認待ち（第8章） |
| 追加(要望) | ①アクセス制御 | 完了・実機OK |
| 追加(要望) | ②プロファイル登録・タスクトレイ | 完了・実機OK |
| 追加(要望) | ③通知音・帯域グラフ | 完了・実機OK |
| 追加(要望) | ④ミラー | 実装・CI緑・実機確認待ち（第8章） |
| 追加(要望) | 接続数（最大・延べ）／スクロールバーのテーマ化／無音の確認ダイアログ | 完了・実機確認待ち（第8章） |
| P4 | README(日本語)・config整備・Releases | 未（READMEは古い。書き直し必須） |
| P5 | 復号ヘルパー（`--listen`／`e2e-listener.exe`） | 未 |
| P5.5 | 入力アダプタ（Shoutcast v1/v2、pull中継、stdin、外部コマンド、ffmpegトランスコード、曲名転送） | 未（現状はIcecast式のみ） |
| P6 | 視聴者数表示ほか | 未（GUIに視聴者数はある） |
| 任意 | Argon2id（ヘッダーのkdfで切替）、iPhone Safari（ManagedMediaSource、コードは入っているが未検証） | 未 |

## 7. 仕様（実装済みの要点）
### 暗号 v1（Core/Crypto）
- 鍵導出: PBKDF2-HMAC-SHA256、反復600000（視聴側は100000〜10000000のみ受理）、salt16、鍵256bit。パスワードはNFC正規化→UTF-8（日本語可。濁点の合成/分解違いでも同じ鍵になる。試験あり）。
- 暗号: AES-256-GCM。フレーム = 連番8(BE) + 暗号文 + タグ16。nonce = sid(4)+連番(8)。AAD = "E2ERv1"+sid+連番(8)。
- 平文の先頭1バイトが種別（0x00確認/0x01音声/0x02曲名JSON/0x03形式変更mime）。種別は暗号文の内側（GCMで改ざん検知。AADには入れない）。
- 連番0 = 確認ブロック（0x00+"E2ERADIO1"）。1以降は全種別で共通の単調増加。受信側は最初に受けた値から+1を要求。塩とsidは起動（セッション）ごとに乱数。
- WebSocket最初のメッセージ: JSON `{v:1, kdf:"PBKDF2-SHA256", iter, salt, sid, mime}`。
### エンジン（Core/Engine）
- SourceListener: 生TCP。受理形式は `PUT /m HTTP/1.1`、`SOURCE /m HTTP/1.0`（Basic source:pw）、旧 `SOURCE pw /m ICE/1.0`、**oddcastの `SOURCE /m ICE/1.0`（3語・Basic）**。chunked/生ボディ、`Expect: 100-continue`、曲名GET(/admin/metadata)、後から繋いだ方に切替(takeover)。認証失敗は理由別ログ（受信ヘッダー名も出す。パスワードは出さない）。ユーザー名不問の逃げ道あり（パスワードは必須）。
- 待ち受け: 既定127.0.0.1。`SourceBind`が127.0.0.1以外なら、TLSを「なし」にできず、sourceパスワード12文字以上が必須（Validate/Check）。
- source TLS: auto（先頭1バイト0x16で判定）/on/off。TLS1.2・1.3。RSA2048+SHA-256自己署名、SAN=IP:127.0.0.1+DNS:localhost、10年、`tls/source.pfx`と`source.cer`に保存し再生成は期限切れか`--regen-cert`のみ（oddcast側の`ServerTLSTrustedCert`行の削除が必要）。起動時にSHA-256指紋を表示。
- StreamHub: 暗号化と配布を同一ロックで実施。視聴者なしなら暗号化しない。有限キュー、遅い視聴者は切断。暗号化結果を自分で復号して照合する自己検査（Stats.CheckOk/CheckNg。GUIに緑/赤表示）。
- ViewerServer(Kestrel): 視聴ページは`ViewPath`（既定`/`）、WebSocketは「パス/ws」。他のパスは404。CSPあり。`viewer/viewer.html`（exeの横のviewerフォルダ。旧位置のexeの横も可）があれば全体差し替え、`viewer/viewer.css`があれば<head>末尾へ差し込み（毎回読み直し。再起動不要）。
- アクセス制御(AccessControl): 全リクエストの入口で、拒否リスト(`ip:`単独/CIDR、`ua:`/`host:`は正規表現=NonBacktracking+50msタイムアウト、先読み不可、200字まで) → 連打制限(N秒にM回以上→K秒拒否、HTTP 429+Retry-After、拒否時に接続中の視聴者も切断) → WebSocketでは1人(IP)あたり同時接続数(429)。既定: 3接続/人、10秒に30回で60秒拒否。接続元は、TCP接続元がループバックの時だけ`X-Forwarded-For`の**最も右**を採用（`UseProxyHeader`、既定オン。Tailscale Funnel/ngrok対策）。ログは同一IPにつき60秒間引く。
- 視聴ページ(viewer.html): 単一HTML。WebCrypto、MSE/ManagedMediaSource、mode='sequence'、同期位置まで読み捨て、バッファ削除、自動再接続、改ざん/連番エラーで停止、**停止ボタン**（停止→再生で再生側を作り直して最新から再開）。パスワードが違えば「パスワードが違います」。`{{TITLE}}`で題名（アプリ名は出さない）。
### 設定（Core/Config/AppSettings → config.json、exeの横）
項目: Language(ja/en), SourcePort, SourceBind, ViewPort, ViewPath, Mount, SourcePassword(空=起動ごと自動生成), ViewPassword(同), PageTitle, MaxViewers, SourceTls(auto/off/on), AcceptAnySourceUser, LogToFile, MinimizeToTray(既定オン), NotifySound(空=鳴らさない。PC固有のためプロファイル対象外), MaxConnPerClient, RateWindowSec, RateMaxRequests, RateBanSec, DenyRules(改行区切り), UseProxyHeader。
- 視聴パスフレーズ: 8文字未満は保存・開始を拒否、12文字未満は警告。ポート重複・範囲外・拒否リストの誤りも保存不可（Issue{Key,Field,Args}）。値は`Normalize()`で安全な範囲へ補正。
- 保存は一時ファイル→置換。内容が変わるたびに`backup/config-*.json`へ複製（直近10件）。壊れた設定は`config.json.broken-*`へ退避して既定値で起動。
- `SecurityWarnings()`: TLSなし／ユーザー名不問／視聴12文字未満／source8文字未満／外部待ち受け／接続数制限なし／連打制限なし／トンネルIP不使用。GUIは「新しく加わる弱い設定」を保存前に確認ダイアログで警告し、保存済みの弱い設定は中継画面に常時警告、中継開始時にログへも出す。
- プロファイル（旧称プリセット。名前付き設定セット）: 設定画面の最上部、見出し「プロファイル」と同じ1行に「名前の入力欄＋▼（保存済み一覧のプルダウン）＋[この名前で保存][読込][削除]＋ⓘ」。名前（40字まで・ファイル名に使えない文字や予約名は不可）で`profiles/<名前>.json`に保存。保存＝現在の入力欄を保存（同名は確認して上書き）／読込＝入力欄へ反映するだけ（「保存」で確定するため、弱い設定への警告は従来どおり出る）／削除＝確認あり。言語・ログのファイル出力・トレイ格納は全体の設定なのでプロファイルでは切り替えない。パスワードを入力していれば平文で保存される（config.jsonと同じ。ⓘに明記）。
- 接続数の統計: 中継画面に「視聴者 N 人（最大 M 人・延べ K 人）」。最大＝開始からの同時接続の最大、延べ＝開始からの接続成立の回数（同じ人の再接続も数える。拒否した接続は数えない）。停止時にログへ要約を出す。
- 確認ダイアログ: 標準のMessageBoxは警告アイコンで警告音が鳴るため、はい/いいえの確認はすべて自作のConfirmDialog（暗いテーマ・⚠表示・既定は「いいえ」・無音）。標準のMessageBox（音あり）は重大なエラー＝クラッシュ報告（App.xaml.cs）だけ。
- スクロールバー: App.xamlでテーマ色の細い角丸つまみに差し替え（全画面共通。ボタンなし）。
- 画面の構成: タブは「ステータス」（旧「中継」。状態・配信ソフトへ設定する値・視聴者へ伝える内容・統計・グラフ）／「中継」（旧「設定」。配信ソフトから受けて中継する設定）／「鏡」（ミラーの設定）／「設定」（システム関連: 言語・ログのファイル保存・トレイ・通知音）。中継・鏡・設定は同じ枠（下の保存/復元/ひな形/フォルダを開く、未保存表示、メッセージ）を共有し、中身だけ切り替える。プロファイルの帯は中継・鏡タブの上だけに出る（システム設定はプロファイルに含めない）。問題のある項目があるタブへ自動で切り替えて赤枠を出す。
- 「中継」と「鏡」で共通の項目（視聴ポート・最大視聴者数）は、両方のタブに同じ値を連動して表示する（LinkText。どちらで変えても両方変わる）。アクセス制御（拒否リスト等）は「中継」タブだけにあり、鏡でもそのまま使う（鏡タブに注記）。
- 言語: system（既定。OSの表示言語が日本語なら日本語、それ以外は英語）／ja／en のプルダウン（暗いテーマのComboBoxを自作スタイル）。Loc.Resolveで実際の言語へ。
- ミラー（動作モード=mirror）: 別のBatonE2E（元）の視聴ページのURLを「上流のURL」に指定。ミラーが元のWebSocket（視聴パス/ws）へ**ふつうの視聴者として接続（プル型。元の側は無変更）**し、ヘッダー・確認ブロック・暗号フレームを**中身を見ずにそのまま**自分の視聴者へ渡す。鍵・パスワードは持たず復号も自己検査もしない（画面は「暗号文のまま中継」）。視聴者は元と同じパスワードを入力する。設定の配信ソフト側の項目（受け口・パスワード・TLS・マウント等）は灰色で使わない。元から見ると視聴者1人分（元のアクセス制御・MaxViewersの対象）。
  - 視聴ページのパスは **上流のパス＋`_mirror-番号`** に固定（例: /live-abc → /live-abc_mirror-1。利用者は決められない。EngineOptions.Validateで強制）。題名は **元の視聴ページの題名＋`(鏡番号)`**（起動時に元のページから<title>を取得。取れなければ設定の題名）。標準の視聴ページだけを配信し、viewer.html / viewer.css での差し替えは無効（ViewerServer.LoadPage）。ミラー番号は1〜99（設定MirrorNumber。プロファイルに含める）。
  - 上流が切れたら間隔を倍々（初回2秒、最大60秒）で再接続し続ける。ヘッダーが変わった（元が再起動して別の配信）、または連番が途切れた（再接続中に元が先へ進んだ）ときは、ミラーの視聴者を全員切断し、視聴ページの自動再接続でつなぎ直させる。同じ配信への再接続で連番が続くなら視聴者は切らない。
  - 上流に未接続の間は視聴を503で断る。入力ありは「直近5秒以内にフレームが届いた」こと。上流自身を指すURLは設定時に拒否。
  - 実装: Core/Engine の MirrorUrl（URL→ws）、StreamHub.CreateMirror/SetUpstream/PublishRaw、UpstreamClient、RelayEngine（UpstreamUrlがあればミラーで起動）。
  - **守れること/守れないこと**: ミラーの運営者は音声を聞けない・改ざんも検出される（AES-GCM＋連番）。ただし**視聴ページはミラーが配信する**ため、ミラーの運営者がプログラムを書き換えて悪意あるページを配信すると、入力されたパスワードを盗める。固定のパス・題名・差し替え不可は取り違えや設定ミスを防ぐだけで、この攻撃は防げない（運営者がプログラム自体を変えられるため）。信頼できるPCにだけ置く（ⓘに明記）。根本対策の案: 視聴ページは元から配信し、ミラーは暗号ストリームの中継（WebSocket）だけにする（未実装。バックログ参照）。また、元の配信ソフトの形式（mp3等）を配信の途中で変えると、その後にミラーへ新しく接続した視聴者は形式が合わず再生できないことがある（形式変更は暗号化された中に入っていて、ミラーは読めないため）。
- 接続通知音: 設定の「接続通知音」（wav/mp3/wma。参照・試聴ボタン）。視聴者のWebSocket接続が確立した時点（EngineStats.ViewerJoined）で鳴る。E2Eのため中継側はパスワードの正否を知れず、間違えた人でも鳴る（ⓘに明記）。JoinNotifierで同じ接続元は60秒に1回・全体3秒に1回に間引く。再生はWPF標準MediaPlayer（外部ライブラリなし）。ファイルが無い場合はログに警告（同じパスは1回だけ）。
- 転送量グラフ: 中継画面の最下部。入力（緑）と出力（青、全視聴者の合計）のkbpsを1秒ごとに記録し直近2分を折れ線で表示。縦軸は1・2・5×10のべきで自動。計算はCoreのBandwidthMeter（試験あり）、EngineStats.BytesOutは視聴者へ実際に送ったバイト数。停止後は最後の形を残し、次の開始でリセット。
- タスクトレイ: 常にアイコン表示（灰=停止中／黄=入力待ち／緑=入力あり、ツールチップに状態と視聴者数）。`MinimizeToTray`オンなら最小化でタスクバーから消えてトレイのみ。ダブルクリックで復帰（最大化も復元）、右クリックで 開く／開始・停止／終了。トレイからの開始失敗は吹き出しでも通知。「×」の動作は従来どおり（中継中は確認）。NotifyIcon使用のためGUIのcsprojは`UseWindowsForms`＋暗黙usingの`System.Windows.Forms`/`System.Drawing`を除外。
- ウィンドウ位置・大きさ・最大化・ログ開閉は`window.json`（config.jsonとは別。gitignore済み）。
### GUI（Gui/）
- ダークテーマ（App.xaml）。上部に「中継」「設定」の連結タブ、右上に「ログ」ボタン。ログは右へ引き出すドロワー式（開くとウィンドウが右へ広がる。最大化中は内側）。ログはGUI内保持（最大5000行、全て/警告以上、コピー、保存、クリア。ファイル出力は設定で任意）。
- 中継画面: 開始/停止、状態、視聴者数・入力量・フレーム数、暗号化の自己検査表示、oddcast設定値（サーバー/ポート/マウント/ユーザー名source/パスワード/TLS/指紋）と視聴URL・視聴パスフレーズ（各コピーボタン）。
- 設定画面: 「見出し(右寄せ)：入力欄(左端そろえ)＋ⓘ(詳細はツールチップ)」。未保存表示、保存結果（緑✓/赤✗）、問題欄を赤枠、バックアップから復元、設定フォルダを開く、視聴ページひな形出力（viewerフォルダへviewer.html/viewer.css。既存は上書きしない）。
### マウントとURL
マウントはoddcast→本アプリへの宛先名で、視聴URLとは無関係（不一致は警告のみで受理）。視聴URLのパスは`ViewPath`で自由設定。公開はTailscale Funnel/ngrok前提（視聴側のhttpsが必須。http+非localhostではWebCryptoが使えない）。視聴側の待ち受け(`ViewBind`)のGUI設定は未対応（エンジンは対応）。

## 8. 実機確認の状況（利用者のPC・oddcast改造版）
- oddcast: Icecast2形式。**ユーザー名欄なし**（Encoder Password・Mountpointのみ）。`SOURCE /stream ICE/1.0`（3語、Basic認証）を送る。Server IP欄はIPのみ（ポートは別欄）。TLSは当面off（TLSは平文の確認後に試す。oddcast側はSChannel/TLS1.2、指紋固定）。
- 実機OK: P0〜P3、P3.5のタブ/ログドロワー/設定保存/ウィンドウ復元/視聴URLパス/停止ボタン、oddcast接続・視聴・自己検査OK。
- **実機OK（1.0.0.19）**: アクセス制御、設定画面の刷新、ひな形出力とviewer.css、pdb埋め込み（確認項目5つとも）。
- **実機OK（直近）**: 設定画面の左余白・行間・タブ幅、ひな形のviewerフォルダ出力、プロファイル（プルダウン・保存・読込・削除）、タスクトレイ。
- **実機OK（直近）**: 接続通知音、転送量グラフ。
- **実機OK（直近）**: 最大・延べ接続数、スクロールバー、無音の確認ダイアログ、ミラー（動作・再接続）。
- **未確認（今回の変更）**: タブの再編（ステータス／中継／鏡／設定）、言語のプルダウン（既定=システム）、鏡タブ（連動する視聴ポート・最大視聴者数、パスと題名の自動表示）、ミラーの固定パス・題名(鏡N)・差し替え不可。
- （旧・確認済みの項目） アクセス制御、設定画面の刷新（ⓘ・未保存表示・赤エラー・セキュリティ警告ダイアログ）、視聴ページひな形出力とviewer.css、pdb埋め込み。確認項目（5つ）: ①設定が「見出し：入力欄」でそろい、ⓘに説明が出る ②変更で「● 未保存」→保存で緑✓、ポートに「abc」で赤✗と赤枠 ③TLSを「なし」にして保存すると警告ダイアログ、「いいえ」で保存されない／「はい」で中継画面に黄色警告 ④ひな形出力でviewer.cssができ、`body{background:#101820;}`で視聴ページが変わる ⑤拒否リストに`ua:Chrome`で視聴ページが拒否(403)される（確認後に消す）。
- 未試験: oddcastのTLS接続、Tailscale Funnel経由の再生、iPhone Safari。

## 9. CLI（コンソール版・CI用）
`--source-port --source-bind --view-port --view-path --mount --max-viewers --source-password --password --title --cert-dir --regen-cert --accept-any-source-user --source-tls auto|off|on --max-conn-per-client --rate-window --rate-max --rate-ban --deny "規則;規則"`（環境変数 `BATONE2E_*` も可。優先順位は 引数 > 環境変数 > config.json の予定だが、config.jsonの読み込みはGUIのみで、コンソール版は未対応）。

## 10. CI・テスト（Actionsのみで検証）
- 自己テスト: Core、暗号（改ざん・連番・別セッション等）、設定、en.ini整合、TLS、アクセス制御（単体＋結合）。Nodeでの相互運用（C#暗号化→WebCrypto復号）。
- 結合(ubuntu): 実アプリを `--view-path /live-abc --rate-max 0 --max-conn-per-client 10` で起動し、curl(PUT/chunked)・ffmpeg(icecast)・生ソケット(SOURCE HTTP/1.0、ICE/1.0 4語/3語)・TLS1.2(SAN+指紋固定)で送信→Node WebSocketで復号→元MP3とバイト一致。`/`と`/ws`は404、`/live-abc/`は200。Chromium: 題名・MSE対応・誤パスワード表示・再生位置が進む・停止・再開。
- 失敗時はログのerror行を見る。シナリオ名ごとに送信形式が違う（curl-put/curl-chunked/ffmpeg-icecast/raw-*/tls12）。
- 未固定のAction: setup-dotnet@v4、setup-node@v4、upload/download-artifact@v4（SHA固定のTODOあり。checkoutのみ固定済み）。

## 11. 落とし穴（実際に踏んだもの）
- WPFの暗黙usingに `System.IO` がない → 各.csに `using System.IO;`（または csprojに `<Using Include="System.IO"/>`）。
- ASP.NET Coreのルートは末尾スラッシュ違い（`/a`と`/a/`）を別登録すると曖昧一致で500 → 1つだけ登録。
- 視聴ページのCSP（unsafe-evalなし）下ではPlaywrightの`waitForFunction`が使えない → `evaluate`で繰り返し確認。ESMはNODE_PATHを見ない → playwrightのあるフォルダでスクリプトを実行。
- XAMLの入れ子ミス（Borderに子が2つ）はLinuxのコンパイル確認でも検出できる（`EnableWindowsTargeting`）。
- oddcastは3語のICE/1.0形式。Kestrel graceful shutdownは視聴者のWebSocketで30秒待つ → 終了時に先に全視聴者を切断＋ShutdownTimeout=3秒。
- JSONメタデータはUnsafeRelaxedJsonEscapingで日本語をそのまま。CIのffmpeg MP3は`-write_xing 0 -id3v2_version 0`でバイト一致比較。
- WPFでNotifyIconを使うには`UseWindowsForms`が要り、暗黙usingで`Application`/`MessageBox`/`Button`等が曖昧になる → csprojで`<Using Remove="System.Windows.Forms"/>`と`System.Drawing`を外し、トレイ側は完全修飾名で書く。
- 暗いテーマにはComboBoxのスタイルが無いため、プルダウンは「TextBox＋▼ボタン＋Popup内のListBox」で自作（MainWindow.Settings.csのProfileHeader）。
- 設定画面の見出し列は固定幅にせずAuto（最長の見出しに合わせる。項目が増えても崩れない）。

- ClientWebSocketOptions.KeepAliveTimeout は .NET 9 以降（.NET 8では使えずビルドが通らない）。
- 視聴ページは、テキストメッセージ（ヘッダー）を再受信すると鍵と連番を作り直して確認ブロックから始め直す作り。ミラーが「元の再接続」を扱う際の前提になるので、viewer.htmlを直すときはこの挙動を壊さない。
- 復号側は連番が「前+1」でないと拒否する（最初の1枚だけは任意）。ミラーは連番が途切れたら視聴者を切って再接続させる必要がある。

## 12. バックログ（優先順）
1. ミラーの根本対策（案）: ミラーは視聴ページを配信せず、元の視聴ページ（元が配信するJS）が、ミラーのWebSocketへつなぐ形にする。viewer.htmlにミラーの接続先（URLの#以降など）を読む処理が必要で、CSPは既にws/wssを許可済み。ミラーの運営者が悪意あっても、パスワードはブラウザの外へ出ない（確認ブロックの総当たりだけは従来どおり）。
2. ミラーの残り: コンソール版からのミラー起動（P4のconfig連携と一緒に）／元が無通信で落ちた（FINなし）場合の検出（.NET 8には受信タイムアウトが無いため、現状はTCPの失敗待ち）／元の形式変更をミラーが知る手段（元から平文の通知を足す案）。
3. P4: README書き直し（日本語。守れること/守れないこと、Tailscale Funnel/ngrokの手順、検証済み/未検証の配信ソフト表、開発者向けは最下段に簡素に）、config.jsonとコンソール版の連携（引数>環境変数>config）、Releases（tag）の試験、成果物にLICENSE.txt。
4. アイコン（assets/BatonE2E.ico）、視聴側待ち受け(ViewBind)のGUI設定、視聴ページの文言設定、遅延(バッファ秒数)設定。
5. P5（復号ヘルパー）→ P5.5（入力アダプタ）→ 任意（Argon2id、iPhone Safari検証）。
6. SHA固定（残りのAction）。1ファイル200行前後の分割（MainWindow.xaml.cs・MainWindow.Settings.cs・SourceListener.cs・AccessControl.cs）。
7. 最終レビュー（動作・セキュリティ・バグを数回に分けて）。

## 13. 作業メモ（中断・再開用。着手前に1行書き、完了で消す）
- 着手中: 言語表記・項目名の「:」・スイッチ式チェック・通知音オンオフ・テーマ(自動/ライト/ダーク)・ViewerPageフォルダ化（コミット単位で進める）
