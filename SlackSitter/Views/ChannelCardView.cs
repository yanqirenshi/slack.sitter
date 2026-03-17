using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using SlackSitter.Converters;
using SlackSitter.Models;

namespace SlackSitter.Views
{
    public sealed class ChannelCardView : UserControl
    {
        private static readonly BooleanToHeaderBrushConverter HeaderBrushConverter = new BooleanToHeaderBrushConverter();

        public static readonly DependencyProperty ChannelProperty =
            DependencyProperty.Register(
                nameof(Channel),
                typeof(ChannelWithMessages),
                typeof(ChannelCardView),
                new PropertyMetadata(null, OnChannelChanged));

        public ChannelWithMessages? Channel
        {
            get => (ChannelWithMessages?)GetValue(ChannelProperty);
            set => SetValue(ChannelProperty, value);
        }

        public event TypedEventHandler<ChannelCardView, RichTextBlock>? MessageRichTextBlockLoadedRequested;
        public event TypedEventHandler<ChannelCardView, Border>? MessageAvatarBorderLoadedRequested;
        public event TypedEventHandler<ChannelCardView, Border>? ReactionBorderLoadedRequested;
        public event TypedEventHandler<ChannelCardView, Button>? ShowImageRequested;

        public ChannelCardView()
        {
            Width = 300;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        private static void OnChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ChannelCardView view)
            {
                view.BuildContent();
            }
        }

        private void BuildContent()
        {
            if (Channel == null)
            {
                Content = null;
                return;
            }

            var outerBorder = new Border
            {
                Background = GetBrush("LayerFillColorDefaultBrush"),
                BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var headerBorder = new Border
            {
                Background = HeaderBrushConverter.Convert(Channel.IsMember, typeof(Brush), null, string.Empty) as Brush,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(16, 12, 16, 12)
            };
            Grid.SetRow(headerBorder, 0);

            var headerLink = new HyperlinkButton
            {
                NavigateUri = Channel.ChannelUri,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = Channel.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            headerBorder.Child = headerLink;
            rootGrid.Children.Add(headerBorder);

            var contentScrollViewer = new ScrollViewer
            {
                Padding = new Thickness(12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(contentScrollViewer, 1);

            var messagesStack = new StackPanel();
            foreach (var message in Channel.Messages)
            {
                var messageItemView = new MessageItemView
                {
                    Message = message
                };
                messageItemView.MessageRichTextBlockLoadedRequested += MessageItemView_MessageRichTextBlockLoadedRequested;
                messageItemView.MessageAvatarBorderLoadedRequested += MessageItemView_MessageAvatarBorderLoadedRequested;
                messageItemView.ReactionBorderLoadedRequested += MessageItemView_ReactionBorderLoadedRequested;
                messageItemView.ShowImageRequested += MessageItemView_ShowImageRequested;
                messagesStack.Children.Add(messageItemView);
            }

            contentScrollViewer.Content = messagesStack;
            rootGrid.Children.Add(contentScrollViewer);

            outerBorder.Child = rootGrid;
            Content = outerBorder;
        }

        private void MessageItemView_MessageRichTextBlockLoadedRequested(MessageItemView sender, RichTextBlock richTextBlock)
        {
            MessageRichTextBlockLoadedRequested?.Invoke(this, richTextBlock);
        }

        private void MessageItemView_MessageAvatarBorderLoadedRequested(MessageItemView sender, Border border)
        {
            MessageAvatarBorderLoadedRequested?.Invoke(this, border);
        }

        private void MessageItemView_ReactionBorderLoadedRequested(MessageItemView sender, Border border)
        {
            ReactionBorderLoadedRequested?.Invoke(this, border);
        }

        private void MessageItemView_ShowImageRequested(MessageItemView sender, Button button)
        {
            ShowImageRequested?.Invoke(this, button);
        }

        private static Brush? GetBrush(string resourceKey)
        {
            return Application.Current.Resources.TryGetValue(resourceKey, out var resource)
                ? resource as Brush
                : null;
        }
    }
}
