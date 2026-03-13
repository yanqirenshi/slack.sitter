using SlackNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SlackSitter.Models
{
    public class ChannelWithMessages
    {
        public Conversation Channel { get; set; }
        public List<MessageDisplayItem> Messages { get; set; }
        public Uri? ChannelUri { get; }

        public string Name => Channel?.Name ?? string.Empty;
        public string? TopicValue => Channel?.Topic?.Value;
        public string? PurposeValue => Channel?.Purpose?.Value;
        public int? NumMembers => Channel?.NumMembers;
        public bool IsMember => Channel?.IsMember ?? false;
        public string? LastMessageTs => Messages?.FirstOrDefault()?.Ts;

        public ChannelWithMessages(Conversation channel, List<MessageDisplayItem> messages, string? workspaceUrl)
        {
            Channel = channel;
            Messages = messages ?? new List<MessageDisplayItem>();
            ChannelUri = CreateChannelUri(workspaceUrl, channel?.Id);
        }

        private static Uri? CreateChannelUri(string? workspaceUrl, string? channelId)
        {
            if (string.IsNullOrWhiteSpace(workspaceUrl) || string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            if (Uri.TryCreate($"{workspaceUrl.TrimEnd('/')}/archives/{channelId}", UriKind.Absolute, out var uri))
            {
                return uri;
            }

            return null;
        }
    }
}
