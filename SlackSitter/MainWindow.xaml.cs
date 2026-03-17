using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using SlackSitter.Services;
using SlackSitter.Models;
using SlackSitter.Views;

namespace SlackSitter
{
    public sealed partial class MainWindow : Window
    {
        private enum ChannelDisplayFilter
        {
            All,
            JoinedOnly,
            NotJoinedOnly
        }

        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(5);
        private const int MaxConcurrentMessageLoads = 4;
        private readonly SlackService _slackService;
        private readonly SettingsService _settingsService;
        private readonly HttpClient _httpClient;
        private readonly DispatcherTimer _autoRefreshTimer;
        private string? _currentUserId;
        private string? _currentUserName;
        private ObservableCollection<ChannelWithMessages> _channelsWithMessages;
        private ObservableCollection<string> _logMessages;
        private readonly List<ChannelWithMessages> _allChannels = new List<ChannelWithMessages>();
        private Dictionary<string, string> _customEmojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ChannelDisplayFilter _currentChannelDisplayFilter = ChannelDisplayFilter.JoinedOnly;
        private bool _isAutoRefreshEnabled = true;
        private bool _isRefreshingData;

        public MainWindow()
        {
            InitializeComponent();
            _slackService = new SlackService();
            _settingsService = new SettingsService();
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = AutoRefreshInterval
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _channelsWithMessages = new ObservableCollection<ChannelWithMessages>();
            _logMessages = new ObservableCollection<string>();
            LogPopupBorder.SetLogItemsSource(_logMessages);
            UpdateAutoRefreshTimerState();

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
                            if (VisualTreeHelper.GetChild(container, 0) is FrameworkElement element)
                            {
                                element.Height = availableHeight;
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

        private static int CompareChannels(ChannelWithMessages left, ChannelWithMessages right)
        {
            var memberComparison = right.IsMember.CompareTo(left.IsMember);
            if (memberComparison != 0)
            {
                return memberComparison;
            }

            var timestampComparison = ParseSlackTimestamp(right.LastMessageTs).CompareTo(ParseSlackTimestamp(left.LastMessageTs));
            if (timestampComparison != 0)
            {
                return timestampComparison;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void InsertDisplayedChannel(ChannelWithMessages channel)
        {
            var insertIndex = 0;

            while (insertIndex < _channelsWithMessages.Count && CompareChannels(_channelsWithMessages[insertIndex], channel) <= 0)
            {
                insertIndex++;
            }

            _channelsWithMessages.Insert(insertIndex, channel);
        }

        private bool MatchesCurrentFilter(ChannelWithMessages channel)
        {
            return _currentChannelDisplayFilter switch
            {
                ChannelDisplayFilter.JoinedOnly => channel.IsMember,
                ChannelDisplayFilter.NotJoinedOnly => !channel.IsMember,
                _ => true
            };
        }

        private void RefreshDisplayedChannelsFromFilter()
        {
            var filteredChannels = _allChannels
                .Where(MatchesCurrentFilter)
                .OrderBy(channel => channel, Comparer<ChannelWithMessages>.Create(CompareChannels))
                .ToList();

            _channelsWithMessages = new ObservableCollection<ChannelWithMessages>(filteredChannels);
            TimesChannelsItemsControl.ItemsSource = _channelsWithMessages;
        }

        private void MessageRichTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not RichTextBlock richTextBlock)
            {
                return;
            }

            if (richTextBlock.Tag is not MessageDisplayItem message)
            {
                return;
            }

            richTextBlock.Blocks.Clear();

            var paragraph = new Paragraph();

            foreach (var segment in message.Segments)
            {
                switch (segment.Type)
                {
                    case MessageInlineSegmentType.Text:
                        AppendPlainTextInline(paragraph, segment);
                        break;
                    case MessageInlineSegmentType.Link:
                        if (segment.Uri != null)
                        {
                            var hyperlink = new Hyperlink
                            {
                                NavigateUri = segment.Uri
                            };
                            hyperlink.Inlines.Add(CreateStyledRun(segment));
                            paragraph.Inlines.Add(hyperlink);
                        }
                        else
                        {
                            AppendPlainTextInline(paragraph, segment);
                        }
                        break;
                    case MessageInlineSegmentType.Emoji:
                        if (!AppendEmojiInline(paragraph, segment.Text))
                        {
                            AppendPlainTextInline(paragraph, new MessageInlineSegment(MessageInlineSegmentType.Text, $":{segment.Text}:"));
                        }
                        break;
                }
            }

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new Run { Text = string.Empty });
            }

            richTextBlock.Blocks.Add(paragraph);
        }

