using SlackNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SlackSitter.Services
{
    public class SlackService
    {
        private ISlackApiClient? _client;
        private string? _accessToken;

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

                if (!string.IsNullOrEmpty(authTest.UserId))
                {
                    return true;
                }

                _client = null;
                _accessToken = null;
                return false;
            }
            catch
            {
                _client = null;
                _accessToken = null;
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
                    System.Diagnostics.Debug.WriteLine("  - groups:read");
                    System.Diagnostics.Debug.WriteLine("  - mpim:read");
                    System.Diagnostics.Debug.WriteLine("  - im:read");
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

        public ISlackApiClient? GetClient()
        {
            return _client;
        }

        public string? GetAccessToken()
        {
            return _accessToken;
        }
    }
}
