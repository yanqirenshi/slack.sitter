# SlackSitter ボード表示ロジック・フロー

Slack からデータを取得し、ボード UI に表示するまでの全体フローを整理したドキュメントです。

## 1. データ取得のトリガー

| トリガー | 起点 | 呼ばれるメソッド |
|---------|------|----------------|
| **初回認証成功** | `AuthenticateWithToken()` | → `RefreshWorkspaceDataAsync()` |
| **自動更新タイマー** | `_autoRefreshTimer`（5分間隔の `DispatcherTimer`） | → `RefreshWorkspaceDataAsync()` |
| **手動リフレッシュ** | ログポップアップのリフレッシュボタン | → `RefreshActiveBoardChannelsAsync()`（差分更新対応） |
| **トークン更新** | ユーザーポップアップでトークン変更 | → `RefreshWorkspaceDataAsync()` |
| **カスタムボード作成/編集** | チャンネル追加リクエスト | → `MainController_AddChannelsRequested()` |

## 2. データ取得フロー

```
RefreshWorkspaceDataAsync()
 ├── Task.WhenAll（並列実行）
 │     ├── LoadCustomEmojiAsync()     ← emoji.list API
 │     │     → _customEmojiMap に格納
 │     │     → 絵文字URLを事前解決し MessageRenderContext.ResolvedEmojiUrls に設定
 │     │
 │     └── LoadUserAvatarAsync()      ← auth.test → users.info API
 │           → 現在のユーザー名・アバター画像を取得
 │
 └── LoadChannelsAsync()              ← conversations.list API（バッチ取得）
       │
       ├── GetChannelBatchesAsync() で100件ずつチャンネル取得
       │     → "times" プレフィックスのチャンネルをフィルタ
       │
       └── LoadChannelBatchAsync()（バッチごとに並列処理）
             │  SemaphoreSlim(10) で最大10並列に制限
             │
             ├── GetChannelMessagesAsync(channelId, 10, oldest)
             │     ← conversations.history API（最新10件、差分更新対応）
             │     ← ExecuteWithRetryAsync() でリトライ付き
             │
             ├── Task.WhenAll: GetThreadRepliesAsync(channelId, threadTs)
             │     ← conversations.replies API（reply_count > 0 のメッセージのみ、並列取得）
             │     ← ExecuteWithRetryAsync() でリトライ付き
             │
             └── Task.WhenAll: GetUserImageUrlAsync(userId)
                   ← users.info API（キャッシュ付き、リトライ付き）
```

### 差分更新フロー（手動リフレッシュ・自動リフレッシュ時）

```
RefreshActiveBoardChannelsAsync()
 ├── 既存チャンネルの LastFetchedTs を収集
 ├── LoadChannelBatchAsync(channels, workspaceUrl, oldestByChannelId)
 │     → oldest パラメータで前回取得以降の新着メッセージのみ取得
 └── UpsertChannelAcrossBoards() で既存データを更新
```

## 3. データ変換レイヤー

生の Slack データを表示用モデルに変換する。

```
SlackNet.Events.MessageEvent
        ↓ 変換
MessageDisplayItem
 ├── Segments: IReadOnlyList<MessageInlineSegment>
 │     ParseSegments() でテキストを解析（2パス最適化）：
 │     ├── 1パス目: Regex.Matches() でリンクと絵文字を一括検出
 │     ├── 2パス目: 装飾トグル（太字/斜体/取消線/コード）を処理
 │     ├── プレーンテキスト（太字/斜体/取消線/コード装飾）
 │     ├── リンク（<url|label> パターン）
 │     └── 絵文字（:name: パターン）
 │
 ├── Images: IReadOnlyList<MessageImageItem>
 │     ExtractImages() で画像URL抽出（PropertyInfo キャッシュ付きリフレクション）：
 │     ├── Files（mimetype が image/* のもの）
 │     └── Attachments / Blocks
 │
 ├── Reactions: IReadOnlyList<MessageReactionItem>
 │     ExtractReactions() でリアクション取得（名前＋件数、PropertyInfo キャッシュ付き）
 │
 ├── Replies: IReadOnlyList<MessageDisplayItem>（再帰的に生成）
 │
 ├── UserAvatarUri    ← ユーザーアバター画像
 └── PermalinkUri     ← Slack上の直リンク
```

変換された `MessageDisplayItem` は `ChannelWithMessages` にまとめられる：

