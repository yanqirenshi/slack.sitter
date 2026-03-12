using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using SlackSitter.Services;
using SlackSitter.Models;

namespace SlackSitter
{
    public sealed partial class MainWindow : Window
    {
        private readonly SlackService _slackService;
        private readonly SettingsService _settingsService;
        private string? _currentUserId;
        private string? _currentUserName;

        public MainWindow()
        {
            InitializeComponent();
            _slackService = new SlackService();
            _settingsService = new SettingsService();

            System.Diagnostics.Debug.WriteLine($".env file path: {_settingsService.GetEnvFilePath()}");

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

        private async void LoadSettingsAndAuthenticate()
        {
            var settings = await _settingsService.LoadSettingsAsync();

            if (!string.IsNullOrEmpty(settings.AccessToken))
            {
                AccessTokenPasswordBox.Password = settings.AccessToken;
                await AuthenticateWithToken(settings.AccessToken);
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
            System.Diagnostics.Debug.WriteLine("=== チャンネル一覧の取得開始 ===");

            StatusText.Text = "チャンネル一覧を読み込み中...";
            StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            ErrorText.Visibility = Visibility.Collapsed;
            RequiredScopesPanel.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Visible;

            try
            {
                var channels = await _slackService.GetChannelsAsync();

                if (channels.Count == 0)
                {
                    StatusText.Text = "";
                    ErrorText.Text = "⚠️ チャンネルの取得に失敗しました。トークンの権限を確認してください。";
                    ErrorText.Visibility = Visibility.Visible;
                    RequiredScopesPanel.Visibility = Visibility.Visible;
                    System.Diagnostics.Debug.WriteLine("チャンネルが取得できませんでした。権限を確認してください。");
                }
                else
                {
                    // #times* で始まるチャンネルをフィルタリング
                    var timesChannels = channels
                        .Where(c => c.Name != null && c.Name.StartsWith("times"))
                        .OrderBy(c => c.Name)
                        .ToList();

                    System.Diagnostics.Debug.WriteLine($"取得したチャンネル数: {channels.Count}");
                    System.Diagnostics.Debug.WriteLine($"#times* チャンネル数: {timesChannels.Count}");
                    System.Diagnostics.Debug.WriteLine("");

                    if (timesChannels.Count > 0)
                    {
                        StatusText.Text = "メッセージを読み込み中...";

                        // 各チャンネルのメッセージを取得
                        var channelsWithMessages = new List<ChannelWithMessages>();
                        foreach (var channel in timesChannels)
                        {
                            var messages = await _slackService.GetChannelMessagesAsync(channel.Id, 10);
                            channelsWithMessages.Add(new ChannelWithMessages(channel, messages));

                            System.Diagnostics.Debug.WriteLine($"チャンネル #{channel.Name}: {messages.Count} 件のメッセージを取得");
                        }

                        // ItemsControlにチャンネルを設定
                        TimesChannelsItemsControl.ItemsSource = channelsWithMessages;
                        StatusPanel.Visibility = Visibility.Collapsed;

                        System.Diagnostics.Debug.WriteLine("=== #times* チャンネル一覧 ===");
                        foreach (var channelWithMessages in channelsWithMessages)
                        {
                            var channel = channelWithMessages.Channel;
                            var channelType = channel.IsPrivate ? "プライベート" : "パブリック";
                            System.Diagnostics.Debug.WriteLine($"チャンネル: #{channel.Name}");
                            System.Diagnostics.Debug.WriteLine($"  ID: {channel.Id}");
                            System.Diagnostics.Debug.WriteLine($"  種類: {channelType}");
                            System.Diagnostics.Debug.WriteLine($"  メンバー数: {channel.NumMembers}");
                            System.Diagnostics.Debug.WriteLine($"  トピック: {channel.Topic?.Value ?? "(なし)"}");
                            System.Diagnostics.Debug.WriteLine($"  説明: {channel.Purpose?.Value ?? "(なし)"}");
                            System.Diagnostics.Debug.WriteLine($"  メッセージ数: {channelWithMessages.Messages.Count}");
                            System.Diagnostics.Debug.WriteLine("");
                        }
                    }
                    else
                    {
                        StatusText.Text = "⚠️ #times* で始まるチャンネルが見つかりませんでした";
                        StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
                        StatusPanel.Visibility = Visibility.Visible;
                    }

                    System.Diagnostics.Debug.WriteLine("=== チャンネル一覧の取得完了 ===");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"チャンネル取得中にエラーが発生: {ex.Message}");
                StatusText.Text = "";
                ErrorText.Text = $"⚠️ エラーが発生しました: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;

                if (ex.Message.Contains("missing_scope") || ex.Message.Contains("権限"))
                {
                    RequiredScopesPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private async System.Threading.Tasks.Task LoadUserAvatarAsync()
        {
            try
            {
                var (userId, userName, userImageUrl) = await _slackService.GetCurrentUserInfoAsync();

                if (!string.IsNullOrEmpty(userImageUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"ユーザーアイコンを読み込み中: {userName} ({userId})");
                    System.Diagnostics.Debug.WriteLine($"アイコンURL: {userImageUrl}");

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
