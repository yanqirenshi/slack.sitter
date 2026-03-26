# SlackSitter ボード表示ロジック・フロー

Slack からデータを取得し、ボード UI に表示するまでの全体フローを整理したドキュメントです。

## 1. データ取得のトリガー

| トリガー | 起点 | 呼ばれるメソッド |
|---------|------|----------------|
| **初回認証成功** | `AuthenticateWithToken()` | → `RefreshWorkspaceDataAsync()` |
| **自動更新タイマー** | `_autoRefreshTimer`（5分間隔の `DispatcherTimer`） | → `RefreshWorkspaceDataAsync()` |
| **手動リフレッシュ** | ログポップアップのリフレッシュボタン | → `RefreshActiveBoardChannelsAsync()` |
| **トークン更新** | ユーザーポップアップでトークン変更 | → `RefreshWorkspaceDataAsync()` |
| **カスタムボード作成/編集** | チャンネル追加リクエスト | → `MainController_AddChannelsRequested()` |

## 2. データ取得フロー

```
RefreshWorkspaceDataAsync()
 ├── LoadCustomEmojiAsync()     ← emoji.list API
 │     → _customEmojiMap に格納（エイリアス解決付き）
 │
 ├── LoadUserAvatarAsync()      ← auth.test → users.info API
 │     → 現在のユーザー名・アバター画像を取得
 │
 └── LoadChannelsAsync()        ← conversations.list API（バッチ取得）
       │
       ├── GetChannelBatchesAsync() で100件ずつチャンネル取得
       │     → "times" プレフィックスのチャンネルをフィルタ
       │
       └── LoadChannelBatchAsync()（バッチごとに並列処理）
             │  SemaphoreSlim(4) で最大4並列に制限
             │
             ├── GetChannelMessagesAsync(channelId, 10)
             │     ← conversations.history API（最新10件）
             │
             ├── GetThreadRepliesAsync(channelId, threadTs)
             │     ← conversations.replies API（reply_count > 0 のみ）
             │
             └── GetUserImageUrlAsync(userId)
                   ← users.info API（キャッシュ付き）
```

## 3. データ変換レイヤー

生の Slack データを表示用モデルに変換する。

```
SlackNet.Events.MessageEvent
        ↓ 変換
MessageDisplayItem
 ├── Segments: IReadOnlyList<MessageInlineSegment>
 │     ParseSegments() でテキストを解析：
 │     ├── プレーンテキスト（太字/斜体/取消線/コード装飾）
 │     ├── リンク（<url|label> パターン）
 │     └── 絵文字（:name: パターン）
 │
 ├── Images: IReadOnlyList<MessageImageItem>
 │     ExtractImages() で画像URL抽出：
 │     ├── Files（mimetype が image/* のもの）
 │     └── Attachments / Blocks
 │
 ├── Reactions: IReadOnlyList<MessageReactionItem>
 │     ExtractReactions() でリアクション取得（名前＋件数）
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
 └── Messages: List<MessageDisplayItem>（表示用メッセージ一覧）
```

## 4. コレクション管理

4つの `ObservableCollection` でチャンネルを管理し、フィルタで切り替える。

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

## 5. UI レンダリング階層

```
MainWindow.xaml
 │
 ├── StatusPanelView         ← 状態表示（認証状態、エラー等）
 ├── MainControllerView      ← フィルタ切替、カスタムボード管理
 │
 └── ChannelBoardView × 4    ← フィルタごとに1つずつ
       │  ItemsControl + 横スクロール StackPanel
       │
       └── ChannelCardView（チャンネルカード）
             ├── ヘッダー（チャンネル名 + トピック）
             └── ScrollViewer
                   └── StackPanel（メッセージ一覧）
                         │
                         └── MessageItemView（メッセージ1件）
                               ├── タイムスタンプ（Hyperlink → Slack直リンク）
                               ├── アバター画像（ImageBrush）
                               ├── メッセージ本文カード
                               │    ├── RichTextBlock（装飾テキスト）
                               │    └── 画像スタック（ボタンクリックで遅延読込）
                               ├── リアクション表示
                               └── スレッド返信（MessageItemView を再帰表示）
```