```
ChannelWithMessages
 ├── Channel: SlackNet.Conversation（チャンネル情報）
 ├── Messages: List<MessageDisplayItem>（表示用メッセージ一覧）
 └── LastFetchedTs: string?（差分更新用の最終取得タイムスタンプ）
```

## 4. コレクション管理

4つの `BatchObservableCollection` でチャンネルを管理し、フィルタで切り替える。

```
_allDisplayedChannels          ← 全チャンネル
_joinedDisplayedChannels       ← 参加中のみ（IsMember=true）
_notJoinedDisplayedChannels    ← 未参加のみ（IsMember=false）
_customDisplayedChannels       ← ユーザー選択のカスタムボード
```

**ソート順**（`InsertDisplayedChannel()` / `CompareChannels()`）:

1. 参加中チャンネルを先に
2. 最終メッセージが新しい順
3. チャンネル名アルファベット順

**フィルタ切替**: `_currentChannelDisplayFilter` で表示するボードを選択（All / JoinedOnly / NotJoinedOnly / CustomOnly）

**バッチ更新**: `BeginBatchUpdate()` / `EndBatchUpdate()` で複数チャンネルの挿入を1回の `CollectionChanged(Reset)` にまとめる

## 5. UI レンダリング階層

```
MainWindow.xaml
 │
 ├── StatusPanelView         ← 状態表示（認証状態、エラー等）
 ├── MainControllerView      ← フィルタ切替、カスタムボード管理
 │
 └── ChannelBoardView × 4    ← フィルタごとに1つずつ
       │  ScrollViewer + ItemsRepeater + HorizontalVirtualizingLayout
       │  （ビューポート内のカードのみ実体化する仮想化レイアウト）
       │
       └── ChannelCardView（チャンネルカード、XAML テンプレート）
             ├── ヘッダー Border（チャンネル名 HyperlinkButton、参加状態で色分け）
             └── ScrollViewer
                   └── StackPanel（MessagesPanel、コードビハインドで動的構築）
                         │
                         └── MessageItemView（メッセージ1件、C#コードで構築）
                               ├── タイムスタンプ（HyperlinkButton → Slack直リンク）
                               ├── アバター画像（MessageRenderContext.PopulateAvatar）
                               ├── メッセージ本文カード
                               │    ├── RichTextBlock（MessageRenderContext.BuildMessageParagraph）
                               │    └── 画像スタック（ボタンクリックで遅延読込）
                               ├── リアクション表示（MessageRenderContext.PopulateReaction）
                               └── スレッド返信（MessageItemView を再帰表示）
```

## 6. コンテンツ描画

`MessageRenderContext`（シングルトン）がメッセージ描画のヘルパーメソッドとリソースを提供する。
`MessageItemView.BuildContent()` 内で直接呼び出し、Loaded イベントハンドラは使用しない。

| メソッド / リソース | 処理内容 |
|-------------------|---------|
| `BuildMessageParagraph()` | `Segments` をもとに `Run`（テキスト装飾）, `Hyperlink`（リンク）, 絵文字 `Image`（キャッシュ経由）を Paragraph に構築 |
| `PopulateAvatar()` | ユーザーアバターを `ImageBrush` で設定（`BitmapImageCache` 経由） |
| `PopulateReaction()` | 絵文字画像（事前解決済みURL + キャッシュ）or テキスト + カウントを Border に設定 |
| `ResolvedEmojiUrls` | エイリアスチェーン解決済みの絵文字URL辞書（O(1) ルックアップ） |
| `ImageCache` | `BitmapImageCache` — URL→BitmapImage の WeakReference キャッシュ |
| `ShowMessageImageButton_Click` | 画像を遅延読込（候補URLを優先順に試行、リダイレクト対応、Content-Type検証）※MainWindow で処理 |

## 7. キャッシュ・最適化

