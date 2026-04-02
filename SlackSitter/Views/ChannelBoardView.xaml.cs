using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SlackSitter.Views
{
    public sealed partial class ChannelBoardView : UserControl
    {
        /// <summary>
        /// 画像表示イベント（ユーザー操作起点のため維持）
        /// </summary>
        public event TypedEventHandler<ChannelCardView, Button>? ShowImageRequested;
        public event TypedEventHandler<ChannelCardView, ImageSource>? ImagePreviewRequested;

        public ChannelBoardView()
        {
            InitializeComponent();
        }

        public void SetItemsSource(object? itemsSource)
        {
            TimesChannelsItemsRepeater.ItemsSource = itemsSource;
        }

        private void ChannelScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            var availableHeight = scrollViewer.ActualHeight - 40;
            TimesChannelsItemsRepeater.Height = availableHeight;
        }

        private void ChannelCardView_ShowImageRequested(ChannelCardView sender, Button button)
        {
            ShowImageRequested?.Invoke(sender, button);
        }

        private void ChannelCardView_ImagePreviewRequested(ChannelCardView sender, ImageSource imageSource)
        {
            ImagePreviewRequested?.Invoke(sender, imageSource);
        }
    }
}
