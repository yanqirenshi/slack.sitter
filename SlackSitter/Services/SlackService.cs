using SlackNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace SlackSitter.Services
{
    public class SlackService
    {
        private ISlackApiClient? _client;
        private string? _accessToken;
        private string? _workspaceUrl;
        private readonly HttpClient _httpClient = new();
        private readonly Dictionary<string, string?> _userImageUrlCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        public bool IsAuthenticated => _client != null && !string.IsNullOrEmpty(_accessToken);

        public async Task<bool> AuthenticateAsync(string accessToken)
        {
            try
            {
                _accessToken = accessToken;

                _client = new SlackServiceBuilder()
                    .UseApiToken(_accessToken)
                    .GetApiClient();

                var authTest = await _client.Auth.Test();
                _workspaceUrl = authTest.Url;
                _userImageUrlCache.Clear();

                if (!string.IsNullOrEmpty(authTest.UserId))
                {
                    return true;
                }

                _client = null;
                _accessToken = null;
                _workspaceUrl = null;
                return false;
            }
            catch
            {
                _client = null;
                _accessToken = null;
                _workspaceUrl = null;
                return false;
            }
        }

        public async Task<List<SlackNet.Conversation>> GetChannelsAsync()
        {
            if (_client == null)
            {
                return new List<SlackNet.Conversation>();
            }

            try
            {
                var channels = new List<SlackNet.Conversation>();
                string? cursor = null;

                do
                {
                    var response = await _client.Conversations.List(
                        cursor: cursor,
                        types: new[] { SlackNet.WebApi.ConversationType.PublicChannel, SlackNet.WebApi.ConversationType.PrivateChannel },
                        excludeArchived: true,
                        limit: 200
                    );

                    if (response.Channels != null)
                    {
                        channels.AddRange(response.Channels);
                    }

                    cursor = response.ResponseMetadata?.NextCursor;
                }
                while (!string.IsNullOrEmpty(cursor));

                return channels;
            }
            catch (SlackNet.SlackException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slack API エラー: {ex.Message}");
                if (ex.Message.Contains("missing_scope"))
                {
                    System.Diagnostics.Debug.WriteLine("必要な権限が不足しています。");
                    System.Diagnostics.Debug.WriteLine("Slack App の OAuth Scopes に以下の権限を追加してください：");
                    System.Diagnostics.Debug.WriteLine("  - channels:read");
                    System.Diagnostics.Debug.WriteLine("  - channels:history");
                    System.Diagnostics.Debug.WriteLine("  - groups:read");
                    System.Diagnostics.Debug.WriteLine("  - groups:history");
                    System.Diagnostics.Debug.WriteLine("  - mpim:read");
                    System.Diagnostics.Debug.WriteLine("  - mpim:history");
                    System.Diagnostics.Debug.WriteLine("  - im:read");
                    System.Diagnostics.Debug.WriteLine("  - im:history");
                    System.Diagnostics.Debug.WriteLine("  - users:read");
                    System.Diagnostics.Debug.WriteLine("  - chat:write");
                }
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting channels: {ex.Message}");
                return new List<SlackNet.Conversation>();
            }
        }

        public async IAsyncEnumerable<List<SlackNet.Conversation>> GetChannelBatchesAsync(int limit = 100)
        {
            if (_client == null)
            {
                yield break;
            }

            string? cursor = null;

            do
            {
                var response = await _client.Conversations.List(
                    cursor: cursor,
                    types: new[] { SlackNet.WebApi.ConversationType.PublicChannel, SlackNet.WebApi.ConversationType.PrivateChannel },
                    excludeArchived: true,
                    limit: limit
                );

                yield return response.Channels?.ToList() ?? new List<SlackNet.Conversation>();

                cursor = response.ResponseMetadata?.NextCursor;
            }
            while (!string.IsNullOrEmpty(cursor));
        }

        public ISlackApiClient? GetClient()
        {
            return _client;
        }

        public string? GetAccessToken()
        {
            return _accessToken;
        }

        public string? GetWorkspaceUrl()
        {
            return _workspaceUrl;
        }

        public async Task<(string? UserId, string? UserName, string? UserImageUrl)> GetCurrentUserInfoAsync()
        {
            if (_client == null)
            {
                return (null, null, null);
            }

            try
            {
                var authTest = await _client.Auth.Test();
                var userId = authTest.UserId;

                if (!string.IsNullOrEmpty(userId))
                {
                    var userInfo = await _client.Users.Info(userId);
                    var userName = userInfo.Name;
                    var userImageUrl = userInfo.Profile?.Image192 ?? userInfo.Profile?.Image72;

                    return (userId, userName, userImageUrl);
                }

                return (null, null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting user info: {ex.Message}");
                return (null, null, null);
            }
        }

        public async Task<string?> GetUserImageUrlAsync(string? userId)
        {
            if (_client == null || string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            if (_userImageUrlCache.TryGetValue(userId, out var cachedImageUrl))
            {
                return cachedImageUrl;
            }

            try
            {
                var userInfo = await _client.Users.Info(userId);
                var userImageUrl = userInfo.Profile?.Image72 ?? userInfo.Profile?.Image48 ?? userInfo.Profile?.Image32;
                _userImageUrlCache[userId] = userImageUrl;
                return userImageUrl;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting user image for {userId}: {ex.Message}");
                _userImageUrlCache[userId] = null;
                return null;
            }
        }

        public async Task<List<SlackNet.Events.MessageEvent>> GetChannelMessagesAsync(string channelId, int limit = 10)
        {
            if (_client == null)
            {
                return new List<SlackNet.Events.MessageEvent>();
            }

            try
            {
                var response = await _client.Conversations.History(channelId, limit: limit);

                if (response.Messages != null)
                {
                    return response.Messages.OrderByDescending(m => m.Ts).ToList();
                }

                return new List<SlackNet.Events.MessageEvent>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting messages for channel {channelId}: {ex.Message}");
                return new List<SlackNet.Events.MessageEvent>();
            }
        }

        public async Task<List<SlackNet.Events.MessageEvent>> GetThreadRepliesAsync(string channelId, string threadTs, int limit = 20)
        {
            if (_client == null || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(threadTs))
            {
                return new List<SlackNet.Events.MessageEvent>();
            }

            try
            {
                var response = await _client.Conversations.Replies(channelId, threadTs, limit: limit);

                if (response.Messages != null)
                {
                    return response.Messages
                        .Where(message => !string.Equals(message.Ts, threadTs, StringComparison.Ordinal))
                        .OrderBy(message => message.Ts)
                        .ToList();
                }

                return new List<SlackNet.Events.MessageEvent>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting thread replies for channel {channelId} thread {threadTs}: {ex.Message}");
                return new List<SlackNet.Events.MessageEvent>();
            }
        }

        public async Task<(Dictionary<string, string> EmojiMap, string? Error)> GetCustomEmojiAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "アクセストークンが設定されていません");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://slack.com/api/emoji.list");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);

                if (!document.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
                {
                    var error = document.RootElement.TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString()
                        : "unknown_error";
                    return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), error);
                }

                if (!document.RootElement.TryGetProperty("emoji", out var emojiElement))
                {
                    return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "emoji_not_found");
                }

                var emojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in emojiElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        emojiMap[property.Name] = property.Value.GetString() ?? string.Empty;
                    }
                }

                return (emojiMap, null);
            }
            catch (Exception ex)
            {
                return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ex.Message);
            }
        }
    }
}
