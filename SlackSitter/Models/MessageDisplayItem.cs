using SlackNet.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    public sealed class MessageImageItem
    {
        public IReadOnlyList<string> CandidateUrls { get; }

        public MessageImageItem(IEnumerable<string> candidateUrls)
        {
            CandidateUrls = candidateUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public class MessageDisplayItem
    {
        private static readonly Regex SlackLinkRegex = new Regex(@"<(?<url>https?://[^|>]+)(\|(?<label>[^>]+))?>", RegexOptions.Compiled);
        private static readonly Regex SlackEmojiRegex = new Regex(@":(?<name>[a-zA-Z0-9_+\-]+):", RegexOptions.Compiled);

        public string? User { get; }
        public string? Ts { get; }
        public string? Text { get; }
        public Uri? UserAvatarUri { get; }
        public Uri? PermalinkUri { get; }
        public IReadOnlyList<MessageInlineSegment> Segments { get; }
        public IReadOnlyList<MessageImageItem> Images { get; }

        public MessageDisplayItem(MessageEvent message, string channelId, string? workspaceUrl, string? userAvatarUrl = null)
        {
            User = message.User;
            Ts = message.Ts;
            Text = message.Text;
            UserAvatarUri = CreateUri(userAvatarUrl);
            PermalinkUri = CreatePermalinkUri(workspaceUrl, channelId, message.Ts);
            Segments = ParseSegments(message.Text);
            Images = ExtractImages(message);
        }

        private static Uri? CreateUri(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri;
        }

        private static IReadOnlyList<MessageImageItem> ExtractImages(MessageEvent message)
        {
            var images = new List<MessageImageItem>();

            if (message.Files != null)
            {
                foreach (var file in message.Files)
                {
                    if (file == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(file.Mimetype) && file.Mimetype.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        AddImageItem(images,
                            file.Thumb360,
                            file.Thumb480,
                            file.Thumb160,
                            file.Thumb80,
                            file.Thumb64,
                            file.UrlPrivateDownload,
                            file.UrlPrivate);
                    }
                }
            }

            AddImageItemsFromObjects(images, GetEnumerablePropertyValue(message, "Attachments"));
            AddImageItemsFromObjects(images, GetEnumerablePropertyValue(message, "Blocks"));

            return images
                .Where(image => image.CandidateUrls.Count > 0)
                .ToList();
        }

        private static void AddImageItemsFromObjects(List<MessageImageItem> images, IEnumerable<object> objects)
        {
            foreach (var item in objects)
            {
                AddImageItem(images,
                    GetStringPropertyValue(item, "ImageUrl"),
                    GetStringPropertyValue(item, "ImageOriginal"),
                    GetStringPropertyValue(item, "ThumbnailUrl"),
                    GetStringPropertyValue(item, "ThumbUrl"),
                    GetStringPropertyValue(item, "Url"));

                var nestedImage = GetPropertyValue(item, "Image");
                if (nestedImage != null)
                {
                    AddImageItem(images,
                        GetStringPropertyValue(nestedImage, "ImageUrl"),
                        GetStringPropertyValue(nestedImage, "ImageOriginal"),
                        GetStringPropertyValue(nestedImage, "ThumbnailUrl"),
                        GetStringPropertyValue(nestedImage, "ThumbUrl"),
                        GetStringPropertyValue(nestedImage, "Url"));
                }
            }
        }

        private static void AddImageItem(List<MessageImageItem> images, params string?[] candidates)
        {
            var imageItem = new MessageImageItem(candidates!.OfType<string>());
            if (imageItem.CandidateUrls.Count > 0)
            {
                images.Add(imageItem);
            }
        }

        private static IEnumerable<object> GetEnumerablePropertyValue(object source, string propertyName)
        {
            var propertyValue = GetPropertyValue(source, propertyName);
            if (propertyValue is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        yield return item;
                    }
                }
            }
        }

        private static object? GetPropertyValue(object source, string propertyName)
        {
            return source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);
        }

        private static string? GetStringPropertyValue(object source, string propertyName)
        {
            return GetPropertyValue(source, propertyName) as string;
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

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    segments.Add(new MessageInlineSegment(MessageInlineSegmentType.Link, GetLinkDisplayText(uri), uri));
                }
                else
                {
                    var label = match.Groups["label"].Success ? match.Groups["label"].Value : url;
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

        private static string GetLinkDisplayText(Uri uri)
        {
            return string.IsNullOrWhiteSpace(uri.Host) ? uri.ToString() : uri.Host;
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
