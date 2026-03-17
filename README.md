# SlackSitter

`SlackSitter` は Slack の `#times*` チャンネルを横並びで閲覧するための WinUI 3 / .NET 8 デスクトップアプリです。

## 主な機能

- Slack User OAuth Token による認証
- `#times*` チャンネルの一覧取得
- メッセージ本文、画像、リアクションの表示
- 参加中 / 未参加チャンネルのフィルター切り替え
- ログ表示ポップアップ
- ユーザー情報ポップアップ
- 自動更新

## 技術スタック

- `.NET 8`
- `WinUI 3`
- `Windows App SDK`
- `SlackNet`

## プロジェクト構成

- `SlackSitter/MainWindow.xaml(.cs)`
  - 画面全体のオーケストレーション
- `SlackSitter/Views/`
  - `AuthenticationView`
  - `UserPopupView`
  - `LogPopupView`
  - `PopupBubbleView`
  - `CircleActionButtonView`
  - `MainControllerView`
  - `StatusPanelView`
  - `ChannelCardView`
  - `MessageItemView`
- `SlackSitter/Models/`
  - 表示用モデル群
- `SlackSitter/Services/`
  - Slack 通信、設定保存
- `SlackSitter/Converters/`
  - UI バインディング用 converter

モデルの整理方針は `SlackSitter/MODELS.md` を参照してください。

## セットアップ

### 1. 必要環境

- Windows
- Visual Studio 2026 以降相当の WinUI 3 開発環境
- .NET 8 SDK

### 2. リポジトリ取得

```powershell
git clone https://github.com/yanqirenshi/slack.sitter.git
cd slack.sitter
```

### 3. ビルド

```powershell
dotnet restore .\SlackSitter\SlackSitter.csproj
dotnet build .\SlackSitter\SlackSitter.csproj
```

### 4. 実行

Visual Studio で `SlackSitter.sln` を開いて実行してください。

## Slack 側で必要な権限

User Token Scopes に少なくとも以下が必要です。

- `channels:read`
- `channels:history`
- `groups:read`
- `groups:history`
- `mpim:read`
- `mpim:history`
- `im:read`
- `im:history`
- `users:read`
- `chat:write`
- `emoji:read`

## 現在の UI 構成メモ

下部コントローラは `MainControllerView` に集約済みです。

表示要素:
- 更新
- ユーザー
- 歯車
- `+`
- `①`
- `②`

補足:
- `①` / `②` は角丸四角
- 選択中の `①` / `②` は赤表示
- 歯車 / `+` は現状未実装でログ出力のみ

## 今後の候補

- `MessageDisplayItem` の変換責務を `Services` へ分離
- `DownloadedImageResult` の `Services` への移動
- 歯車 / `+` ボタンの実機能追加
- `SlackNet` 依存のさらなる隠蔽
