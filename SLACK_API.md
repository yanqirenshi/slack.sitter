# Slack API 利用一覧

本プロジェクト（SlackSitter）で利用している Slack API の一覧です。
API 呼び出しは `SlackSitter/Services/SlackService.cs` に集約されています。

## 利用ライブラリ

- **SlackNet** — API 1〜5 の呼び出しに使用（`ISlackApiClient` 経由）
- **HttpClient** — API 6 の呼び出しに使用（SlackNet 未対応のため直接 HTTP リクエスト）

## API 一覧

### 1. auth.test

| 項目 | 内容 |
|------|------|
| SlackNet メソッド | `_client.Auth.Test()` |
| 呼び出し箇所 | `SlackService.cs` — `AuthenticateAsync()`, `GetCurrentUserInfoAsync()` |
| 用途 | アクセストークンの検証、ワークスペース URL の取得、認証ユーザーの UserId 取得 |
| 戻り値の利用 | `authTest.Url`（ワークスペースURL）、`authTest.UserId`（ユーザーID） |

### 2. conversations.list

| 項目 | 内容 |
|------|------|
| SlackNet メソッド | `_client.Conversations.List()` |
| 呼び出し箇所 | `SlackService.cs` — `GetChannelsAsync()`, `GetChannelBatchesAsync()` |
| 用途 | ワークスペース内のチャンネル一覧を取得 |
| 主なパラメータ | `types`: PublicChannel + PrivateChannel、`excludeArchived`: true、`limit`: 100〜200 |
| ページネーション | カーソルベース（`ResponseMetadata.NextCursor`） |

### 3. conversations.history

| 項目 | 内容 |
|------|------|
| SlackNet メソッド | `_client.Conversations.History()` |
| 呼び出し箇所 | `SlackService.cs` — `GetChannelMessagesAsync()` |
| 用途 | 指定チャンネルのメッセージ履歴を取得 |
| 主なパラメータ | `channelId`: チャンネルID、`limit`: 10（デフォルト）、`oldestTs`: 差分取得用タイムスタンプ（オプション） |
| リトライ | `ExecuteWithRetryAsync()` で最大3回リトライ（指数バックオフ + 429 レート制限対応） |
| 備考 | 取得後、タイムスタンプ降順でソート。差分更新時は `oldestTs` を指定して前回以降の新着のみ取得 |

### 4. conversations.replies

| 項目 | 内容 |
|------|------|
| SlackNet メソッド | `_client.Conversations.Replies()` |
| 呼び出し箇所 | `SlackService.cs` — `GetThreadRepliesAsync()` |
| 用途 | スレッドの返信メッセージを取得 |
| 主なパラメータ | `channelId`: チャンネルID、`threadTs`: スレッドの親メッセージのタイムスタンプ、`limit`: 20（デフォルト） |
| リトライ | `ExecuteWithRetryAsync()` で最大3回リトライ（指数バックオフ + 429 レート制限対応） |
| 備考 | 親メッセージは結果から除外（`Ts != threadTs`）、タイムスタンプ昇順でソート |

### 5. users.info

| 項目 | 内容 |
|------|------|
| SlackNet メソッド | `_client.Users.Info()` |
| 呼び出し箇所 | `SlackService.cs` — `GetCurrentUserInfoAsync()`, `GetUserImageUrlAsync()` |
| 用途 | ユーザーのプロフィール情報（名前・アバター画像 URL）を取得 |
| 戻り値の利用 | `userInfo.Name`、`userInfo.Profile.Image192` / `Image72` / `Image48` / `Image32` |
| キャッシュ | `_userImageUrlCache`（Dictionary）でユーザーごとの画像 URL をキャッシュ |
| リトライ | `GetUserImageUrlAsync()` は `ExecuteWithRetryAsync()` で最大3回リトライ |

### 6. emoji.list

| 項目 | 内容 |
|------|------|
| 呼び出し方法 | `HttpClient` による直接 HTTP GET（`https://slack.com/api/emoji.list`） |
| 呼び出し箇所 | `SlackService.cs` — `GetCustomEmojiAsync()` |
| 用途 | ワークスペースのカスタム絵文字一覧を取得 |
| 認証 | Bearer トークンを Authorization ヘッダーに設定 |
| レスポンス解析 | `System.Text.Json` で手動パース（`ok`、`emoji` プロパティを参照） |
| リトライ | `ExecuteWithRetryAsync()` で最大3回リトライ。HTTP 429 検出時は `Retry-After` ヘッダ秒数を待機 |

## リトライ・レート制限対応

全 API に共通のリトライ機構 `ExecuteWithRetryAsync<T>()` が導入されている。

| 項目 | 内容 |
|------|------|
| 最大リトライ回数 | 3回 |
| バックオフ方式 | 指数バックオフ（1秒 → 2秒 → 4秒） |
| 429 対応 | `Retry-After` ヘッダ秒数 or デフォルト30秒を待機 |
| リトライ対象 | HTTP 429、SlackNet `ratelimited` 例外、`TaskCanceledException`、`HttpRequestException` |
| 適用 API | conversations.history、conversations.replies、users.info（画像取得）、emoji.list |
| 非適用 API | auth.test（認証）、conversations.list（初回のみ） |

## 必要な OAuth Scopes

ソースコード内のエラーハンドリングから推定される必要な権限：

| Scope | 用途 |
|-------|------|
| `channels:read` | パブリックチャンネル一覧の取得 |
| `channels:history` | パブリックチャンネルのメッセージ履歴取得 |
| `groups:read` | プライベートチャンネル一覧の取得 |
| `groups:history` | プライベートチャンネルのメッセージ履歴取得 |
| `mpim:read` | グループ DM の一覧取得 |
| `mpim:history` | グループ DM のメッセージ履歴取得 |
| `im:read` | DM の一覧取得 |
| `im:history` | DM のメッセージ履歴取得 |
| `users:read` | ユーザー情報の取得 |
| `chat:write` | メッセージの送信 |
| `emoji:read` | カスタム絵文字一覧の取得（emoji.list に必要） |
