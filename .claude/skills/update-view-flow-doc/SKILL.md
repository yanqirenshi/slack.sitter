---
name: update-view-flow-doc
description: >
  VIEW_FLOW.md ドキュメントを最新のソースコードに基づいて自動更新するスキル。
  MainWindow.xaml.cs、Views/、Models/ 配下のソースファイルをスキャンし、
  データ取得トリガー、データフロー、データ変換、コレクション管理、UIレンダリング階層、
  キャッシュ・最適化の情報を検出して VIEW_FLOW.md に反映する。
  このスキルは以下のような場面で使用する：
  - ユーザーが「VIEW_FLOW.md を更新して」「ビューフローのドキュメントを最新化して」と依頼したとき
  - ユーザーが「表示ロジックのドキュメントをメンテして」「ボード表示フローを更新して」と言ったとき
  - MainWindow.xaml.cs や Views/ 配下のファイルに変更を加えた後にドキュメントの同期を求められたとき
  - 「データフローの図を更新して」「UI構成のドキュメントを直して」と依頼されたとき
---

# VIEW_FLOW.md メンテナンススキル

SlackSitter プロジェクトの `VIEW_FLOW.md` を、ソースコードの実際のデータ取得・変換・表示ロジックに基づいて正確に更新する。

## 手順

### Step 1: ソースコードをスキャンして構造変更を検出する

以下のファイル群を読み込み、VIEW_FLOW.md の各セクションに対応する情報を収集する。

**スキャン対象ファイル：**

| ファイル | 収集する情報 |
|---------|------------|
| `MainWindow.xaml.cs` | データ取得トリガー、Refresh系メソッド、Load系メソッド、コレクション管理、Loadedイベントハンドラ |
| `MainWindow.xaml` | UI レンダリング階層、ビューの配置構成 |
| `Services/SlackService.cs` | API 呼び出しメソッドとそのパラメータ |
| `Views/*.xaml` / `Views/*.cs` | ビューコンポーネントの構成と役割 |
| `Models/*.cs` | データモデルのプロパティ構成 |
| `Services/CustomBoardStorageService.cs` | カスタムボード永続化ロジック |

### Step 2: 各セクションの情報を収集する

VIEW_FLOW.md は以下の9セクションで構成される。各セクションに対応する情報をソースコードから抽出する。

#### セクション 1: データ取得のトリガー

検出パターン：
- `DispatcherTimer` の設定（間隔、Tick ハンドラ）
- `RefreshWorkspaceDataAsync()` や `RefreshActiveBoardChannelsAsync()` を呼び出しているイベントハンドラ
- ユーザー操作起点のメソッド（ボタンクリック、トークン更新など）

#### セクション 2: データ取得フロー

検出パターン：
- `RefreshWorkspaceDataAsync()` の内部構造（呼び出しているサブメソッド）
- `LoadChannelsAsync()` / `LoadChannelBatchAsync()` の処理フロー
- `SemaphoreSlim` の並列数設定
- SlackService のメソッド呼び出しとそのパラメータ（limit値など）
- チャンネルフィルタリング条件（"times" プレフィックスなど）

#### セクション 3: データ変換レイヤー

検出パターン：
- `MessageDisplayItem` のコンストラクタとプロパティ
- `ParseSegments()` のパースロジック（対応するフォーマット種類）
- `ExtractImages()` の画像抽出ロジック
- `ExtractReactions()` のリアクション抽出ロジック
- `ChannelWithMessages` のプロパティ構成

#### セクション 4: コレクション管理

検出パターン：
- `ObservableCollection` のフィールド定義
- `InsertDisplayedChannel()` / `CompareChannels()` のソートロジック
- `_currentChannelDisplayFilter` のフィルタ条件
- フィルタ切替メソッド

#### セクション 5: UI レンダリング階層

検出パターン：
- `MainWindow.xaml` 内の View コンポーネント配置
- 各 View の XAML テンプレート構造（ItemsControl、DataTemplate など）
- `ChannelBoardView` / `ChannelCardView` / `MessageItemView` の構造

#### セクション 6: 動的コンテンツ描画

検出パターン：
- `_Loaded` イベントハンドラ（`MessageRichTextBlock_Loaded` など）
- `_Click` イベントハンドラ（画像表示ボタンなど）
- 各ハンドラが行うUI操作（RichTextBlock への Inline 挿入、ImageBrush 設定など）

#### セクション 7: キャッシュ・最適化

検出パターン：
- キャッシュ用 Dictionary フィールド
- `SemaphoreSlim` による並列制御
- `IAsyncEnumerable` によるストリーミング処理
- 条件付き取得（reply_count チェックなど）
- 遅延読込パターン

#### セクション 8: フロー全体の概略図

セクション 1〜7 の情報を総合してフロー図を更新する。主要な処理の流れに変更があった場合のみ図を修正する。

#### セクション 9: 主要ファイル一覧

スキャン対象ファイルの一覧を更新する。新規ファイルの追加や、ファイルの役割変更を反映する。

### Step 3: 現在の VIEW_FLOW.md と差分を取る

`VIEW_FLOW.md` を読み込み、ソースコードのスキャン結果と比較して差分を特定する：

- **追加**: 新しいトリガー、データモデル、Viewコンポーネント、最適化手法
- **削除**: 削除されたメソッド、コンポーネント、モデルプロパティ
- **変更**: パラメータ値の変更、処理フローの変更、UI階層の変更

### Step 4: VIEW_FLOW.md を更新する

既存のフォーマットとセクション構成を維持しつつ、変更箇所のみを反映する。

#### フォーマットルール

- セクション番号は1〜9の連番を維持する
- 図やツリー構造はテキストベースの ASCII アートで表現する
- テーブルは Markdown テーブル形式で記述する
- メソッド名やクラス名はバッククォートで囲む
- 日本語で記述する
- 新しいセクションが必要な場合は、既存セクションの後に追加し番号を振り直す

### Step 5: 変更内容をユーザーに報告する

更新が完了したら、以下を報告する：

- 追加された要素（新しいトリガー、コンポーネント、最適化など）
- 削除された要素
- 変更された項目（パラメータ変更、フロー変更など）
- 変更がなかった場合は「VIEW_FLOW.md は最新です」と報告する
