using SlackNet;
using SlackNet.Events;
using System.Collections.Generic;

namespace SlackSitter.Models
{
    public class ChannelWithMessages
    {
        public Conversation Channel { get; set; }
        public List<MessageEvent> Messages { get; set; }

        public string Name => Channel?.Name ?? string.Empty;
        public string? TopicValue => Channel?.Topic?.Value;
        public string? PurposeValue => Channel?.Purpose?.Value;
        public int? NumMembers => Channel?.NumMembers;
        public bool IsMember => Channel?.IsMember ?? false;

        public ChannelWithMessages(Conversation channel, List<MessageEvent> messages)
        {
            Channel = channel;
            Messages = messages ?? new List<MessageEvent>();
        }
    }
}