## 6. 動的コンテンツ描画

UI 要素の `Loaded` イベントで動的にコンテンツを組み立てる。

| イベント | 処理内容 |
|---------|---------|
| `MessageRichTextBlock_Loaded` | `Segments` をもとに `Run`（テキスト）, `Hyperlink`（リンク）, カスタム絵文字画像を RichTextBlock に挿入 |
| `MessageAvatarBorder_Loaded` | ユーザーアバターを `ImageBrush` で背景に設定 |
| `ReactionBorder_Loaded` | `_customEmojiMap` から絵文字画像を解決し、件数とともに表示 |
| `ShowMessageImageButton_Click` | 画像を遅延読込（候補URLを優先順に試行、リダイレクト対応、Content-Type検証） |

## 7. キャッシュ・最適化

| 最適化 | 詳細 |
|--------|------|
| **ユーザー画像キャッシュ** | `_userImageUrlCache`（Dictionary）で userId → 画像URL をキャッシュ |
| **並列メッセージ取得** | `SemaphoreSlim(4)` で最大4チャンネル同時取得 |
| **チャンネルバッチ取得** | `IAsyncEnumerable` で100件ずつ yield、到着順に即UI反映 |
| **スレッド取得の条件分岐** | `reply_count > 0` のメッセージのみスレッド取得 |
| **画像の遅延読込** | ボタンクリック時にのみHTTP取得（初回表示のコスト削減） |
| **カスタム絵文字のエイリアス解決** | `alias:xxx` チェーンを事前に解決済みURLに変換 |
| **カスタムボード永続化** | `CustomBoardStorageService` でボード設定を保存・復元 |

## 8. フロー全体の概略図

```
[タイマー/ユーザー操作]
        │
        ▼
RefreshWorkspaceDataAsync()
        │
        ├─→ Slack API 呼び出し（並列）
        │     conversations.list → conversations.history → conversations.replies
        │     users.info（キャッシュ付き）/ emoji.list
        │
        ▼
MessageDisplayItem に変換（パース・画像抽出・リアクション抽出）
        │
        ▼
ChannelWithMessages にまとめる
        │
        ▼
ObservableCollection に挿入（ソート済み）
        │
        ▼
データバインディングで UI 自動更新
        │
        ▼
ChannelBoardView → ChannelCardView → MessageItemView
        │
        ▼
Loaded イベントで動的描画（RichText・アバター・絵文字・リアクション）
```

## 9. 主要ファイル一覧

| ファイル | 役割 |
|---------|------|
| `MainWindow.xaml.cs` | データ取得・変換・UI更新の全体オーケストレーション |
| `MainWindow.xaml` | レイアウト定義（4つのボードビュー、ステータスパネル、メインコントローラー） |
| `Services/SlackService.cs` | Slack API ラッパー（チャンネル、メッセージ、ユーザー、絵文字） |
| `Views/ChannelBoardView.xaml(.cs)` | 横スクロール可能なボードコンテナ |
| `Views/ChannelCardView.cs` | チャンネルカード（タイトル＋メッセージ一覧） |
| `Views/MessageItemView.cs` | メッセージ表示（アバター、テキスト、リアクション、スレッド） |
| `Models/ChannelWithMessages.cs` | チャンネル＋メッセージのバインディングオブジェクト |
| `Models/MessageDisplayItem.cs` | パース済みメッセージ（セグメント、画像、リアクション、返信） |
| `Models/MessageInlineSegment.cs` | テキストセグメント（タイプ、装飾、オプションURI） |
| `Services/CustomBoardStorageService.cs` | カスタムボード設定の永続化 |
