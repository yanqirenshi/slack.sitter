# Models ドキュメント

## 現在のクラス一覧

| クラス / enum | ファイル | 種別 |
|---|---|---|
| `ChannelWithMessages` | `Models/ChannelWithMessages.cs` | 集約モデル |
| `MessageDisplayItem` | `Models/MessageDisplayItem.cs` | 表示モデル |
| `MessageInlineSegment` | `Models/MessageInlineSegment.cs` | 値オブジェクト |
| `MessageImageItem` | `Models/MessageImageItem.cs` | 値オブジェクト |
| `MessageReactionItem` | `Models/MessageReactionItem.cs` | 値オブジェクト |
| `MessageInlineSegmentType` | `Models/MessageInlineSegmentType.cs` | enum |
| `DownloadedImageResult` | `MainWindow.xaml.cs` (内部クラス) | ヘルパー |

---

## 各クラスの役割

### `ChannelWithMessages`

1 つの Slack チャンネルと、その表示対象メッセージ群をまとめる集約モデル。  
画面の「1 列分の表示データ」に対応する。

**プロパティ**

| プロパティ | 型 | 概要 |
|---|---|---|
| `Channel` | `SlackNet.Conversation` | Slack API から取得した生チャンネル |
| `Messages` | `List<MessageDisplayItem>` | 表示対象メッセージ |
| `ChannelUri` | `Uri?` | Slack の該当チャンネル URL |
| `Name` | `string` | チャンネル名 |
| `IsMember` | `bool` | 自分が参加しているか |
| `LastMessageTs` | `string?` | 最新メッセージのタイムスタンプ |
| `TopicValue` | `string?` | チャンネルトピック |
| `PurposeValue` | `string?` | チャンネル説明 |
| `NumMembers` | `int?` | メンバー数 |

---

### `MessageDisplayItem`

`SlackNet.Events.MessageEvent` を UI 表示用に変換したモデル。  
テキスト解析・画像抽出・リアクション抽出も内包している。

**プロパティ**

| プロパティ | 型 | 概要 |
|---|---|---|
| `User` | `string?` | 投稿者の UserId |
| `Ts` | `string?` | タイムスタンプ |
| `Text` | `string?` | 生テキスト |
| `UserAvatarUri` | `Uri?` | アバター画像 URL |
| `PermalinkUri` | `Uri?` | メッセージへの直リンク |
| `Segments` | `IReadOnlyList<MessageInlineSegment>` | パース済みインライン要素 |
| `Images` | `IReadOnlyList<MessageImageItem>` | 添付画像 |
| `Reactions` | `IReadOnlyList<MessageReactionItem>` | リアクション |

**内包しているロジック**

- Slack テキストのリンク解析 (`SlackLinkRegex`)
- 絵文字抽出 (`SlackEmojiRegex`)
- 画像 URL 抽出 (`Files`, `Attachments`, `Blocks` 対応)
- リアクション抽出

---

### `MessageInlineSegment`

メッセージ本文のインライン要素 1 個分を表す値オブジェクト。

| プロパティ | 型 | 概要 |
|---|---|---|
| `Type` | `MessageInlineSegmentType` | テキスト / リンク / 絵文字 |
| `Text` | `string` | 表示テキスト |
| `Uri` | `Uri?` | リンク先 (Type=Link の場合) |
| `IsBold` | `bool` | 太字 |
| `IsItalic` | `bool` | 斜体 |
| `IsStrikethrough` | `bool` | 取り消し線 |
| `IsCode` | `bool` | インラインコード |

---

### `MessageImageItem`

メッセージ内画像の候補 URL 群を保持する値オブジェクト。  
Slack の `Files` / `Attachments` / `Blocks` 由来の複数候補から最適なものを選ぶために使う。

| プロパティ | 型 | 概要 |
|---|---|---|
| `CandidateUrls` | `IReadOnlyList<string>` | 画像候補 URL 一覧 |

---

### `MessageReactionItem`

リアクション 1 個分の表示データ。

