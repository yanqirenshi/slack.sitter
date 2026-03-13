using SlackNet.Events;
using System;

namespace SlackSitter.Models
{
    public class MessageDisplayItem
    {
        public string? User { get; }
        public string? Ts { get; }
        public string? Text { get; }
        public Uri? PermalinkUri { get; }

        public MessageDisplayItem(MessageEvent message, string channelId, string? workspaceUrl)
        {
            User = message.User;
            Ts = message.Ts;
            Text = message.Text;
            PermalinkUri = CreatePermalinkUri(workspaceUrl, channelId, message.Ts);
        }

        private static Uri? CreatePermalinkUri(string? workspaceUrl, string channelId, string? timestamp)
        {
            if (string.IsNullOrWhiteSpace(workspaceUrl) || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(timestamp))
            {
                return null;
            }

            var normalizedWorkspaceUrl = workspaceUrl.TrimEnd('/');
            var normalizedTimestamp = timestamp.Replace(".", string.Empty);

            if (Uri.TryCreate($"{normalizedWorkspaceUrl}/archives/{channelId}/p{normalizedTimestamp}", UriKind.Absolute, out var uri))
            {
                return uri;
            }

            return null;
        }
    }
}
