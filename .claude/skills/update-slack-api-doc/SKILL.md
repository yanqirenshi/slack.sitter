---
name: update-slack-api-doc
description: >
  SLACK_API.md ドキュメントを最新のソースコードに基づいて自動更新するスキル。
  SlackService.cs やその他のソースファイルをスキャンし、Slack API の利用状況（SlackNet ライブラリ経由・直接 HTTP 呼び出し両方）を検出して、
  SLACK_API.md に反映する。API の追加・削除・パラメータ変更・OAuth Scope の変更を検出する。
  このスキルは以下のような場面で使用する：
  - ユーザーが「SLACK_API.md を更新して」「Slack API ドキュメントを最新化して」と依頼したとき
  - ユーザーが「API ドキュメントをメンテして」「APIの一覧を更新して」と言ったとき
  - SlackService.cs に変更を加えた後に API ドキュメントの同期を求められたとき
  - 「Slack API の利用状況を確認・更新して」と依頼されたとき
---

# SLACK_API.md メンテナンススキル

SlackSitter プロジェクトの `SLACK_API.md` を、ソースコードの実際の Slack API 利用状況に基づいて正確に更新する。

## 手順

### Step 1: ソースコードをスキャンして API 呼び出しを収集する

以下の 2 種類の呼び出しパターンを検出する。

**パターン A: SlackNet ライブラリ経由の呼び出し**

`_client.<Category>.<Method>(...)` の形式を `.cs` ファイルから検索する。

```
_client.Auth.Test()           → auth.test
_client.Conversations.List()  → conversations.list
_client.Conversations.History() → conversations.history
_client.Conversations.Replies() → conversations.replies
_client.Users.Info()          → users.info
_client.Chat.PostMessage()    → chat.postMessage
```

SlackNet のカテゴリ名・メソッド名を Slack API 名にマッピングするルール：
- カテゴリ名をスネークケースに変換（例: `Conversations` → `conversations`）
- メソッド名をスネークケースに変換（例: `PostMessage` → `postMessage`）
- 両者をドットで結合（例: `conversations.list`）

**パターン B: HttpClient による直接呼び出し**

`https://slack.com/api/` を含む URL を検索する。URL のパス末尾が API 名になる。

```
https://slack.com/api/emoji.list → emoji.list
```

### Step 2: 各 API 呼び出しの詳細を収集する

検出した各 API 呼び出しについて、以下の情報をソースコードから読み取る：

1. **呼び出し方法**: SlackNet メソッド名 or HttpClient 直接呼び出し
2. **呼び出し箇所**: ファイル名とメソッド名（例: `SlackService.cs` — `GetChannelsAsync()`）
3. **用途**: メソッドの文脈から判断（メソッド名、コメント、変数名を参考にする）
4. **主なパラメータ**: メソッド呼び出し時に渡しているパラメータ名とデフォルト値
5. **備考**: ページネーション、キャッシュ、ソート、フィルタリングなどの特記事項
6. **戻り値の利用**: レスポンスからどのプロパティを利用しているか

### Step 3: OAuth Scopes を収集する

以下の方法で必要な OAuth Scopes を特定する：

1. ソースコード内の `missing_scope` エラーハンドリング箇所で明示されている Scope 一覧を確認する
2. 各 API が必要とする標準的な Scope を Slack 公式ドキュメントの知識に基づいて補完する
3. 例：`emoji.list` → `emoji:read`

### Step 4: 現在の SLACK_API.md と差分を取る

`SLACK_API.md` を読み込み、ソースコードのスキャン結果と比較して差分を特定する：

- **新規追加**: ソースにあるが SLACK_API.md にない API
- **削除**: SLACK_API.md にあるがソースから消えた API
- **変更**: パラメータ、呼び出し箇所、用途の変更

### Step 5: SLACK_API.md を更新する

以下のフォーマットで `SLACK_API.md` を更新する。既存のフォーマットを維持しつつ、変更箇所だけを反映する。

#### ドキュメント構成

```markdown
# Slack API 利用一覧

本プロジェクト（SlackSitter）で利用している Slack API の一覧です。
API 呼び出しは `SlackSitter/Services/SlackService.cs` に集約されています。

## 利用ライブラリ

- **SlackNet** — ... の呼び出しに使用（`ISlackApiClient` 経由）
- **HttpClient** — ... の呼び出しに使用（SlackNet 未対応のため直接 HTTP リクエスト）

## API 一覧

### N. <api.name>

| 項目 | 内容 |
|------|------|
| SlackNet メソッド（または 呼び出し方法） | ... |
| 呼び出し箇所 | ... |
| 用途 | ... |
| 主なパラメータ | ...（該当する場合） |
| 戻り値の利用 | ...（該当する場合） |
| ページネーション | ...（該当する場合） |
| キャッシュ | ...（該当する場合） |
| 備考 | ...（該当する場合） |

## 必要な OAuth Scopes

| Scope | 用途 |
|-------|------|
| `scope:name` | 説明 |
```

#### フォーマットルール

- API は番号付き見出し（`### N. api.name`）で列挙する
- 番号は 1 から連番
- テーブルの「項目」列は、その API に該当する情報のみ記載する（不要な行は省略）
- SlackNet 経由の呼び出しは「SlackNet メソッド」、HttpClient 直接呼び出しは「呼び出し方法」と表記する
- 利用ライブラリセクションの番号範囲は API 数に応じて更新する
- 日本語で記述する

### Step 6: 変更内容をユーザーに報告する

更新が完了したら、以下を報告する：

- 追加された API（あれば）
- 削除された API（あれば）
- 変更された項目（あれば）
- OAuth Scopes の変更（あれば）
- 変更がなかった場合は「SLACK_API.md は最新です」と報告する

## スキャン対象ファイル

メインのスキャン対象は `SlackSitter/Services/SlackService.cs` だが、以下のパターンで他のファイルにも API 呼び出しがないか確認する：

- `SlackSitter/**/*.cs` 内の `_client.` で始まる Slack API 呼び出し
- `SlackSitter/**/*.cs` 内の `slack.com/api/` を含む HTTP リクエスト

新たな呼び出し箇所が見つかった場合は、ドキュメントの冒頭説明文も適宜更新する（「SlackService.cs に集約」→「主に SlackService.cs で呼び出し」など）。