| プロパティ | 型 | 概要 |
|---|---|---|
| `Name` | `string` | 絵文字名 |
| `Count` | `int` | リアクション数 |

---

### `DownloadedImageResult` (内部クラス)

`MainWindow.xaml.cs` 内に定義されている画像ダウンロード結果のヘルパークラス。

| プロパティ | 型 | 概要 |
|---|---|---|
| `Bytes` | `byte[]` | 画像バイト列 |
| `ContentType` | `string?` | MIME タイプ |
| `FinalUri` | `string?` | リダイレクト後の最終 URL |

---

## 依存関係

```
MainWindow.xaml.cs
├── ChannelWithMessages
│   ├── SlackNet.Conversation          ← 外部ライブラリ
│   └── MessageDisplayItem
│       ├── SlackNet.Events.MessageEvent ← 外部ライブラリ
│       ├── MessageInlineSegment
│       │   └── MessageInlineSegmentType
│       ├── MessageImageItem
│       └── MessageReactionItem
└── DownloadedImageResult              ← 内部クラス (MainWindow に混在)
```

---

## 現在の問題点

### 1. `MessageDisplayItem` に責務が集まりすぎ

現状やっていること:

- Slack テキストの解析
- リンク・絵文字の変換
- 画像 URL の抽出 (Files / Attachments / Blocks)
- リアクション抽出
- Permalink URI の生成

これは **表示モデル + 変換サービス** が混在している状態。

---

### 2. ~~`MessageDisplayItem.cs` に複数クラスが同居~~ ✅ 解決済み

各クラスを独立ファイルに分割済み。

---

### 3. `ChannelWithMessages` が `SlackNet.Conversation` を直接保持

UI 表示モデルが外部ライブラリの型に直接依存している。  
将来 Slack API ライブラリを変更した場合に影響範囲が広い。

---

### 4. `DownloadedImageResult` が `MainWindow.xaml.cs` に混在

本来は `Services` 層に属すべきヘルパークラスが View コードに置かれている。

---

## 整理案

### 案A: ファイル分割のみ ✅ 完了

クラスの中身は変えず、1 ファイル 1 クラスに分割した。

**対象**

| ファイル | クラス |
|---|---|
| `Models/MessageInlineSegmentType.cs` | `MessageInlineSegmentType` |
| `Models/MessageInlineSegment.cs` | `MessageInlineSegment` |
| `Models/MessageImageItem.cs` | `MessageImageItem` |
| `Models/MessageReactionItem.cs` | `MessageReactionItem` |

> `Services/DownloadedImageResult.cs` への移動は未対応 (問題点4)

---

### 案B: 変換ロジックをサービスへ分離 (推奨)

案A に加え、`MessageDisplayItem` 内の変換処理を専用サービスに移す。

**新規クラス**

| クラス | 場所 | 責務 |
|---|---|---|
| `SlackMessageParser` | `Services/` | テキスト解析・セグメント変換 |
| `SlackMessageAssetExtractor` | `Services/` | 画像・リアクション抽出 |
| `MessageDisplayItemFactory` | `Services/` | `MessageEvent → MessageDisplayItem` の組み立て |

**効果**: `MessageDisplayItem` が純粋な表示データになる / 単体テストが書きやすくなる

---

### 案C: `SlackNet` 依存を隠蔽 (将来対応)

`ChannelWithMessages` が `SlackNet.Conversation` を直接保持しないよう、  
チャンネル情報を表す独自モデルを導入する。

**新規クラス**

| クラス | 場所 | 概要 |
|---|---|---|
| `ChannelInfo` | `Models/` | `Conversation` の必要プロパティのみ保持 |

**効果**: 外部ライブラリ変更時の影響を `Services` 層に閉じ込められる

---

## 推奨実施順

```
Step 1: 案A  → ファイル分割のみ              ✅ 完了
Step 2: 案B  → 変換ロジックをサービスへ移動  🔲 未対応
Step 3: 案C  → 必要なら SlackNet 依存の隠蔽  🔲 未対応
```
