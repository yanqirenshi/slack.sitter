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
using SlackNet;
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
        private const string DefaultCustomBoardName = "????????";

        private enum ChannelDisplayFilter
        {
            All,
            JoinedOnly,
            NotJoinedOnly,
            CustomOnly
        }

        private sealed class CustomBoardRuntimeState
        {
            public string Name { get; set; } = string.Empty;
            public List<string> SelectedChannelNames { get; set; } = new();
            public List<ChannelWithMessages> Channels { get; set; } = new();
        }

        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(5);
        private const int MaxConcurrentMessageLoads = 4;
        private readonly SlackService _slackService;
        private readonly SettingsService _settingsService;
        private readonly CustomBoardStorageService _customBoardStorageService;
        private readonly HttpClient _httpClient;
        private readonly DispatcherTimer _autoRefreshTimer;
        private readonly HashSet<string> _allUnarchivedChannelNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Conversation> _allUnarchivedChannelsByName = new(StringComparer.OrdinalIgnoreCase);
        private string? _currentUserId;
        private string? _currentUserName;
        private readonly ObservableCollection<ChannelWithMessages> _allDisplayedChannels;
        private readonly ObservableCollection<ChannelWithMessages> _joinedDisplayedChannels;
        private readonly ObservableCollection<ChannelWithMessages> _notJoinedDisplayedChannels;
        private readonly ObservableCollection<ChannelWithMessages> _customDisplayedChannels;
        private readonly List<CustomBoardRuntimeState> _customBoards = new();
        private ObservableCollection<string> _logMessages;
        private Dictionary<string, string> _customEmojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ChannelDisplayFilter _currentChannelDisplayFilter = ChannelDisplayFilter.JoinedOnly;
        private int _activeCustomBoardIndex = -1;
        private bool _isAutoRefreshEnabled = true;
        private bool _isRefreshingData;

        public MainWindow()
        {
            StartupTrace.Log("MainWindow constructor entered");
            InitializeComponent();
            StartupTrace.Log("MainWindow InitializeComponent completed");
            _slackService = new SlackService();
            _settingsService = new SettingsService();
            _customBoardStorageService = new CustomBoardStorageService();
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = AutoRefreshInterval
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _allDisplayedChannels = new ObservableCollection<ChannelWithMessages>();
            _joinedDisplayedChannels = new ObservableCollection<ChannelWithMessages>();
            _notJoinedDisplayedChannels = new ObservableCollection<ChannelWithMessages>();
            _customDisplayedChannels = new ObservableCollection<ChannelWithMessages>();
            _logMessages = new ObservableCollection<string>();
            AllChannelBoard.SetItemsSource(_allDisplayedChannels);
            JoinedChannelBoard.SetItemsSource(_joinedDisplayedChannels);
            NotJoinedChannelBoard.SetItemsSource(_notJoinedDisplayedChannels);
            CustomChannelBoard.SetItemsSource(_customDisplayedChannels);
            MainController.SetLogItemsSource(_logMessages);
            UpdateChannelFilterButtonState();
            UpdateVisibleChannelBoard();
            UpdateAutoRefreshTimerState();

            AddLog($".env file path: {_settingsService.GetEnvFilePath()}");

            StartupTrace.Log("MainWindow constructor before LoadSettingsAndAuthenticate");
            LoadSettingsAndAuthenticate();
            StartupTrace.Log("MainWindow constructor completed");
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

        private static int GetReplyCount(SlackNet.Events.MessageEvent message)
        {
            var propertyValue = message.GetType().GetProperty("ReplyCount")?.GetValue(message);

            if (propertyValue == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(propertyValue);
            }
            catch
            {
                return 0;
            }
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

        private void InsertDisplayedChannel(ObservableCollection<ChannelWithMessages> displayedChannels, ChannelWithMessages channel)
        {
            var insertIndex = 0;

            while (insertIndex < displayedChannels.Count && CompareChannels(displayedChannels[insertIndex], channel) <= 0)
            {
                insertIndex++;
            }

            displayedChannels.Insert(insertIndex, channel);
        }

        private void RefreshDisplayedChannelsFromFilter()
        {
            UpdateVisibleChannelBoard();
        }

        private void UpdateVisibleChannelBoard()
        {
            AllChannelBoard.Visibility = _currentChannelDisplayFilter == ChannelDisplayFilter.All
                ? Visibility.Visible
                : Visibility.Collapsed;
            JoinedChannelBoard.Visibility = _currentChannelDisplayFilter == ChannelDisplayFilter.JoinedOnly
                ? Visibility.Visible
                : Visibility.Collapsed;
            NotJoinedChannelBoard.Visibility = _currentChannelDisplayFilter == ChannelDisplayFilter.NotJoinedOnly
                ? Visibility.Visible
                : Visibility.Collapsed;
            CustomChannelBoard.Visibility = _currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly
                ? Visibility.Visible
                : Visibility.Collapsed;
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
                    var threadRepliesByParentTs = new Dictionary<string, List<SlackNet.Events.MessageEvent>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var message in messages.Where(message => !string.IsNullOrWhiteSpace(message.Ts) && GetReplyCount(message) > 0))
                    {
                        var replies = await _slackService.GetThreadRepliesAsync(channel.Id, message.Ts!).ConfigureAwait(false);
                        if (replies.Count > 0)
                        {
                            threadRepliesByParentTs[message.Ts!] = replies;
                        }
                    }

                    var allMessages = messages
                        .Concat(threadRepliesByParentTs.Values.SelectMany(replyMessages => replyMessages))
                        .ToList();

                    var userImageTasks = allMessages
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
                                : null,
                            threadRepliesByParentTs.TryGetValue(message.Ts ?? string.Empty, out var replies)
                                ? replies
                                    .Select(reply => new MessageDisplayItem(
                                        reply,
                                        channel.Id,
                                        workspaceUrl,
                                        !string.IsNullOrWhiteSpace(reply.User) && userImageTasks.TryGetValue(reply.User, out var replyImageTask)
                                            ? replyImageTask.Result
                                            : null))
                                    .ToList()
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

            MainController.SetAutoRefreshState(
                _isAutoRefreshEnabled ? "自動更新を停止" : "自動更新を再開",
                _isAutoRefreshEnabled
                    ? $"自動更新: 有効 ({(int)AutoRefreshInterval.TotalMinutes}分ごと)"
                    : "自動更新: 停止中");
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

        private void UpdateChannelFilterButtonState()
        {
            MainController.SetFilterButtonState(
                _currentChannelDisplayFilter == ChannelDisplayFilter.JoinedOnly,
                _currentChannelDisplayFilter == ChannelDisplayFilter.NotJoinedOnly,
                _currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly,
                _currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly ? _activeCustomBoardIndex : null);
        }

        private async void MainController_CircleIcon1Click(object sender, RoutedEventArgs e)
        {
            _currentChannelDisplayFilter = _currentChannelDisplayFilter == ChannelDisplayFilter.JoinedOnly
                ? ChannelDisplayFilter.All
                : ChannelDisplayFilter.JoinedOnly;

            RefreshDisplayedChannelsFromFilter();
            UpdateChannelFilterButtonState();
            await SaveCustomBoardStateAsync();
            AddLog(_currentChannelDisplayFilter == ChannelDisplayFilter.JoinedOnly
                ? "参加中チャンネルのみ表示に切り替えました"
                : "すべてのチャンネル表示に戻しました");
        }

        private async void MainController_CircleIcon2Click(object sender, RoutedEventArgs e)
        {
            _currentChannelDisplayFilter = _currentChannelDisplayFilter == ChannelDisplayFilter.NotJoinedOnly
                ? ChannelDisplayFilter.All
                : ChannelDisplayFilter.NotJoinedOnly;

            RefreshDisplayedChannelsFromFilter();
            UpdateChannelFilterButtonState();
            await SaveCustomBoardStateAsync();
            AddLog(_currentChannelDisplayFilter == ChannelDisplayFilter.NotJoinedOnly
                ? "未参加チャンネルのみ表示に切り替えました"
                : "すべてのチャンネル表示に戻しました");
        }

        private async void MainController_CustomChannelClick(object sender, RoutedEventArgs e)
        {
            if (sender is not CircleActionButtonView button || button.Tag is not int customBoardIndex || customBoardIndex < 0 || customBoardIndex >= _customBoards.Count)
            {
                return;
            }

            if (_currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly && _activeCustomBoardIndex == customBoardIndex)
            {
                _currentChannelDisplayFilter = ChannelDisplayFilter.All;
            }
            else
            {
                _activeCustomBoardIndex = customBoardIndex;
                _currentChannelDisplayFilter = ChannelDisplayFilter.CustomOnly;
                ApplyActiveCustomBoard();
            }

            RefreshDisplayedChannelsFromFilter();
            UpdateChannelFilterButtonState();
            await SaveCustomBoardStateAsync();
            AddLog(_currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly
                ? $"追加チャンネル表示に切り替えました: {GetCustomBoardDisplayName(_customBoards[_activeCustomBoardIndex], _activeCustomBoardIndex)}"
                : "すべてのチャンネル表示に戻しました");
        }

        private async void LogPopupView_RefreshRequested(object sender, RoutedEventArgs e)
        {
            if (!_slackService.IsAuthenticated)
            {
                AddLog("再取得できません: 未認証です");
                return;
            }


            await RefreshActiveBoardChannelsAsync();
        }

        private async Task RefreshActiveBoardChannelsAsync()
        {
            if (_isRefreshingData)
            {
                return;
            }

            var activeChannels = GetActiveBoardChannels();
            if (activeChannels.Count == 0)
            {
                AddLog("再取得対象のチャンネルがありません");
                return;
            }

            _isRefreshingData = true;
            MainController.ShowLoadingIndicatorBusy();

            try
            {
                AddLog($"=== アクティブボードのデータ再取得開始 ({activeChannels.Count} チャンネル) ===");

                await LoadCustomEmojiAsync();

                var refreshedChannels = await LoadChannelBatchAsync(
                    activeChannels.Select(channel => channel.Channel).ToList(),
                    _slackService.GetWorkspaceUrl());

                if (_currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly)
                {
                    ReplaceCustomChannels(_activeCustomBoardIndex, refreshedChannels);
                }
                else
                {
                    foreach (var refreshedChannel in refreshedChannels)
                    {
                        UpsertChannelAcrossBoards(refreshedChannel);
                        AddLog($"チャンネル #{refreshedChannel.Name}: {refreshedChannel.Messages.Count} 件のメッセージを再取得");
                    }
                }

                AddLog("=== アクティブボードのデータ再取得完了 ===");
            }
            finally
            {
                _isRefreshingData = false;
                MainController.SetLoadingIndicatorIdle();
                UpdateAutoRefreshTimerState();
            }
        }

        private List<ChannelWithMessages> GetActiveBoardChannels()
        {
            return _currentChannelDisplayFilter switch
            {
                ChannelDisplayFilter.JoinedOnly => _joinedDisplayedChannels.ToList(),
                ChannelDisplayFilter.NotJoinedOnly => _notJoinedDisplayedChannels.ToList(),
                ChannelDisplayFilter.CustomOnly => _customDisplayedChannels.ToList(),
                _ => _allDisplayedChannels.ToList()
            };
        }

        private void ReplaceCustomChannels(int customBoardIndex, IEnumerable<ChannelWithMessages> refreshedChannels)
        {
            if (customBoardIndex < 0 || customBoardIndex >= _customBoards.Count)
            {
                return;
            }

            var customBoard = _customBoards[customBoardIndex];
            customBoard.Channels = OrderCustomChannels(customBoard.SelectedChannelNames, refreshedChannels);
            _customDisplayedChannels.Clear();
            foreach (var channel in customBoard.Channels)
            {
                _customDisplayedChannels.Add(channel);
                AddLog($"チャンネル #{channel.Name}: {channel.Messages.Count} 件のメッセージを再取得");
            }
        }

        private void ApplyActiveCustomBoard()
        {
            _customDisplayedChannels.Clear();

            if (_activeCustomBoardIndex < 0 || _activeCustomBoardIndex >= _customBoards.Count)
            {
                return;
            }

            foreach (var channel in _customBoards[_activeCustomBoardIndex].Channels)
            {
                _customDisplayedChannels.Add(channel);
            }
        }

        private static List<ChannelWithMessages> OrderCustomChannels(IReadOnlyList<string> selectedChannelNames, IEnumerable<ChannelWithMessages> channels)
        {
            var refreshedChannelMap = channels.ToDictionary(channel => channel.Name, StringComparer.OrdinalIgnoreCase);
            var orderedChannels = new List<ChannelWithMessages>();

            foreach (var channelName in selectedChannelNames)
            {
                if (refreshedChannelMap.TryGetValue(channelName, out var channel))
                {
                    orderedChannels.Add(channel);
                }
            }

            return orderedChannels;
        }

        private void SyncCustomBoardButtons()
        {
            MainController.SetCustomChannelButtons(
                _customBoards.Select((board, index) => GetCustomBoardDisplayName(board, index)).ToList(),
                _currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly ? _activeCustomBoardIndex : null);
            MainController.SetCustomChannelButtonVisible(_customBoards.Count > 0);
        }

        private static string GetCustomBoardDisplayName(CustomBoardRuntimeState board, int index)
        {
            return NormalizeCustomBoardName(board.Name);
        }

        private void UpsertChannelAcrossBoards(ChannelWithMessages refreshedChannel)
        {
            UpsertChannelInCollection(_allDisplayedChannels, refreshedChannel);

            if (refreshedChannel.IsMember)
            {
                UpsertChannelInCollection(_joinedDisplayedChannels, refreshedChannel);
                RemoveChannelFromCollection(_notJoinedDisplayedChannels, refreshedChannel.Channel.Id);
            }
            else
            {
                UpsertChannelInCollection(_notJoinedDisplayedChannels, refreshedChannel);
                RemoveChannelFromCollection(_joinedDisplayedChannels, refreshedChannel.Channel.Id);
            }
        }

        private void UpsertChannelInCollection(ObservableCollection<ChannelWithMessages> channels, ChannelWithMessages channel)
        {
            RemoveChannelFromCollection(channels, channel.Channel.Id);
            InsertDisplayedChannel(channels, channel);
        }

        private static void RemoveChannelFromCollection(ObservableCollection<ChannelWithMessages> channels, string? channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return;
            }

            for (int i = channels.Count - 1; i >= 0; i--)
            {
                if (string.Equals(channels[i].Channel.Id, channelId, StringComparison.OrdinalIgnoreCase))
                {
                    channels.RemoveAt(i);
                }
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

                MainController.SetEnvironmentPathText($".env ファイルの保存先: {_settingsService.GetEnvFilePath()}");
                await LoadPersistedCustomBoardStateAsync();

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
            MainController.ShowLoadingIndicatorBusy();

            StatusPanel.ShowLoadingMessage("チャンネル一覧を読み込み中...");

            try
            {
                _allUnarchivedChannelNames.Clear();
                _allUnarchivedChannelsByName.Clear();
                _allDisplayedChannels.Clear();
                _joinedDisplayedChannels.Clear();
                _notJoinedDisplayedChannels.Clear();
                MainController.SetAvailableChannels(Array.Empty<string>());

                var totalChannelsCount = 0;
                var totalTimesChannelsCount = 0;
                var workspaceUrl = _slackService.GetWorkspaceUrl();

                await foreach (var channelBatch in _slackService.GetChannelBatchesAsync(100))
                {
                    totalChannelsCount += channelBatch.Count;
                    AddLog($"チャンネルを {channelBatch.Count} 件取得 (累計: {totalChannelsCount} 件)");

                    foreach (var channelName in channelBatch
                        .Where(c => !c.IsArchived && !string.IsNullOrWhiteSpace(c.Name))
                        .Select(c => c.Name!))
                    {
                        _allUnarchivedChannelNames.Add(channelName);
                    }

                    foreach (var channel in channelBatch.Where(c => !c.IsArchived && !string.IsNullOrWhiteSpace(c.Name)))
                    {
                        _allUnarchivedChannelsByName[channel.Name!] = channel;
                    }

                    var timesChannelBatch = channelBatch
                        .Where(c => c.Name != null && c.Name.StartsWith("times") && !c.IsArchived)
                        .ToList();

                    totalTimesChannelsCount += timesChannelBatch.Count;

                    if (timesChannelBatch.Count == 0)
                    {
                        continue;
                    }

                    StatusPanel.HidePanel();

                    var batchResults = await LoadChannelBatchAsync(timesChannelBatch, workspaceUrl);

                    foreach (var channelWithMessages in batchResults.OrderBy(channel => channel, Comparer<ChannelWithMessages>.Create(CompareChannels)))
                    {
                        InsertDisplayedChannel(_allDisplayedChannels, channelWithMessages);

                        if (channelWithMessages.IsMember)
                        {
                            InsertDisplayedChannel(_joinedDisplayedChannels, channelWithMessages);
                        }
                        else
                        {
                            InsertDisplayedChannel(_notJoinedDisplayedChannels, channelWithMessages);
                        }

                        AddLog($"チャンネル #{channelWithMessages.Name}: {channelWithMessages.Messages.Count} 件のメッセージを取得");
                    }
                }

                AddLog($"取得したチャンネル数: {totalChannelsCount}");
                AddLog($"#times* チャンネル数: {totalTimesChannelsCount}");
                MainController.SetAvailableChannels(_allUnarchivedChannelNames);
                await RestorePersistedCustomBoardAsync(workspaceUrl);

                if (totalChannelsCount == 0)
                {
                    StatusPanel.ShowErrorMessage("⚠️ チャンネルの取得に失敗しました。トークンの権限を確認してください。", true);
                    AddLog("チャンネルが取得できませんでした。権限を確認してください。");

                    // インジケーターをグレーに設定
                    MainController.SetLoadingIndicatorIdle();
                }
                else if (totalTimesChannelsCount > 0)
                {
                    AddLog("=== チャンネル情報の取得完了 ===");

                    // データ取得完了 - インジケーターをグレーに変更
                    MainController.SetLoadingIndicatorIdle();
                    AddLog("=== チャンネル一覧の取得完了 ===");
                }
                else
                {
                    StatusPanel.ShowWarningMessage("⚠️ #times* で始まるチャンネルが見つかりませんでした", new SolidColorBrush(Microsoft.UI.Colors.Orange));

                    MainController.SetLoadingIndicatorIdle();

                    AddLog("=== チャンネル一覧の取得完了 ===");
                }
            }
            catch (Exception ex)
            {
                AddLog($"チャンネル取得中にエラーが発生: {ex.Message}");
                StatusPanel.ShowErrorMessage(
                    $"⚠️ エラーが発生しました: {ex.Message}",
                    ex.Message.Contains("missing_scope") || ex.Message.Contains("権限"));

                // エラー時はインジケーターをグレーに設定
                MainController.SetLoadingIndicatorIdle();
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
                    MainController.SetUserAvatarImage(bitmap);
                    MainController.SetUserInfo(userName, userId);
                    MainController.ShowUserActionButtons();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ユーザーアイコンの取得に失敗しました");
                    MainController.HideUserActionButtons();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ユーザーアイコンの読み込み中にエラーが発生: {ex.Message}");
                MainController.HideUserActionButtons();
            }
        }

        private void MainController_GearIconClick(object sender, RoutedEventArgs e)
        {
            if (_activeCustomBoardIndex < 0 || _activeCustomBoardIndex >= _customBoards.Count)
            {
                MainController.SetGearPopupInputs(string.Empty, Array.Empty<string>());
                return;
            }

            var customBoard = _customBoards[_activeCustomBoardIndex];
            MainController.SetGearPopupInputs(
                GetCustomBoardDisplayName(customBoard, _activeCustomBoardIndex),
                customBoard.SelectedChannelNames);
        }

        private void MainController_PlusIconClick(object sender, RoutedEventArgs e)
        {
        }

        private async void MainController_AddChannelsRequested(object sender, RoutedEventArgs e)
        {
            if (!_slackService.IsAuthenticated)
            {
                AddLog("追加チャンネルを取得できません: 未認証です");
                return;
            }

            if (_customBoards.Count >= 10)
            {
                AddLog("追加できるカスタムボードは最大 10 件です");
                MainController.HideAllPopups();
                return;
            }

            var selectedChannelNames = MainController.SelectedChannelNames;
            if (selectedChannelNames.Count == 0)
            {
                AddLog("追加対象のチャンネルが選択されていません");
                return;
            }

            var selectedChannels = selectedChannelNames
                .Where(name => _allUnarchivedChannelsByName.TryGetValue(name, out _))
                .Select(name => _allUnarchivedChannelsByName[name])
                .ToList();

            if (selectedChannels.Count == 0)
            {
                AddLog("追加対象のチャンネル情報が見つかりませんでした");
                return;
            }

            MainController.ShowLoadingIndicatorBusy();

            try
            {
                AddLog($"=== 追加チャンネルの取得開始 ({selectedChannels.Count} チャンネル) ===");
                var customChannels = await LoadChannelBatchAsync(selectedChannels, _slackService.GetWorkspaceUrl());
                var customBoardIndex = _customBoards.Count;
                var customBoard = new CustomBoardRuntimeState
                {
                    Name = NormalizeCustomBoardName(MainController.PendingCustomBoardName),
                    SelectedChannelNames = selectedChannelNames.ToList(),
                    Channels = OrderCustomChannels(selectedChannelNames, customChannels)
                };

                _customBoards.Add(customBoard);
                _activeCustomBoardIndex = customBoardIndex;
                ApplyActiveCustomBoard();
                _currentChannelDisplayFilter = ChannelDisplayFilter.CustomOnly;
                RefreshDisplayedChannelsFromFilter();
                SyncCustomBoardButtons();
                UpdateChannelFilterButtonState();
                await SaveCustomBoardStateAsync();
                MainController.HideAllPopups();
                AddLog("=== 追加チャンネルの取得完了 ===");
            }
            finally
            {
                MainController.SetLoadingIndicatorIdle();
            }
        }

        private async void MainController_UpdateCustomBoardRequested(object sender, RoutedEventArgs e)
        {
            if (_activeCustomBoardIndex < 0 || _activeCustomBoardIndex >= _customBoards.Count)
            {
                AddLog("変更対象のカスタムボードが選択されていません");
                return;
            }

            var selectedChannelNames = MainController.SelectedChannelNames;
            if (selectedChannelNames.Count == 0)
            {
                AddLog("変更対象のチャンネルが選択されていません");
                return;
            }

            var selectedChannels = selectedChannelNames
                .Where(name => _allUnarchivedChannelsByName.TryGetValue(name, out _))
                .Select(name => _allUnarchivedChannelsByName[name])
                .ToList();

            if (selectedChannels.Count == 0)
            {
                AddLog("変更対象のチャンネル情報が見つかりませんでした");
                return;
            }

            MainController.ShowLoadingIndicatorBusy();

            try
            {
                AddLog($"=== カスタムボードの変更開始 ({selectedChannels.Count} チャンネル) ===");
                var customChannels = await LoadChannelBatchAsync(selectedChannels, _slackService.GetWorkspaceUrl());
                var customBoard = _customBoards[_activeCustomBoardIndex];
                customBoard.Name = NormalizeCustomBoardName(MainController.PendingGearBoardName);
                customBoard.SelectedChannelNames = selectedChannelNames.ToList();
                customBoard.Channels = OrderCustomChannels(selectedChannelNames, customChannels);

                if (_currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly)
                {
                    ApplyActiveCustomBoard();
                    RefreshDisplayedChannelsFromFilter();
                }

                SyncCustomBoardButtons();
                UpdateChannelFilterButtonState();
                await SaveCustomBoardStateAsync();
                MainController.HideAllPopups();
                AddLog($"=== カスタムボードの変更完了: {GetCustomBoardDisplayName(customBoard, _activeCustomBoardIndex)} ===");
            }
            finally
            {
                MainController.SetLoadingIndicatorIdle();
            }
        }

        private async void UserPopupView_LogoutRequested(object sender, RoutedEventArgs e)
        {
            MainController.HideAllPopups();
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

                MainController.ShowTokenStatus("✅ トークンを更新しました", new SolidColorBrush(Microsoft.UI.Colors.Green));
                MainController.ClearPendingAccessToken();

                await RefreshWorkspaceDataAsync("=== データ再取得開始 ===", "=== データ再取得完了 ===");

                await System.Threading.Tasks.Task.Delay(2000);
                MainController.HideTokenStatus();
                MainController.HideAllPopups();
            }
            else
            {
                MainController.ShowTokenStatus("❌ 認証に失敗しました", new SolidColorBrush(Microsoft.UI.Colors.Red));
                UpdateAutoRefreshTimerState();
            }

            MainController.SetUpdateTokenBusy(false);
        }

        private async System.Threading.Tasks.Task LogoutAsync()
        {
            var settings = new SettingsService.Settings
            {
                AccessToken = null
            };
            await _settingsService.SaveSettingsAsync(settings);

            AuthenticationPanel.ResetStatus();

            MainPanel.Visibility = Visibility.Collapsed;
            AuthenticationPanel.Visibility = Visibility.Visible;
            MainController.Reset();
            UpdateAutoRefreshTimerState();
        }

        private async Task LoadPersistedCustomBoardStateAsync()
        {
            var state = await _customBoardStorageService.LoadAsync();
            _customBoards.Clear();

            foreach (var board in state.CustomBoards.Take(10))
            {
                var selectedChannels = board.SelectedChannels
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (selectedChannels.Count == 0)
                {
                    continue;
                }

                _customBoards.Add(new CustomBoardRuntimeState
                {
                    Name = NormalizeCustomBoardName(board.Name),
                    SelectedChannelNames = selectedChannels
                });
            }

            _activeCustomBoardIndex = _customBoards.Count == 0
                ? -1
                : Math.Clamp(state.ActiveCustomBoardIndex, 0, _customBoards.Count - 1);

            MainController.ResetPlusPopupInputs();
            SyncCustomBoardButtons();
            _currentChannelDisplayFilter = ParsePersistedFilter(state.ActiveFilter, _customBoards.Count > 0);
            UpdateChannelFilterButtonState();
            RefreshDisplayedChannelsFromFilter();
        }

        private async Task RestorePersistedCustomBoardAsync(string? workspaceUrl)
        {
            if (_customBoards.Count == 0)
            {
                _customDisplayedChannels.Clear();
                SyncCustomBoardButtons();
                if (_currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly)
                {
                    _currentChannelDisplayFilter = ChannelDisplayFilter.JoinedOnly;
                    RefreshDisplayedChannelsFromFilter();
                    UpdateChannelFilterButtonState();
                    await SaveCustomBoardStateAsync();
                }

                return;
            }

            foreach (var customBoard in _customBoards)
            {
                var selectedChannels = customBoard.SelectedChannelNames
                    .Where(name => _allUnarchivedChannelsByName.TryGetValue(name, out _))
                    .Select(name => _allUnarchivedChannelsByName[name])
                    .ToList();

                customBoard.Channels = selectedChannels.Count == 0
                    ? new List<ChannelWithMessages>()
                    : OrderCustomChannels(customBoard.SelectedChannelNames, await LoadChannelBatchAsync(selectedChannels, workspaceUrl));
            }

            ApplyActiveCustomBoard();
            SyncCustomBoardButtons();
        }

        private Task SaveCustomBoardStateAsync()
        {
            return _customBoardStorageService.SaveAsync(
                _customBoards.Select(board => new CustomBoardStorageService.CustomBoardDefinition
                {
                    Name = board.Name,
                    SelectedChannels = board.SelectedChannelNames.ToList()
                }),
                _activeCustomBoardIndex,
                _currentChannelDisplayFilter.ToString());
        }

        private static ChannelDisplayFilter ParsePersistedFilter(string? activeFilter, bool hasCustomChannels)
        {
            if (!Enum.TryParse<ChannelDisplayFilter>(activeFilter, true, out var filter))
            {
                return ChannelDisplayFilter.JoinedOnly;
            }

            if (filter == ChannelDisplayFilter.CustomOnly && !hasCustomChannels)
            {
                return ChannelDisplayFilter.JoinedOnly;
            }

            return filter;
        }

        private static string NormalizeCustomBoardName(string? name)
        {
            return string.IsNullOrWhiteSpace(name) ? DefaultCustomBoardName : name.Trim();
        }
    }
}
