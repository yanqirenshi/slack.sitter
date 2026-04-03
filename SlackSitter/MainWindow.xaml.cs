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
        private const int MaxConcurrentMessageLoads = 10;
        private const int MaxConcurrentInlineImageLoads = 2;
        private readonly SlackService _slackService;
        private readonly SettingsService _settingsService;
        private readonly CustomBoardStorageService _customBoardStorageService;
        private readonly HttpClient _httpClient;
        private readonly DispatcherTimer _autoRefreshTimer;
        private readonly SemaphoreSlim _inlineImageLoadSemaphore = new(MaxConcurrentInlineImageLoads);
        private readonly HashSet<string> _allUnarchivedChannelNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Conversation> _allUnarchivedChannelsByName = new(StringComparer.OrdinalIgnoreCase);
        private string? _currentUserId;
        private string? _currentUserName;
        private readonly BatchObservableCollection<ChannelWithMessages> _allDisplayedChannels;
        private readonly BatchObservableCollection<ChannelWithMessages> _joinedDisplayedChannels;
        private readonly BatchObservableCollection<ChannelWithMessages> _notJoinedDisplayedChannels;
        private readonly BatchObservableCollection<ChannelWithMessages> _customDisplayedChannels;
        private readonly List<CustomBoardRuntimeState> _customBoards = new();
        private ObservableCollection<string> _logMessages;
        private Dictionary<string, string> _customEmojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ChannelDisplayFilter _currentChannelDisplayFilter = ChannelDisplayFilter.JoinedOnly;
        private int _activeCustomBoardIndex = -1;
        private bool _isAutoRefreshEnabled = true;
        private bool _isRefreshingData;
        private bool _isStartupSplashVisible = true;

        public MainWindow()
        {
            StartupTrace.Log("MainWindow constructor entered");
            InitializeComponent();
            StartupTrace.Log("MainWindow InitializeComponent completed");
            InitializeStartupSplashImage();
            _slackService = new SlackService();
            _settingsService = new SettingsService();
            _customBoardStorageService = new CustomBoardStorageService();
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = AutoRefreshInterval
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _allDisplayedChannels = new BatchObservableCollection<ChannelWithMessages>();
            _joinedDisplayedChannels = new BatchObservableCollection<ChannelWithMessages>();
            _notJoinedDisplayedChannels = new BatchObservableCollection<ChannelWithMessages>();
            _customDisplayedChannels = new BatchObservableCollection<ChannelWithMessages>();
            _logMessages = new ObservableCollection<string>();
            Services.MessageRenderContext.Current.LoadMessageImageAsync = LoadInlineMessageBitmapAsync;
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

        private void InitializeStartupSplashImage()
        {
            var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            if (!System.IO.File.Exists(imagePath))
            {
                return;
            }

            StartupSplashImage.Source = new BitmapImage(new Uri(imagePath));
        }

        private void HideStartupSplash()
        {
            if (!_isStartupSplashVisible)
            {
                return;
            }

            StartupSplashOverlay.Visibility = Visibility.Collapsed;
            _isStartupSplashVisible = false;
        }

        private static double ParseSlackTimestamp(string? timestamp)
        {
            return double.TryParse(timestamp, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : double.MinValue;
        }

        // ReplyCount プロパティへのアクセスをキャッシュ済みデリゲートで高速化
        private static readonly Func<SlackNet.Events.MessageEvent, int> GetReplyCountDelegate = CreateReplyCountGetter();

        private static Func<SlackNet.Events.MessageEvent, int> CreateReplyCountGetter()
        {
            var property = typeof(SlackNet.Events.MessageEvent).GetProperty("ReplyCount",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (property == null)
            {
                return _ => 0;
            }

            var getter = property.GetGetMethod();
            if (getter == null)
            {
                return _ => 0;
            }

            return message =>
            {
                try
                {
                    var value = getter.Invoke(message, null);
                    return value != null ? Convert.ToInt32(value) : 0;
                }
                catch
                {
                    return 0;
                }
            };
        }

        private static int GetReplyCount(SlackNet.Events.MessageEvent message)
        {
            return GetReplyCountDelegate(message);
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

        // Loaded 系イベントハンドラは MessageRenderContext に移行済み
        // MessageItemView.BuildContent() 内で直接コンテンツを構築するため不要

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

                var bitmapImage = await LoadInlineMessageBitmapAsync(imageItem, logFailures: true);
                if (bitmapImage == null)
                {
                    throw new InvalidOperationException("画像の取得候補が見つかりませんでした");
                }

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

        private Task<BitmapImage?> LoadInlineMessageBitmapAsync(MessageImageItem imageItem)
        {
            return LoadInlineMessageBitmapAsync(imageItem, logFailures: false);
        }

        private async Task<BitmapImage?> LoadInlineMessageBitmapAsync(MessageImageItem imageItem, bool logFailures)
        {
            await _inlineImageLoadSemaphore.WaitAsync();

            try
            {
                DownloadedImageResult? imageResult = null;
                string? lastError = null;

                foreach (var candidateUrl in imageItem.CandidateUrls)
                {
                    try
                    {
                        var candidateResult = await DownloadSlackImageAsync(candidateUrl);
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
                    if (logFailures && !string.IsNullOrWhiteSpace(lastError))
                    {
                        AddLog($"画像の読み込みに失敗しました: {lastError}");
                    }

                    return null;
                }

                var bitmapImage = new BitmapImage();
                using var randomAccessStream = new InMemoryRandomAccessStream();
                await randomAccessStream.WriteAsync(imageResult.Bytes.AsBuffer());
                randomAccessStream.Seek(0);
                await bitmapImage.SetSourceAsync(randomAccessStream);
                return bitmapImage;
            }
            finally
            {
                _inlineImageLoadSemaphore.Release();
            }
        }

        private void ChannelCardView_ShowImageRequested(ChannelCardView sender, Button button)
        {
            ShowMessageImageButton_Click(button, new RoutedEventArgs());
        }

        private void ChannelCardView_ImagePreviewRequested(ChannelCardView sender, ImageSource imageSource)
        {
            ShowImagePreview(imageSource);
        }

        private async void ChannelCardView_ReactionRequested(ChannelCardView sender, MessageReactionClickInfo reactionInfo)
        {
            if (!_slackService.IsAuthenticated)
            {
                AddLog("リアクションを変更できません: 未認証です");
                return;
            }

            if (_isRefreshingData)
            {
                AddLog("データ更新中のため、リアクション変更をスキップしました");
                return;
            }

            if (sender.Channel?.Channel == null || string.IsNullOrWhiteSpace(sender.Channel.Channel.Id))
            {
                AddLog("リアクション対象のチャンネル情報が見つかりませんでした");
                return;
            }

            if (string.IsNullOrWhiteSpace(reactionInfo?.Message?.Ts) || string.IsNullOrWhiteSpace(reactionInfo.Reaction.Name))
            {
                AddLog("リアクション対象のメッセージ情報が不正です");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentUserId))
            {
                AddLog("リアクションを変更できません: 表示アカウント情報が未取得です");
                return;
            }

            MainController.ShowLoadingIndicatorBusy();

            try
            {
                var channel = sender.Channel.Channel;
                var reactionState = await _slackService.HasUserReactionAsync(
                    channel.Id,
                    reactionInfo.Message.Ts,
                    reactionInfo.Reaction.Name,
                    _currentUserId);

                if (!reactionState.Success)
                {
                    AddLog($"リアクション状態の取得に失敗しました: {reactionState.Error}");
                    return;
                }

                var updateResult = reactionState.HasReacted
                    ? await _slackService.RemoveReactionAsync(channel.Id, reactionInfo.Message.Ts, reactionInfo.Reaction.Name)
                    : await _slackService.AddReactionAsync(channel.Id, reactionInfo.Message.Ts, reactionInfo.Reaction.Name);

                if (!updateResult.Success)
                {
                    AddLog($"リアクションの変更に失敗しました: {updateResult.Error}");
                    return;
                }

                await RefreshChannelAsync(channel);
                AddLog(reactionState.HasReacted
                    ? $"リアクションを削除しました :{reactionInfo.Reaction.Name}: #{channel.Name}"
                    : $"リアクションを追加しました :{reactionInfo.Reaction.Name}: #{channel.Name}");
            }
            finally
            {
                MainController.SetLoadingIndicatorIdle();
            }
        }

        private void ImagePreviewCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideImagePreview();
        }

        private void ImagePreviewOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                HideImagePreview();
                e.Handled = true;
            }
        }

        private void ShowImagePreview(ImageSource imageSource)
        {
            ImagePreviewImage.Source = imageSource;
            ImagePreviewOverlay.Visibility = Visibility.Visible;
            ImagePreviewCloseButton.Focus(FocusState.Programmatic);
        }

        private void HideImagePreview()
        {
            ImagePreviewImage.Source = null;
            ImagePreviewOverlay.Visibility = Visibility.Collapsed;
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

        // AppendPlainTextInline, CreateStyledInline, CreateStyledRun, AppendEmojiInline は
        // MessageRenderContext に移動済み

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

                // 全絵文字のエイリアスチェーンを事前解決し、MessageRenderContext に設定
                var resolvedUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _customEmojiMap)
                {
                    var resolved = ResolveEmojiUrl(kvp.Key);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        resolvedUrls[kvp.Key] = resolved;
                    }
                }

                Services.MessageRenderContext.Current.ResolvedEmojiUrls = resolvedUrls;
                AddLog($"絵文字URLを {resolvedUrls.Count} 件事前解決");
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

        private async Task<List<ChannelWithMessages>> LoadChannelBatchAsync(
            IReadOnlyList<SlackNet.Conversation> channels,
            string? workspaceUrl,
            Dictionary<string, string?>? oldestByChannelId = null)
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentMessageLoads);

            var tasks = channels.Select(async channel =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    // 差分更新: oldest が指定されていれば差分取得
                    string? oldest = null;
                    oldestByChannelId?.TryGetValue(channel.Id, out oldest);

                    var messages = await _slackService.GetChannelMessagesAsync(channel.Id, 10, oldest: oldest).ConfigureAwait(false);

                    // スレッド返信を並列取得（逐次 foreach + await → Task.WhenAll）
                    var threadMessages = messages
                        .Where(message => !string.IsNullOrWhiteSpace(message.Ts) && GetReplyCount(message) > 0)
                        .ToList();

                    var threadTasks = threadMessages.Select(async message =>
                    {
                        var replies = await _slackService.GetThreadRepliesAsync(channel.Id, message.Ts!).ConfigureAwait(false);
                        return (Ts: message.Ts!, Replies: replies);
                    }).ToList();

                    var threadResults = await Task.WhenAll(threadTasks).ConfigureAwait(false);

                    var threadRepliesByParentTs = threadResults
                        .Where(result => result.Replies.Count > 0)
                        .ToDictionary(result => result.Ts, result => result.Replies, StringComparer.OrdinalIgnoreCase);

                    var allMessages = messages
                        .Concat(threadRepliesByParentTs.Values.SelectMany(replyMessages => replyMessages))
                        .ToList();

                    var userImageTasks = allMessages
                        .Select(message => message.User)
                        .Where(userId => !string.IsNullOrWhiteSpace(userId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(userId => userId!, userId => _slackService.GetUserImageUrlAsync(userId));

                    await Task.WhenAll(userImageTasks.Values).ConfigureAwait(false);

                    // Task.Result → await 済み Task から安全に結果取得
                    var userImageResults = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in userImageTasks)
                    {
                        userImageResults[kvp.Key] = await kvp.Value.ConfigureAwait(false);
                    }

                    var displayMessages = messages
                        .Select(message => new MessageDisplayItem(
                            message,
                            channel.Id,
                            workspaceUrl,
                            !string.IsNullOrWhiteSpace(message.User) && userImageResults.TryGetValue(message.User, out var imageUrl)
                                ? imageUrl
                                : null,
                            threadRepliesByParentTs.TryGetValue(message.Ts ?? string.Empty, out var replies)
                                ? replies
                                    .Select(reply => new MessageDisplayItem(
                                        reply,
                                        channel.Id,
                                        workspaceUrl,
                                        !string.IsNullOrWhiteSpace(reply.User) && userImageResults.TryGetValue(reply.User, out var replyImageUrl)
                                            ? replyImageUrl
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

                // Emoji と UserAvatar は互いに独立しているため並列取得
                await Task.WhenAll(LoadCustomEmojiAsync(), LoadUserAvatarAsync());
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

                // 差分更新: 既存チャンネルの最終取得タイムスタンプを収集
                var oldestByChannelId = activeChannels
                    .Where(c => !string.IsNullOrWhiteSpace(c.LastFetchedTs))
                    .ToDictionary(c => c.Channel.Id, c => c.LastFetchedTs);

                var refreshedChannels = await LoadChannelBatchAsync(
                    activeChannels.Select(channel => channel.Channel).ToList(),
                    _slackService.GetWorkspaceUrl(),
                    oldestByChannelId);

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

        private async Task RefreshChannelAsync(Conversation channel)
        {
            var refreshedChannels = await LoadChannelBatchAsync(new List<Conversation> { channel }, _slackService.GetWorkspaceUrl());
            var refreshedChannel = refreshedChannels.FirstOrDefault();
            if (refreshedChannel == null)
            {
                return;
            }

            UpsertChannelAcrossBoards(refreshedChannel);
            UpdateChannelInCustomBoards(refreshedChannel);

            if (_currentChannelDisplayFilter == ChannelDisplayFilter.CustomOnly)
            {
                ApplyActiveCustomBoard();
                RefreshDisplayedChannelsFromFilter();
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

        private void UpdateChannelInCustomBoards(ChannelWithMessages refreshedChannel)
        {
            foreach (var customBoard in _customBoards)
            {
                if (!customBoard.SelectedChannelNames.Any(name => string.Equals(name, refreshedChannel.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                customBoard.Channels = OrderCustomChannels(
                    customBoard.SelectedChannelNames,
                    customBoard.Channels
                        .Where(channel => !string.Equals(channel.Channel.Id, refreshedChannel.Channel.Id, StringComparison.OrdinalIgnoreCase))
                        .Append(refreshedChannel));
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
                HideStartupSplash();
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
                HideStartupSplash();
            }
            else
            {
                AuthenticationPanel.StatusMessage = "認証に失敗しました。トークンを確認してください。";
                AuthenticationPanel.StatusBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
                UpdateAutoRefreshTimerState();
                HideStartupSplash();
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

                    // バッチ更新: 複数チャンネルの挿入を1回の CollectionChanged(Reset) にまとめる
                    _allDisplayedChannels.BeginBatchUpdate();
                    _joinedDisplayedChannels.BeginBatchUpdate();
                    _notJoinedDisplayedChannels.BeginBatchUpdate();

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

                    _allDisplayedChannels.EndBatchUpdate();
                    _joinedDisplayedChannels.EndBatchUpdate();
                    _notJoinedDisplayedChannels.EndBatchUpdate();
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

        private void MainController_MessageIconClick(object sender, RoutedEventArgs e)
        {
        }

        private async void MainController_MessageCommentRequested(object sender, RoutedEventArgs e)
        {
            if (!_slackService.IsAuthenticated)
            {
                AddLog("コメントを投稿できません: 未認証です");
                return;
            }

            var channelName = MainController.PendingMessageChannelName;
            if (string.IsNullOrWhiteSpace(channelName))
            {
                AddLog("コメント投稿先のチャンネル名を入力してください");
                return;
            }

            var messageBody = MainController.PendingMessageBody?.Trim();
            if (string.IsNullOrWhiteSpace(messageBody))
            {
                AddLog("投稿するコメントを入力してください");
                return;
            }

            if (!_allUnarchivedChannelsByName.TryGetValue(channelName, out var channel) || string.IsNullOrWhiteSpace(channel.Id))
            {
                AddLog($"投稿先チャンネルが見つかりません: #{channelName}");
                return;
            }

            MainController.ShowLoadingIndicatorBusy();

            try
            {
                AddLog($"チャンネル #{channelName} にコメントを投稿中...");
                var result = await _slackService.PostMessageAsync(channel.Id, messageBody);
                if (!result.Success)
                {
                    AddLog($"コメント投稿に失敗しました: {result.Error ?? "unknown_error"}");
                    return;
                }

                AddLog($"チャンネル #{channelName} にコメントを投稿しました");
                MainController.HideMessagePopup();
            }
            finally
            {
                MainController.SetLoadingIndicatorIdle();
            }
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
