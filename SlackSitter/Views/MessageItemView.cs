using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using SlackSitter.Converters;
using SlackSitter.Models;
using SlackSitter.Services;

namespace SlackSitter.Views
{
    public sealed partial class MessageItemView : UserControl
    {
        private static readonly TimestampToDateTimeConverter TimestampConverter = new TimestampToDateTimeConverter();
        private const double FixedMessageCardWidth = 236d;

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(
                nameof(Message),
                typeof(MessageDisplayItem),
                typeof(MessageItemView),
                new PropertyMetadata(null, OnMessageChanged));

        public MessageDisplayItem? Message
        {
            get => (MessageDisplayItem?)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        /// <summary>
        /// 画像表示ボタンのクリックイベント（ユーザー操作起点のため維持）
        /// </summary>
        public event TypedEventHandler<MessageItemView, Button>? ShowImageRequested;

        public MessageItemView()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MessageItemView view)
            {
                view.BuildContent();
            }
        }

        private void BuildContent()
        {
            if (Message == null)
            {
                Content = null;
                return;
            }

            var renderContext = MessageRenderContext.Current;

            var rootGrid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 4)
            };

            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // タイムスタンプ
            var timestampButton = new HyperlinkButton
            {
                NavigateUri = Message.PermalinkUri,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 2)
            };
            Grid.SetRow(timestampButton, 0);
            Grid.SetColumn(timestampButton, 1);
            timestampButton.Content = new TextBlock
            {
                Text = TimestampConverter.Convert(Message.Ts, typeof(string), null, string.Empty) as string ?? Message.Ts ?? string.Empty,
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
            rootGrid.Children.Add(timestampButton);

            // アバター — Loaded ハンドラではなく直接構築
            var avatarBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Margin = new Thickness(0, 2, 4, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            renderContext.PopulateAvatar(avatarBorder, Message);
            Grid.SetRow(avatarBorder, 1);
            Grid.SetColumn(avatarBorder, 0);
            rootGrid.Children.Add(avatarBorder);

            // メッセージカード
            var messageCard = new Border
            {
                Background = GetBrush("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Width = FixedMessageCardWidth,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(messageCard, 1);
            Grid.SetColumn(messageCard, 1);

            var messageStack = new StackPanel
            {
                Spacing = 8
            };

            // RichTextBlock — Loaded ハンドラではなく直接構築
            var richTextBlock = new RichTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 18
            };
            var paragraph = renderContext.BuildMessageParagraph(Message);
            richTextBlock.Blocks.Add(paragraph);
            messageStack.Children.Add(richTextBlock);

            // 画像は直接表示し、実体化時に非同期ロードする
            if (Message.Images.Count > 0)
            {
                var imagesStack = new StackPanel
                {
                    Spacing = 8
                };

                foreach (var imageItem in Message.Images)
                {
                    var image = new Image
                    {
                        Visibility = Visibility.Collapsed,
                        MaxHeight = 240,
                        Stretch = Stretch.Uniform
                    };
                    StartInlineImageLoad(image, imageItem);

                    imagesStack.Children.Add(image);
                }

                messageStack.Children.Add(imagesStack);
            }

            messageCard.Child = messageStack;
            rootGrid.Children.Add(messageCard);

            // リアクション — Loaded ハンドラではなく直接構築
            if (Message.Reactions.Count > 0)
            {
                var reactionContainer = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                Grid.SetRow(reactionContainer, 2);
                Grid.SetColumn(reactionContainer, 1);

                foreach (var reaction in Message.Reactions)
                {
                    var reactionBorder = new Border
                    {
                        Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                        BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(6, 3, 6, 3)
                    };
                    renderContext.PopulateReaction(reactionBorder, reaction);
                    reactionContainer.Children.Add(reactionBorder);
                }

                rootGrid.Children.Add(reactionContainer);
            }

            // スレッド返信（再帰的に MessageItemView を生成）
            if (Message.Replies.Count > 0)
            {
                var repliesStack = new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                Grid.SetRow(repliesStack, 3);
                Grid.SetColumn(repliesStack, 0);
                Grid.SetColumnSpan(repliesStack, 2);

                foreach (var reply in Message.Replies)
                {
                    var replyItemView = new MessageItemView
                    {
                        Message = reply,
                        Margin = new Thickness(28, 0, 0, 0)
                    };
                    replyItemView.ShowImageRequested += (_, button) => ShowImageRequested?.Invoke(this, button);
                    repliesStack.Children.Add(replyItemView);
                }

                rootGrid.Children.Add(repliesStack);
            }

            Content = rootGrid;
        }

        private async void StartInlineImageLoad(Image image, MessageImageItem imageItem)
        {
            if (image.Source != null)
            {
                return;
            }

            var loader = MessageRenderContext.Current.LoadMessageImageAsync;
            if (loader == null)
            {
                return;
            }

            BitmapImage? bitmapImage = null;

            try
            {
                bitmapImage = await loader(imageItem);
            }
            catch
            {
            }

            if (bitmapImage != null)
            {
                image.Source = bitmapImage;
                image.Visibility = Visibility.Visible;
            }
            else
            {
                image.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowMessageImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                ShowImageRequested?.Invoke(this, button);
            }
        }

        private static Brush? GetBrush(string resourceKey)
        {
            return Application.Current.Resources.TryGetValue(resourceKey, out var resource)
                ? resource as Brush
                : null;
        }
    }
}