        private void MessageItemView_MessageRichTextBlockLoadedRequested(MessageItemView sender, RichTextBlock richTextBlock)
        {
            MessageRichTextBlock_Loaded(richTextBlock, new RoutedEventArgs());
        }

        private void MessageAvatarBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not MessageDisplayItem message)
            {
                return;
            }

            if (message.UserAvatarUri == null)
            {
                border.Visibility = Visibility.Collapsed;
                return;
            }

            border.Visibility = Visibility.Visible;
            border.Background = new ImageBrush
            {
                ImageSource = new BitmapImage(message.UserAvatarUri),
                Stretch = Stretch.UniformToFill
            };
        }

        private void MessageItemView_MessageAvatarBorderLoadedRequested(MessageItemView sender, Border border)
        {
            MessageAvatarBorder_Loaded(border, new RoutedEventArgs());
        }

        private void ReactionBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not MessageReactionItem reaction)
            {
                return;
            }

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            var emojiUrl = ResolveEmojiUrl(reaction.Name);
            if (!string.IsNullOrWhiteSpace(emojiUrl) && Uri.TryCreate(emojiUrl, UriKind.Absolute, out var emojiUri))
            {
                content.Children.Add(new Image
                {
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    Source = new BitmapImage(emojiUri)
                });
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = $":{reaction.Name}:",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = reaction.Count.ToString(),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = content;
        }

        private void MessageItemView_ReactionBorderLoadedRequested(MessageItemView sender, Border border)
        {
            ReactionBorder_Loaded(border, new RoutedEventArgs());
        }

        private async void ShowMessageImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not MessageImageItem imageItem)
            {
                return;
            }

            if (button.Parent is not Panel panel)
            {
                return;
            }

            var image = panel.Children.OfType<Image>().FirstOrDefault();
            if (image == null)
            {
                return;
            }

            if (image.Source != null)
            {
                image.Visibility = Visibility.Visible;
                button.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                button.IsEnabled = false;
                button.Content = "読み込み中...";

                AddLog($"画像読み込み開始: 候補URL数={imageItem.CandidateUrls.Count}");

                DownloadedImageResult? imageResult = null;
                string? lastError = null;

                foreach (var candidateUrl in imageItem.CandidateUrls)
                {
                    try
                    {
                        AddLog($"画像候補URLを試行: {candidateUrl}");
                        var candidateResult = await DownloadSlackImageAsync(candidateUrl);
                        AddLog($"画像候補URLの応答: FinalUri={candidateResult.FinalUri}, ContentType={candidateResult.ContentType ?? "(unknown)"}");
                        if (!string.IsNullOrWhiteSpace(candidateResult.ContentType) && candidateResult.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        {
                            imageResult = candidateResult;
                            break;
                        }

                        lastError = $"画像ではないレスポンスです: {candidateResult.ContentType ?? "(unknown)"}";
                    }
                    catch (Exception ex)
                    {
                        lastError = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                    }
                }

                if (imageResult == null)
                {
                    throw new InvalidOperationException(lastError ?? "画像の取得候補が見つかりませんでした");
                }

                var bitmapImage = new BitmapImage();
                using var randomAccessStream = new InMemoryRandomAccessStream();
                await randomAccessStream.WriteAsync(imageResult.Bytes.AsBuffer());
                randomAccessStream.Seek(0);
                await bitmapImage.SetSourceAsync(randomAccessStream);
                image.Source = bitmapImage;
                image.Visibility = Visibility.Visible;
                button.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                var errorMessage = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                AddLog($"画像の読み込みに失敗しました: {errorMessage}");
                image.Visibility = Visibility.Collapsed;
                button.IsEnabled = true;
                button.Content = "画像";
            }
        }

        private void MessageItemView_ShowImageRequested(MessageItemView sender, Button button)
        {
            ShowMessageImageButton_Click(button, new RoutedEventArgs());
        }

        private void ChannelCardView_MessageRichTextBlockLoadedRequested(ChannelCardView sender, RichTextBlock richTextBlock)
        {
            MessageRichTextBlock_Loaded(richTextBlock, new RoutedEventArgs());
        }

        private void ChannelCardView_MessageAvatarBorderLoadedRequested(ChannelCardView sender, Border border)
        {
            MessageAvatarBorder_Loaded(border, new RoutedEventArgs());
        }

        private void ChannelCardView_ReactionBorderLoadedRequested(ChannelCardView sender, Border border)
        {
            ReactionBorder_Loaded(border, new RoutedEventArgs());
        }

        private void ChannelCardView_ShowImageRequested(ChannelCardView sender, Button button)
        {
            ShowMessageImageButton_Click(button, new RoutedEventArgs());
        }

        private sealed class DownloadedImageResult
        {
            public byte[] Bytes { get; init; } = Array.Empty<byte>();
            public string? ContentType { get; init; }
            public string? FinalUri { get; init; }
        }

        private async Task<DownloadedImageResult> DownloadSlackImageAsync(string imageUrl)
        {
            var accessToken = _slackService.GetAccessToken();
            var currentUri = new Uri(imageUrl, UriKind.Absolute);

            for (var redirectCount = 0; redirectCount < 5; redirectCount++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);

                if (!string.IsNullOrWhiteSpace(accessToken) && currentUri.Host.Contains("slack", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }

                using var response = await _httpClient.SendAsync(request);

                if (IsRedirectStatusCode(response.StatusCode))
                {
                    if (response.Headers.Location == null)
                    {
                        throw new HttpRequestException($"リダイレクト先がありません: {(int)response.StatusCode} {response.ReasonPhrase}");
                    }

                    AddLog($"画像取得リダイレクト: {(int)response.StatusCode} {response.ReasonPhrase} -> {response.Headers.Location}");

                    currentUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                return new DownloadedImageResult
                {
                    Bytes = await response.Content.ReadAsByteArrayAsync(),
                    ContentType = response.Content.Headers.ContentType?.MediaType,
                    FinalUri = currentUri.ToString()
                };
            }

            throw new HttpRequestException("画像取得でリダイレクト回数が上限を超えました");
        }

        private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.Moved
                || statusCode == HttpStatusCode.Redirect
                || statusCode == HttpStatusCode.RedirectMethod
                || statusCode == HttpStatusCode.RedirectKeepVerb
                || statusCode == HttpStatusCode.TemporaryRedirect
                || (int)statusCode == 308;
        }

        private void AppendPlainTextInline(Paragraph paragraph, MessageInlineSegment segment)
        {
            var normalizedText = segment.Text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalizedText.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    paragraph.Inlines.Add(CreateStyledInline(segment, lines[i]));
                }

                if (i < lines.Length - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }
        }

        private static Inline CreateStyledInline(MessageInlineSegment segment, string? textOverride = null)
        {
            if (!segment.IsCode)
            {
                return CreateStyledRun(segment, textOverride);
            }

            var textBlock = new TextBlock
            {
                Text = textOverride ?? segment.Text,
                FontFamily = new FontFamily("Consolas"),
                Padding = new Thickness(4, 1, 4, 1)
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                CornerRadius = new CornerRadius(3),
                Child = textBlock
            };

            return new InlineUIContainer
            {
                Child = border
            };
        }

        private static Run CreateStyledRun(MessageInlineSegment segment, string? textOverride = null)
        {
            var run = new Run
            {
                Text = textOverride ?? segment.Text
            };

            if (segment.IsBold)
            {
                run.FontWeight = FontWeights.Bold;
            }

            if (segment.IsItalic)
            {
                run.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }

            if (segment.IsStrikethrough)
            {
                run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            }

            if (segment.IsCode)
            {
                run.FontFamily = new FontFamily("Consolas");
            }

            return run;
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

        private async Task<List<ChannelWithMessages>> LoadChannelBatchAsync(IReadOnlyList<SlackNet.Conversation> channels, string? workspaceUrl)
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentMessageLoads);

            var tasks = channels.Select(async channel =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var messages = await _slackService.GetChannelMessagesAsync(channel.Id, 10).ConfigureAwait(false);
                    var userImageTasks = messages
                        .Select(message => message.User)
                        .Where(userId => !string.IsNullOrWhiteSpace(userId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(userId => userId!, userId => _slackService.GetUserImageUrlAsync(userId));

                    await Task.WhenAll(userImageTasks.Values).ConfigureAwait(false);

                    var displayMessages = messages
                        .Select(message => new MessageDisplayItem(
                            message,
                            channel.Id,
                            workspaceUrl,
                            !string.IsNullOrWhiteSpace(message.User) && userImageTasks.TryGetValue(message.User, out var imageTask)
                                ? imageTask.Result
                                : null))
                        .ToList();

                    return new ChannelWithMessages(channel, displayMessages, workspaceUrl);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var batchResults = await Task.WhenAll(tasks);
            return batchResults.ToList();
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

        private void LogPopupView_CopyLogRequested(object sender, RoutedEventArgs e)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(string.Join(Environment.NewLine, _logMessages));
            Clipboard.SetContent(dataPackage);
            AddLog("ログをクリップボードにコピーしました");
        }

        private void UpdateAutoRefreshTimerState()
        {
            if (_isAutoRefreshEnabled && _slackService.IsAuthenticated)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Start();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }

            if (UserPopupBorder != null)
            {
                UserPopupBorder.AutoRefreshButtonText = _isAutoRefreshEnabled ? "自動更新を停止" : "自動更新を再開";
                UserPopupBorder.AutoRefreshStatusText = _isAutoRefreshEnabled
                    ? $"自動更新: 有効 ({(int)AutoRefreshInterval.TotalMinutes}分ごと)"
                    : "自動更新: 停止中";
            }
        }

        private async Task RefreshWorkspaceDataAsync(string? startLogMessage = null, string? endLogMessage = null)
        {
            if (_isRefreshingData)
            {
                return;
            }

            _isRefreshingData = true;

            try
            {
                if (!string.IsNullOrWhiteSpace(startLogMessage))
                {
                    AddLog(startLogMessage);
                }

                await LoadCustomEmojiAsync();
                await LoadUserAvatarAsync();
                await LoadChannelsAsync();

                if (!string.IsNullOrWhiteSpace(endLogMessage))
                {
                    AddLog(endLogMessage);
                }
            }
            finally
            {
                _isRefreshingData = false;
                UpdateAutoRefreshTimerState();
            }
        }

        private async void AutoRefreshTimer_Tick(object sender, object e)
        {
            if (!_isAutoRefreshEnabled || !_slackService.IsAuthenticated || _isRefreshingData)
            {
                return;
            }

            await RefreshWorkspaceDataAsync("=== 自動データ再取得開始 ===", "=== 自動データ再取得完了 ===");
        }

        private void UserPopupView_AutoRefreshToggleRequested(object sender, RoutedEventArgs e)
        {
            _isAutoRefreshEnabled = !_isAutoRefreshEnabled;
            UpdateAutoRefreshTimerState();
            AddLog(_isAutoRefreshEnabled ? "自動更新を再開しました" : "自動更新を停止しました");
        }

        private void CircleIcon1Button_Click(object sender, RoutedEventArgs e)
        {
            _currentChannelDisplayFilter = _currentChannelDisplayFilter == ChannelDisplayFilter.JoinedOnly
                ? ChannelDisplayFilter.All
                : ChannelDisplayFilter.JoinedOnly;

            RefreshDisplayedChannelsFromFilter();
            AddLog(_currentChannelDisplayFilter == ChannelDisplayFilter.JoinedOnly
                ? "参加中チャンネルのみ表示に切り替えました"
                : "すべてのチャンネル表示に戻しました");
        }

        private void CircleIcon2Button_Click(object sender, RoutedEventArgs e)
        {
            _currentChannelDisplayFilter = _currentChannelDisplayFilter == ChannelDisplayFilter.NotJoinedOnly
                ? ChannelDisplayFilter.All
                : ChannelDisplayFilter.NotJoinedOnly;

            RefreshDisplayedChannelsFromFilter();
            AddLog(_currentChannelDisplayFilter == ChannelDisplayFilter.NotJoinedOnly
                ? "未参加チャンネルのみ表示に切り替えました"
                : "すべてのチャンネル表示に戻しました");
        }

        private async void LogPopupView_RefreshRequested(object sender, RoutedEventArgs e)
        {
            if (sender is not LogPopupView logPopupView)
            {
                return;
            }

            if (!_slackService.IsAuthenticated)
            {
                AddLog("再取得できません: 未認証です");
                return;
            }

            logPopupView.SetRefreshBusy(true);

            try
            {
                await RefreshWorkspaceDataAsync("=== データ再取得開始 ===", "=== データ再取得完了 ===");
            }
            finally
            {
                logPopupView.SetRefreshBusy(false);
            }
        }

        private async void LoadSettingsAndAuthenticate()
        {
            var settings = await _settingsService.LoadSettingsAsync();

            if (!string.IsNullOrEmpty(settings.AccessToken))
            {
                AuthenticationPanel.SetToken(settings.AccessToken);
                await AuthenticateWithToken(settings.AccessToken);
            }
            else
            {
                AuthenticationPanel.Visibility = Visibility.Visible;
            }
        }

        private async void AuthenticationView_AuthenticateRequested(AuthenticationView sender, string accessToken)
        {
            await AuthenticateWithToken(accessToken);
            sender.SetBusy(false);
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
                AuthenticationPanel.StatusMessage = string.Empty;
                MainPanel.Visibility = Visibility.Visible;

                UserPopupBorder.EnvironmentPathText = $".env ファイルの保存先: {_settingsService.GetEnvFilePath()}";

                await RefreshWorkspaceDataAsync();
            }
            else
            {
                AuthenticationPanel.StatusMessage = "認証に失敗しました。トークンを確認してください。";
                AuthenticationPanel.StatusBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
                UpdateAutoRefreshTimerState();
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
                _allChannels.Clear();
                _channelsWithMessages.Clear();
                TimesChannelsItemsControl.ItemsSource = _channelsWithMessages;

                var totalChannelsCount = 0;
                var totalTimesChannelsCount = 0;
                var workspaceUrl = _slackService.GetWorkspaceUrl();

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

                    var batchResults = await LoadChannelBatchAsync(timesChannelBatch, workspaceUrl);

                    foreach (var channelWithMessages in batchResults.OrderBy(channel => channel, Comparer<ChannelWithMessages>.Create(CompareChannels)))
                    {
                        _allChannels.Add(channelWithMessages);

                        if (MatchesCurrentFilter(channelWithMessages))
                        {
                            InsertDisplayedChannel(channelWithMessages);
                        }

                        AddLog($"チャンネル #{channelWithMessages.Name}: {channelWithMessages.Messages.Count} 件のメッセージを取得");
                    }
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
                    UserPopupBorder.SetAvatarImage(bitmap);
                    UserAvatarButton.Visibility = Visibility.Visible;
                    CircleIcon1Button.Visibility = Visibility.Visible;
                    CircleIcon2Button.Visibility = Visibility.Visible;

                    UserPopupBorder.UserNameText = userName ?? "Unknown";
                    UserPopupBorder.UserIdText = userId ?? string.Empty;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ユーザーアイコンの取得に失敗しました");
                    UserAvatarButton.Visibility = Visibility.Collapsed;
                    CircleIcon1Button.Visibility = Visibility.Collapsed;
                    CircleIcon2Button.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ユーザーアイコンの読み込み中にエラーが発生: {ex.Message}");
                UserAvatarButton.Visibility = Visibility.Collapsed;
                CircleIcon1Button.Visibility = Visibility.Collapsed;
                CircleIcon2Button.Visibility = Visibility.Collapsed;
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

        private async void UserPopupView_LogoutRequested(object sender, RoutedEventArgs e)
        {
            UserPopupBorder.Visibility = Visibility.Collapsed;
            await LogoutAsync();
        }

        private async void UserPopupView_UpdateTokenRequested(UserPopupView sender, string newAccessToken)
        {
            var success = await _slackService.AuthenticateAsync(newAccessToken);

            if (success)
            {
                var settings = new SettingsService.Settings
                {
                    AccessToken = newAccessToken
                };
                await _settingsService.SaveSettingsAsync(settings);

                sender.ShowTokenStatus("✅ トークンを更新しました", new SolidColorBrush(Microsoft.UI.Colors.Green));
                sender.ClearPendingAccessToken();

                await RefreshWorkspaceDataAsync("=== データ再取得開始 ===", "=== データ再取得完了 ===");

                await System.Threading.Tasks.Task.Delay(2000);
                sender.HideTokenStatus();
                UserPopupBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                sender.ShowTokenStatus("❌ 認証に失敗しました", new SolidColorBrush(Microsoft.UI.Colors.Red));
                UpdateAutoRefreshTimerState();
            }

            sender.SetUpdateTokenBusy(false);
        }

        private async System.Threading.Tasks.Task LogoutAsync()
        {
            var settings = new SettingsService.Settings
            {
                AccessToken = null
            };
            await _settingsService.SaveSettingsAsync(settings);

            AuthenticationPanel.ResetStatus();
            UserPopupBorder.Reset();

            MainPanel.Visibility = Visibility.Collapsed;
            AuthenticationPanel.Visibility = Visibility.Visible;
            UserAvatarButton.Visibility = Visibility.Collapsed;
            CircleIcon1Button.Visibility = Visibility.Collapsed;
            CircleIcon2Button.Visibility = Visibility.Collapsed;
            UserPopupBorder.Visibility = Visibility.Collapsed;
            UpdateAutoRefreshTimerState();
        }
    }
}
