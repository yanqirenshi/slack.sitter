using SlackNet.Events;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SlackSitter.Models
{
    public enum MessageInlineSegmentType
    {
        Text,
        Link,
        Emoji
    }

    public sealed class MessageInlineSegment
    {
        public MessageInlineSegmentType Type { get; }
        public string Text { get; }
        public Uri? Uri { get; }

        public MessageInlineSegment(MessageInlineSegmentType type, string text, Uri? uri = null)
        {
            Type = type;
            Text = text;
            Uri = uri;
        }
    }

    public class MessageDisplayItem
    {
        private static readonly Regex SlackLinkRegex = new Regex(@"<(?<url>https?://[^|>]+)(\|(?<label>[^>]+))?>", RegexOptions.Compiled);
        private static readonly Regex SlackEmojiRegex = new Regex(@":(?<name>[a-zA-Z0-9_+\-]+):", RegexOptions.Compiled);

        public string? User { get; }
        public string? Ts { get; }
        public string? Text { get; }
        public Uri? PermalinkUri { get; }
        public IReadOnlyList<MessageInlineSegment> Segments { get; }

        public MessageDisplayItem(MessageEvent message, string channelId, string? workspaceUrl)
        {
            User = message.User;
            Ts = message.Ts;
            Text = message.Text;
            PermalinkUri = CreatePermalinkUri(workspaceUrl, channelId, message.Ts);
            Segments = ParseSegments(message.Text);
        }

        private static IReadOnlyList<MessageInlineSegment> ParseSegments(string? sourceText)
        {
            var segments = new List<MessageInlineSegment>();
            var text = sourceText ?? string.Empty;
            var currentIndex = 0;

            foreach (Match match in SlackLinkRegex.Matches(text))
            {
                if (match.Index > currentIndex)
                {
                    AddTextAndEmojiSegments(segments, text.Substring(currentIndex, match.Index - currentIndex));
                }

                var url = match.Groups["url"].Value;
                var label = match.Groups["label"].Success ? match.Groups["label"].Value : url;

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    segments.Add(new MessageInlineSegment(MessageInlineSegmentType.Link, label, uri));
                }
                else
                {
                    AddTextAndEmojiSegments(segments, label);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                AddTextAndEmojiSegments(segments, text.Substring(currentIndex));
            }

            if (segments.Count == 0)
            {
                segments.Add(new MessageInlineSegment(MessageInlineSegmentType.Text, string.Empty));
            }

            return segments;
        }

        private static void AddTextAndEmojiSegments(List<MessageInlineSegment> segments, string text)
        {
            var currentIndex = 0;

            foreach (Match match in SlackEmojiRegex.Matches(text))
            {
                if (match.Index > currentIndex)
                {
                    segments.Add(new MessageInlineSegment(
                        MessageInlineSegmentType.Text,
                        text.Substring(currentIndex, match.Index - currentIndex)));
                }

                segments.Add(new MessageInlineSegment(MessageInlineSegmentType.Emoji, match.Groups["name"].Value));
                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                segments.Add(new MessageInlineSegment(MessageInlineSegmentType.Text, text.Substring(currentIndex)));
            }
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
