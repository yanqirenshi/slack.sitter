# HANDOFF

## 現在の状態

- ブランチ: `master`
- 主要変更は `origin/master` へ push 済み
- ターゲット: `.NET 8`, `WinUI 3`

## 直近の整理内容

### View 分割

`MainWindow` から以下を切り出し済みです。

- `AuthenticationView`
- `UserPopupView`
- `LogPopupView`
- `PopupBubbleView`
- `MessageItemView`
- `ChannelCardView`
- `CircleActionButtonView`
- `MainControllerView`
- `StatusPanelView`

### MainControllerView

画面下部のコントローラ群は `MainControllerView` に集約済みです。

対象:
- 更新
- ユーザー
- 歯車
- `+`
- `①`
- `②`

`MainWindow.xaml` では下部ボタンを個別に持たず、`MainControllerView` を 1 個だけ配置しています。

`MainControllerView.xaml.cs` 側に意図ベース API を持たせています。

- `ShowLoadingIndicatorBusy()`
- `SetLoadingIndicatorIdle()`
- `SetUserAvatarImage(...)`
- `ShowUserActionButtons()`
- `HideUserActionButtons()`
- `Reset()`
- `SetFilterButtonState(...)`

### StatusPanelView

中央のステータス表示も `StatusPanelView` へ切り出し済みです。

API:
- `ShowLoadingMessage(...)`
- `HidePanel()`
- `ShowWarningMessage(...)`
- `ShowErrorMessage(...)`

## UI の現状

### 下部コントローラ

並び:
1. 更新
2. ユーザー
3. 歯車
4. `+`
5. `①`
6. `②`

補足:
- `①` / `②` は角丸四角
- 選択中のフィルターボタンは赤表示
- 更新中インジケーターは赤表示
- 歯車 / `+` はまだ未実装でログ出力のみ

### Popup 系

- `UserPopupView` と `LogPopupView` は `PopupBubbleView` を利用
- ポップアップ背景は `App.xaml` の独自不透明ブラシを利用
  - `OpaquePopupBackgroundBrush`
  - `OpaquePopupBorderBrush`

## 既知の注意点

### XAML UserControl の明示登録

このプロジェクトでは一部 `UserControl` を `csproj` に明示登録しています。
追加済み:
- `Views/PopupBubbleView.xaml`
- `Views/MainControllerView.xaml`
- `Views/StatusPanelView.xaml`

`SlackSitter.csproj` に以下のような `Page Include` / `None Update` が必要です。
新しい XAML `UserControl` を追加したときは同様の登録が必要になる可能性があります。

### モデル層の課題

`SlackSitter/MODELS.md` に整理済みです。
主な未対応項目:
- `MessageDisplayItem` の責務分離
- `DownloadedImageResult` の `Services` への移動
- `SlackNet` 依存の隠蔽

## 次に着手しやすい候補

### 1. 歯車ボタンの実装

現状:
- `MainController_GearIconClick`
- ログ出力のみ

候補:
- 設定ポップアップ
- 表示設定
- フィルター設定

### 2. `+` ボタンの実装

現状:
- `MainController_PlusIconClick`
- ログ出力のみ

候補:
- 表示チャンネル追加
- チャンネル検索
- カスタムフィルター追加

### 3. Message 系の責務分離

`MODELS.md` の案B。
推奨クラス:
- `SlackMessageParser`
- `SlackMessageAssetExtractor`
- `MessageDisplayItemFactory`

## 別 PC での開始手順

```powershell
git clone https://github.com/yanqirenshi/slack.sitter.git
cd slack.sitter
dotnet restore .\SlackSitter\SlackSitter.csproj
dotnet build .\SlackSitter\SlackSitter.csproj
```

Visual Studio で開く場合:
- `SlackSitter.sln` を開く
- 必要なら `bin` / `obj` / `.vs` を再生成

## 補足

`.github/copilot-instructions.md` にあるルール:
- 読みづらくなる UI サイズ変更は戻す
- 下部アイコンコントローラ名は `BottomControllerView` ではなく `MainControllerView`
