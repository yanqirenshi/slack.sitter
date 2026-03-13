using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using SlackSitter.Services;
using SlackSitter.Models;

namespace SlackSitter
{
    public sealed partial class MainWindow : Window
    {
        private static readonly Regex SlackLinkRegex = new Regex(@"<(?<url>https?://[^|>]+)(\|(?<label>[^>]+))?>", RegexOptions.Compiled);
        private static readonly Regex SlackEmojiRegex = new Regex(@":(?<name>[a-zA-Z0-9_+\-]+):", RegexOptions.Compiled);
        private readonly SlackService _slackService;
        private readonly SettingsService _settingsService;
        private string? _currentUserId;
        private string? _currentUserName;
        private ObservableCollection<ChannelWithMessages> _channelsWithMessages;
        private ObservableCollection<string> _logMessages;
        private Dictionary<string, string> _customEmojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();
            _slackService = new SlackService();
            _settingsService = new SettingsService();
            _channelsWithMessages = new ObservableCollection<ChannelWithMessages>();
            _logMessages = new ObservableCollection<string>();
            LogItemsControl.ItemsSource = _logMessages;

            AddLog($".env file path: {_settingsService.GetEnvFilePath()}");

            LoadSettingsAndAuthenticate();
        }

        private void ChannelScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TimesChannelsItemsControl.ItemsPanelRoot is StackPanel panel)
            {
                var scrollViewer = sender as ScrollViewer;
                if (scrollViewer != null)
                {
                    var availableHeight = scrollViewer.ActualHeight - 40;
                    panel.Height = availableHeight;

                    for (int i = 0; i < TimesChannelsItemsControl.Items.Count; i++)
                    {
                        var container = TimesChannelsItemsControl.ContainerFromIndex(i);
                        if (container != null)
                        {
                            if (VisualTreeHelper.GetChild(container, 0) is Border border)
                            {
                                border.Height = availableHeight;
                            }
                        }
                    }
                }
            }
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logMessages.Add($"[{timestamp}] {message}");
        }

        private static double ParseSlackTimestamp(string? timestamp)
        {
            return double.TryParse(timestamp, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : double.MinValue;
        }

        private void RefreshDisplayedChannels(IEnumerable<ChannelWithMessages> channels)
        {
            var sortedChannels = channels
                .OrderByDescending(c => c.IsMember)
                .ThenByDescending(c => ParseSlackTimestamp(c.LastMessageTs))
                .ThenBy(c => c.Name)
                .ToList();

            _channelsWithMessages.Clear();

            foreach (var channel in sortedChannels)
            {
                _channelsWithMessages.Add(channel);
            }
        }

        private void MessageRichTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not RichTextBlock richTextBlock)
            {
                return;
            }

            var sourceText = richTextBlock.Tag as string ?? string.Empty;
            richTextBlock.Blocks.Clear();

            var paragraph = new Paragraph();
            var currentIndex = 0;

            foreach (Match match in SlackLinkRegex.Matches(sourceText))
            {
                if (match.Index > currentIndex)
                {
                    AppendTextInline(paragraph, sourceText.Substring(currentIndex, match.Index - currentIndex));
                }

                var url = match.Groups["url"].Value;
                var label = match.Groups["label"].Success ? match.Groups["label"].Value : url;

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var hyperlink = new Hyperlink
                    {
                        NavigateUri = uri
                    };
                    hyperlink.Inlines.Add(new Run { Text = label });
                    paragraph.Inlines.Add(hyperlink);
                }
                else
                {
                    AppendTextInline(paragraph, label);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < sourceText.Length)
            {
                AppendTextInline(paragraph, sourceText.Substring(currentIndex));
            }

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new Run { Text = string.Empty });
            }

            richTextBlock.Blocks.Add(paragraph);
        }

        private void AppendTextInline(Paragraph paragraph, string text)
        {
            var currentIndex = 0;

            foreach (Match match in SlackEmojiRegex.Matches(text))
            {
                if (match.Index > currentIndex)
                {
                    AppendPlainTextInline(paragraph, text.Substring(currentIndex, match.Index - currentIndex));
                }

                var emojiName = match.Groups["name"].Value;
                if (!AppendEmojiInline(paragraph, emojiName))
                {
                    AppendPlainTextInline(paragraph, match.Value);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                AppendPlainTextInline(paragraph, text.Substring(currentIndex));
            }
        }

        private void AppendPlainTextInline(Paragraph paragraph, string text)
        {
            var normalizedText = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalizedText.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    paragraph.Inlines.Add(new Run { Text = lines[i] });
                }

                if (i < lines.Length - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }
        }

        private bool AppendEmojiInline(Paragraph paragraph, string emojiName)
        {
            var emojiUrl = ResolveEmojiUrl(emojiName);
            if (string.IsNullOrEmpty(emojiUrl) || !Uri.TryCreate(emojiUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var image = new Image
            {
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(1, 0, 1, -2),
                Source = new BitmapImage(uri)
            };

            paragraph.Inlines.Add(new InlineUIContainer { Child = image });
            return true;
        }

        private string? ResolveEmojiUrl(string emojiName)
        {
            if (!_customEmojiMap.TryGetValue(emojiName, out var emojiValue) || string.IsNullOrWhiteSpace(emojiValue))
            {
                return null;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentValue = emojiValue;
            var currentName = emojiName;

            while (currentValue.StartsWith("alias:", StringComparison.OrdinalIgnoreCase))
            {
                if (!visited.Add(currentName))
                {
                    return null;
                }

                currentName = currentValue.Substring("alias:".Length);
                if (!_customEmojiMap.TryGetValue(currentName, out currentValue) || string.IsNullOrWhiteSpace(currentValue))
                {
                    return null;
                }
            }

            return currentValue;
        }

        private async System.Threading.Tasks.Task LoadCustomEmojiAsync()
        {
            var result = await _slackService.GetCustomEmojiAsync();
            _customEmojiMap = result.EmojiMap;

            if (_customEmojiMap.Count > 0)
            {
                AddLog($"カスタム絵文字を {_customEmojiMap.Count} 件取得");
            }
            else if (!string.IsNullOrWhiteSpace(result.Error))
            {
                AddLog($"カスタム絵文字を取得できませんでした: {result.Error}");

                if (result.Error.Contains("missing_scope", StringComparison.OrdinalIgnoreCase))
                {
                    AddLog("カスタム絵文字表示には Slack の OAuth Scope に emoji:read が必要です");
                }
            }
        }

        private void LoadingIndicatorButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogPopupBorder.Visibility == Visibility.Visible)
            {
                LogPopupBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                LogPopupBorder.Visibility = Visibility.Visible;
                UserPopupBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async void LoadSettingsAndAuthenticate()
        {
            var settings = await _settingsService.LoadSettingsAsync();

            if (!string.IsNullOrEmpty(settings.AccessToken))
            {
                AccessTokenPasswordBox.Password = settings.AccessToken;
                await AuthenticateWithToken(settings.AccessToken);
            }
            else
            {
                AuthenticationPanel.Visibility = Visibility.Visible;
            }
        }

        private async void AuthenticateButton_Click(object sender, RoutedEventArgs e)
        {
            var accessToken = AccessTokenPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                AuthenticationStatusText.Text = "User OAuth Token を入力してください";
                AuthenticationStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                return;
            }

            AuthenticateButton.IsEnabled = false;
            AuthenticationStatusText.Text = "認証中...";
            AuthenticationStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);

            await AuthenticateWithToken(accessToken);

            AuthenticateButton.IsEnabled = true;
        }

        private async System.Threading.Tasks.Task AuthenticateWithToken(string accessToken)
        {
            var success = await _slackService.AuthenticateAsync(accessToken);

            if (success)
            {
                var settings = new SettingsService.Settings
                {
                    AccessToken = accessToken
                };
                await _settingsService.SaveSettingsAsync(settings);

                AuthenticationPanel.Visibility = Visibility.Collapsed;
                MainPanel.Visibility = Visibility.Visible;
                AuthenticationStatusText.Text = string.Empty;

                EnvPathText.Text = $".env ファイルの保存先: {_settingsService.GetEnvFilePath()}";

                await LoadCustomEmojiAsync();
                await LoadUserAvatarAsync();
                await LoadChannelsAsync();
            }
            else
            {
                AuthenticationStatusText.Text = "認証に失敗しました。トークンを確認してください。";
                AuthenticationStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            }
        }

        private async System.Threading.Tasks.Task LoadChannelsAsync()
        {
            AddLog("=== チャンネル一覧の取得開始 ===");

            // データ取得中インジケーターを赤色に設定
            LoadingIndicatorButton.Visibility = Visibility.Visible;
            LoadingIndicator.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
            LoadingIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);

            StatusText.Text = "チャンネル一覧を読み込み中...";
            StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            ErrorText.Visibility = Visibility.Collapsed;
            RequiredScopesPanel.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Visible;

            try
            {
                _channelsWithMessages.Clear();
                TimesChannelsItemsControl.ItemsSource = _channelsWithMessages;

                var totalChannelsCount = 0;
                var totalTimesChannelsCount = 0;
                var tempChannelsList = new List<ChannelWithMessages>();

                await foreach (var channelBatch in _slackService.GetChannelBatchesAsync(100))
                {
                    totalChannelsCount += channelBatch.Count;
                    AddLog($"チャンネルを {channelBatch.Count} 件取得 (累計: {totalChannelsCount} 件)");

                    var timesChannelBatch = channelBatch
                        .Where(c => c.Name != null && c.Name.StartsWith("times") && !c.IsArchived)
                        .ToList();

                    totalTimesChannelsCount += timesChannelBatch.Count;

                    if (timesChannelBatch.Count == 0)
                    {
                        continue;
                    }

                    StatusText.Text = "メッセージを読み込み中...";
                    StatusPanel.Visibility = Visibility.Collapsed;

                    foreach (var channel in timesChannelBatch)
                    {
                        var messages = await _slackService.GetChannelMessagesAsync(channel.Id, 10);
                        var displayMessages = messages
                            .Select(message => new MessageDisplayItem(message, channel.Id, _slackService.GetWorkspaceUrl()))
                            .ToList();
                        var channelWithMessages = new ChannelWithMessages(channel, displayMessages, _slackService.GetWorkspaceUrl());
                        tempChannelsList.Add(channelWithMessages);

                        AddLog($"チャンネル #{channel.Name}: {messages.Count} 件のメッセージを取得");
                    }

                    RefreshDisplayedChannels(tempChannelsList);
                }

                AddLog($"取得したチャンネル数: {totalChannelsCount}");
                AddLog($"#times* チャンネル数: {totalTimesChannelsCount}");

                if (totalChannelsCount == 0)
                {
                    StatusText.Text = "";
                    ErrorText.Text = "⚠️ チャンネルの取得に失敗しました。トークンの権限を確認してください。";
                    ErrorText.Visibility = Visibility.Visible;
                    RequiredScopesPanel.Visibility = Visibility.Visible;
                    AddLog("チャンネルが取得できませんでした。権限を確認してください。");

                    // インジケーターをグレーに設定
                    LoadingIndicator.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    LoadingIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                }
                else if (totalTimesChannelsCount > 0)
                {
                    AddLog("=== チャンネル情報の取得完了 ===");

                    // データ取得完了 - インジケーターをグレーに変更
                    LoadingIndicator.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    LoadingIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    AddLog("=== チャンネル一覧の取得完了 ===");
                }
                else
                {
                    StatusText.Text = "⚠️ #times* で始まるチャンネルが見つかりませんでした";
                    StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
                    StatusPanel.Visibility = Visibility.Visible;

                    LoadingIndicator.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    LoadingIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);

                    AddLog("=== チャンネル一覧の取得完了 ===");
                }
            }
            catch (Exception ex)
            {
                AddLog($"チャンネル取得中にエラーが発生: {ex.Message}");
                StatusText.Text = "";
                ErrorText.Text = $"⚠️ エラーが発生しました: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;

                if (ex.Message.Contains("missing_scope") || ex.Message.Contains("権限"))
                {
                    RequiredScopesPanel.Visibility = Visibility.Visible;
                }

                // エラー時はインジケーターをグレーに設定
                LoadingIndicator.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                LoadingIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
        }

        private async System.Threading.Tasks.Task LoadUserAvatarAsync()
        {
            try
            {
                var (userId, userName, userImageUrl) = await _slackService.GetCurrentUserInfoAsync();

                if (!string.IsNullOrEmpty(userImageUrl))
                {
                    AddLog($"ユーザーアイコンを読み込み中: {userName} ({userId})");

                    _currentUserId = userId;
                    _currentUserName = userName;

                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(userImageUrl));
                    UserAvatarBrush.ImageSource = bitmap;
                    PopupAvatarBrush.ImageSource = bitmap;
                    UserAvatarButton.Visibility = Visibility.Visible;

                    PopupUserNameText.Text = userName ?? "Unknown";
                    PopupUserIdText.Text = userId ?? "";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ユーザーアイコンの取得に失敗しました");
                    UserAvatarButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ユーザーアイコンの読み込み中にエラーが発生: {ex.Message}");
                UserAvatarButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UserAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserPopupBorder.Visibility == Visibility.Visible)
            {
                UserPopupBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                UserPopupBorder.Visibility = Visibility.Visible;
                LogPopupBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async void PopupLogoutButton_Click(object sender, RoutedEventArgs e)
        {
            UserPopupBorder.Visibility = Visibility.Collapsed;
            await LogoutAsync();
        }

        private async void PopupUpdateTokenButton_Click(object sender, RoutedEventArgs e)
        {
            var newAccessToken = PopupAccessTokenPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                PopupTokenStatusText.Text = "トークンを入力してください";
                PopupTokenStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                PopupTokenStatusText.Visibility = Visibility.Visible;
                return;
            }

            PopupUpdateTokenButton.IsEnabled = false;
            PopupTokenStatusText.Text = "認証中...";
            PopupTokenStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            PopupTokenStatusText.Visibility = Visibility.Visible;

            var success = await _slackService.AuthenticateAsync(newAccessToken);

            if (success)
            {
                var settings = new SettingsService.Settings
                {
                    AccessToken = newAccessToken
                };
                await _settingsService.SaveSettingsAsync(settings);

                PopupTokenStatusText.Text = "✅ トークンを更新しました";
                PopupTokenStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                PopupAccessTokenPasswordBox.Password = string.Empty;

                await LoadUserAvatarAsync();
                await LoadChannelsAsync();

                await System.Threading.Tasks.Task.Delay(2000);
                PopupTokenStatusText.Visibility = Visibility.Collapsed;
                UserPopupBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                PopupTokenStatusText.Text = "❌ 認証に失敗しました";
                PopupTokenStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            }

            PopupUpdateTokenButton.IsEnabled = true;
        }

        private async System.Threading.Tasks.Task LogoutAsync()
        {
            var settings = new SettingsService.Settings
            {
                AccessToken = null
            };
            await _settingsService.SaveSettingsAsync(settings);

            AccessTokenPasswordBox.Password = string.Empty;
            AuthenticationStatusText.Text = string.Empty;

            MainPanel.Visibility = Visibility.Collapsed;
            AuthenticationPanel.Visibility = Visibility.Visible;
            UserAvatarButton.Visibility = Visibility.Collapsed;
            UserPopupBorder.Visibility = Visibility.Collapsed;
        }
    }
}
