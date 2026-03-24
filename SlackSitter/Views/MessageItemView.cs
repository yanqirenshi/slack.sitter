using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using SlackSitter.Converters;
using SlackSitter.Models;

namespace SlackSitter.Views
{
    public sealed partial class MessageItemView : UserControl
    {
        private static readonly TimestampToDateTimeConverter TimestampConverter = new TimestampToDateTimeConverter();

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

        public event TypedEventHandler<MessageItemView, RichTextBlock>? MessageRichTextBlockLoadedRequested;
        public event TypedEventHandler<MessageItemView, Border>? MessageAvatarBorderLoadedRequested;
        public event TypedEventHandler<MessageItemView, Border>? ReactionBorderLoadedRequested;
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

            var avatarBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Margin = new Thickness(0, 2, 4, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Tag = Message
            };
            avatarBorder.Loaded += MessageAvatarBorder_Loaded;
            Grid.SetRow(avatarBorder, 1);
            Grid.SetColumn(avatarBorder, 0);
            rootGrid.Children.Add(avatarBorder);

            var messageCard = new Border
            {
                Background = GetBrush("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8)
            };
            Grid.SetRow(messageCard, 1);
            Grid.SetColumn(messageCard, 1);

            var messageStack = new StackPanel
            {
                Spacing = 8
            };

            var richTextBlock = new RichTextBlock
            {
                Tag = Message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 18
            };
            richTextBlock.Loaded += MessageRichTextBlock_Loaded;
            messageStack.Children.Add(richTextBlock);

            if (Message.Images.Count > 0)
            {
                var imagesStack = new StackPanel
                {
                    Spacing = 8
                };

                foreach (var imageItem in Message.Images)
                {
                    var imageItemStack = new StackPanel
                    {
                        Spacing = 8
                    };

                    var showImageButton = new Button
                    {
                        Content = "画像",
                        Tag = imageItem,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    showImageButton.Click += ShowMessageImageButton_Click;

                    var image = new Image
                    {
                        Tag = imageItem,
                        Visibility = Visibility.Collapsed,
                        MaxHeight = 240,
                        Stretch = Stretch.Uniform
                    };

                    imageItemStack.Children.Add(showImageButton);
                    imageItemStack.Children.Add(image);
                    imagesStack.Children.Add(imageItemStack);
                }

                messageStack.Children.Add(imagesStack);
            }

            messageCard.Child = messageStack;
            rootGrid.Children.Add(messageCard);

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
                        Tag = reaction,
                        Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                        BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(6, 3, 6, 3)
                    };
                    reactionBorder.Loaded += ReactionBorder_Loaded;
                    reactionContainer.Children.Add(reactionBorder);
                }

                rootGrid.Children.Add(reactionContainer);
            }

            if (Message.Replies.Count > 0)
            {
                var repliesStack = new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                Grid.SetRow(repliesStack, 3);
                Grid.SetColumn(repliesStack, 1);

                foreach (var reply in Message.Replies)
                {
                    var replyItemView = new MessageItemView
                    {
                        Message = reply,
                        Margin = new Thickness(16, 0, 0, 0)
                    };
                    replyItemView.MessageRichTextBlockLoadedRequested += (_, richTextBlock) => MessageRichTextBlockLoadedRequested?.Invoke(this, richTextBlock);
                    replyItemView.MessageAvatarBorderLoadedRequested += (_, border) => MessageAvatarBorderLoadedRequested?.Invoke(this, border);
                    replyItemView.ReactionBorderLoadedRequested += (_, border) => ReactionBorderLoadedRequested?.Invoke(this, border);
                    replyItemView.ShowImageRequested += (_, button) => ShowImageRequested?.Invoke(this, button);
                    repliesStack.Children.Add(replyItemView);
                }

                rootGrid.Children.Add(repliesStack);
            }

            Content = rootGrid;
        }

        private void MessageRichTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is RichTextBlock richTextBlock)
            {
                MessageRichTextBlockLoadedRequested?.Invoke(this, richTextBlock);
            }
        }

        private void MessageAvatarBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border)
            {
                MessageAvatarBorderLoadedRequested?.Invoke(this, border);
            }
        }

        private void ReactionBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border)
            {
                ReactionBorderLoadedRequested?.Invoke(this, border);
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
