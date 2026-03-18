using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using SlackSitter.Models;

namespace SlackSitter.Views
{
    public sealed partial class ChannelBoardView : UserControl
    {
        public event TypedEventHandler<ChannelCardView, RichTextBlock>? MessageRichTextBlockLoadedRequested;
        public event TypedEventHandler<ChannelCardView, Border>? MessageAvatarBorderLoadedRequested;
        public event TypedEventHandler<ChannelCardView, Border>? ReactionBorderLoadedRequested;
        public event TypedEventHandler<ChannelCardView, Button>? ShowImageRequested;

        public ChannelBoardView()
        {
            InitializeComponent();
        }

        public void SetItemsSource(object? itemsSource)
        {
            TimesChannelsItemsControl.ItemsSource = itemsSource;
        }

        private void ChannelScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TimesChannelsItemsControl.ItemsPanelRoot is not StackPanel panel || sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            var availableHeight = scrollViewer.ActualHeight - 40;
            panel.Height = availableHeight;

            for (int i = 0; i < TimesChannelsItemsControl.Items.Count; i++)
            {
                var container = TimesChannelsItemsControl.ContainerFromIndex(i);
                if (container != null && VisualTreeHelper.GetChild(container, 0) is FrameworkElement element)
                {
                    element.Height = availableHeight;
                }
            }
        }

        private void ChannelCardView_MessageRichTextBlockLoadedRequested(ChannelCardView sender, RichTextBlock richTextBlock)
        {
            MessageRichTextBlockLoadedRequested?.Invoke(sender, richTextBlock);
        }

        private void ChannelCardView_MessageAvatarBorderLoadedRequested(ChannelCardView sender, Border border)
        {
            MessageAvatarBorderLoadedRequested?.Invoke(sender, border);
        }

        private void ChannelCardView_ReactionBorderLoadedRequested(ChannelCardView sender, Border border)
        {
            ReactionBorderLoadedRequested?.Invoke(sender, border);
        }

        private void ChannelCardView_ShowImageRequested(ChannelCardView sender, Button button)
        {
            ShowImageRequested?.Invoke(sender, button);
        }
    }
}
