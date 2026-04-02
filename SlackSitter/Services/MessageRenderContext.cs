using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using SlackSitter.Models;
using System.Threading.Tasks;

namespace SlackSitter.Services
{
    /// <summary>
    /// メッセージ描画に必要なリソース（絵文字マップ、画像キャッシュ）と
    /// 描画ヘルパーメソッドを提供するコンテキスト。
    /// MainWindow で初期化し、MessageItemView から参照する。
    /// </summary>
    public sealed class MessageRenderContext
    {
        public static MessageRenderContext Current { get; set; } = new();

        /// <summary>
        /// 画像キャッシュ（アバター・絵文字の BitmapImage を URL 単位でキャッシュ）
        /// </summary>
        public BitmapImageCache ImageCache { get; } = new();

        public Func<MessageImageItem, Task<BitmapImage?>>? LoadMessageImageAsync { get; set; }

        /// <summary>
        /// 事前解決済みの絵文字URL（エイリアスチェーン解決済み）
        /// key: 絵文字名, value: 画像URL
        /// </summary>
        public Dictionary<string, string> ResolvedEmojiUrls { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 絵文字名から解決済みURLを O(1) で取得する。
        /// </summary>
        public string? ResolveEmojiUrl(string emojiName)
        {
            return ResolvedEmojiUrls.TryGetValue(emojiName, out var url) ? url : null;
        }

        /// <summary>
        /// MessageDisplayItem のセグメントから RichTextBlock 用の Paragraph を構築する。
        /// </summary>
        public Paragraph BuildMessageParagraph(MessageDisplayItem message)
        {
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
                            AppendPlainTextInline(paragraph,
                                new MessageInlineSegment(MessageInlineSegmentType.Text, $":{segment.Text}:"));
                        }
                        break;
                }
            }

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new Run { Text = string.Empty });
            }

            return paragraph;
        }

        /// <summary>
        /// アバター画像を Border に設定する。キャッシュ経由で BitmapImage を取得する。
        /// </summary>
        public void PopulateAvatar(Border border, MessageDisplayItem message)
        {
            if (message.UserAvatarUri == null)
            {
                border.Visibility = Visibility.Collapsed;
                return;
            }

            border.Visibility = Visibility.Visible;
            border.Background = new ImageBrush
            {
                ImageSource = ImageCache.GetOrCreate(message.UserAvatarUri),
                Stretch = Stretch.UniformToFill
            };
        }

        /// <summary>
        /// リアクションの内容（絵文字画像 or テキスト + カウント）を Border に設定する。
        /// </summary>
        public void PopulateReaction(Border border, MessageReactionItem reaction)
        {
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
                    Source = ImageCache.GetOrCreateEmoji(emojiUri)
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
                Source = ImageCache.GetOrCreateEmoji(uri)
            };

            paragraph.Inlines.Add(new InlineUIContainer { Child = image });
            return true;
        }
    }
}