| 最適化 | 詳細 |
|--------|------|
| **BitmapImage キャッシュ** | `BitmapImageCache`（WeakReference）で同じURL の BitmapImage を共有。`DecodePixelWidth` でデコードサイズ制限（アバター64px、絵文字24px） |
| **ユーザー画像URLキャッシュ** | `_userImageUrlCache`（Dictionary）で userId → 画像URL をキャッシュ |
| **並列メッセージ取得** | `SemaphoreSlim(10)` で最大10チャンネル同時取得 |
| **スレッド返信の並列取得** | `Task.WhenAll()` で1チャンネル内のスレッド返信を同時取得 |
| **チャンネルバッチ取得** | `IAsyncEnumerable` で100件ずつ yield、到着順に即UI反映 |
| **スレッド取得の条件分岐** | `reply_count > 0` のメッセージのみスレッド取得（キャッシュ済みデリゲートで高速判定） |
| **画像の遅延読込** | ボタンクリック時にのみHTTP取得（初回表示のコスト削減） |
| **絵文字URLの事前解決** | `alias:xxx` チェーンをデータ取得時に1回だけ解決し `ResolvedEmojiUrls` に格納 |
| **メッセージパーサー最適化** | `Regex.Matches()` で一括検出 → Dictionary ルックアップの2パス方式（O(n)） |
| **リフレクションキャッシュ** | `ConcurrentDictionary<(Type, string), PropertyInfo?>` で PropertyInfo を型・プロパティ名ごとにキャッシュ |
| **ReplyCount デリゲートキャッシュ** | `PropertyInfo.GetGetMethod()` を起動時に1回だけ取得、`Func<>` デリゲートにキャッシュ |
| **バッチ UI 更新** | `BatchObservableCollection` で複数 Insert を1回の Reset 通知にまとめる |
| **UI 仮想化** | `ItemsRepeater` + `HorizontalVirtualizingLayout` で画面外カードを生成しない |
| **差分更新** | `LastFetchedTs` + `oldest` パラメータで前回以降の新着メッセージのみ取得 |
| **API リトライ** | `ExecuteWithRetryAsync()` で指数バックオフ + 429 レート制限対応（最大3回） |
| **カスタムボード永続化** | `CustomBoardStorageService` でボード設定を保存・復元 |

## 8. フロー全体の概略図

```
[タイマー/ユーザー操作]
        │
        ▼
RefreshWorkspaceDataAsync()
        │
        ├─→ Task.WhenAll: Emoji + Avatar 並列取得
        │
        ▼
LoadChannelsAsync() / RefreshActiveBoardChannelsAsync()（差分更新対応）
        │
        ├─→ Slack API 呼び出し（SemaphoreSlim(10) で並列制御、リトライ付き）
        │     conversations.list → conversations.history(oldest) → conversations.replies
        │     users.info（キャッシュ付き）/ emoji.list
        │
        ▼
MessageDisplayItem に変換（2パスパーサー・画像抽出・リアクション抽出）
        │
        ▼
ChannelWithMessages にまとめる（LastFetchedTs 記録）
        │
        ▼
BatchObservableCollection に挿入（ソート済み、バッチ通知）
        │
        ▼
ItemsRepeater + HorizontalVirtualizingLayout で仮想化描画
        │
        ▼
ChannelCardView（XAML テンプレート）→ MessageItemView（C# 直接構築）
        │
        ▼
MessageRenderContext で描画（BitmapImageCache + 事前解決済み絵文字）
```

## 9. 主要ファイル一覧

| ファイル | 役割 |
|---------|------|
| `MainWindow.xaml.cs` | データ取得・変換・UI更新の全体オーケストレーション |
| `MainWindow.xaml` | レイアウト定義（4つのボードビュー、ステータスパネル、メインコントローラー） |
| `Services/SlackService.cs` | Slack API ラッパー（チャンネル、メッセージ、ユーザー、絵文字）+ リトライ機構 |
| `Services/MessageRenderContext.cs` | メッセージ描画コンテキスト（RichText構築、アバター、リアクション、絵文字解決） |
| `Services/BitmapImageCache.cs` | URL→BitmapImage の WeakReference キャッシュ（DecodePixelWidth 制限付き） |
| `Views/ChannelBoardView.xaml(.cs)` | ItemsRepeater + HorizontalVirtualizingLayout による仮想化ボードコンテナ |
| `Views/HorizontalVirtualizingLayout.cs` | 水平方向の仮想化レイアウト（ビューポート内のみ実体化） |
| `Views/ChannelCardView.xaml(.cs)` | チャンネルカード（XAML テンプレート + コードビハインドで UpdateContent） |
| `Views/MessageItemView.cs` | メッセージ表示（C#コードで構築、MessageRenderContext 経由で描画） |
| `Models/ChannelWithMessages.cs` | チャンネル＋メッセージ＋差分更新用タイムスタンプ |
| `Models/MessageDisplayItem.cs` | パース済みメッセージ（セグメント、画像、リアクション、返信）+ PropertyInfo キャッシュ |
| `Models/MessageInlineSegment.cs` | テキストセグメント（タイプ、装飾、オプションURI） |
| `Models/BatchObservableCollection.cs` | バッチ更新対応 ObservableCollection（通知抑制 + Reset 1回） |
| `Services/CustomBoardStorageService.cs` | カスタムボード設定の永続化 |
