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

namespace SlackSitter
{
    public sealed partial class MainWindow : Window
    {
        private readonly SlackService _slackService;
        private readonly SettingsService _settingsService;

        public MainWindow()
        {
            InitializeComponent();
            _slackService = new SlackService();
            _settingsService = new SettingsService();

            System.Diagnostics.Debug.WriteLine($".env file path: {_settingsService.GetEnvFilePath()}");

            LoadSettingsAndAuthenticate();
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
                    StatusText.Text = $"✅ チャンネル {channels.Count} 件を取得しました";
                    StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                    ErrorText.Visibility = Visibility.Collapsed;
                    RequiredScopesPanel.Visibility = Visibility.Collapsed;

                    System.Diagnostics.Debug.WriteLine($"取得したチャンネル数: {channels.Count}");
                    System.Diagnostics.Debug.WriteLine("");

                    foreach (var channel in channels.OrderBy(c => c.Name))
                    {
                        var channelType = channel.IsPrivate ? "プライベート" : "パブリック";
                        var memberCount = channel.NumMembers;

                        System.Diagnostics.Debug.WriteLine($"チャンネル: #{channel.Name}");
                        System.Diagnostics.Debug.WriteLine($"  ID: {channel.Id}");
                        System.Diagnostics.Debug.WriteLine($"  種類: {channelType}");
                        System.Diagnostics.Debug.WriteLine($"  メンバー数: {memberCount}");
                        System.Diagnostics.Debug.WriteLine($"  トピック: {channel.Topic?.Value ?? "(なし)"}");
                        System.Diagnostics.Debug.WriteLine($"  説明: {channel.Purpose?.Value ?? "(なし)"}");
                        System.Diagnostics.Debug.WriteLine($"  状態: {(channel.IsArchived ? "アーカイブ済み" : "アクティブ")}");
                        System.Diagnostics.Debug.WriteLine("");
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

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
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
        }
    }
}
