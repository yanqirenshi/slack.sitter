using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SlackSitter.Views
{
    public sealed partial class LogPopupView : UserControl
    {
        public event RoutedEventHandler? CopyLogRequested;
        public event RoutedEventHandler? RefreshRequested;

        public LogPopupView()
        {
            InitializeComponent();
        }

        public void SetLogItemsSource(object itemsSource)
        {
            LogItemsControl.ItemsSource = itemsSource;
        }

        public void SetTitle(string title)
        {
            PopupTitleText.Text = title;
        }

        public void SetCopyVisible(bool isVisible)
        {
            CopyLogButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetLogContentVisible(bool isVisible)
        {
            LogContentPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetRefreshVisible(bool isVisible)
        {
            RefreshDataButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetRefreshBusy(bool isBusy)
        {
            RefreshDataButton.IsEnabled = !isBusy;
            RefreshDataButton.Content = isBusy ? "再取得中..." : "再取得";
        }

        public void SetPointerHorizontalOffset(double offset)
        {
            PopupBubbleRoot.PointerHorizontalOffset = offset;
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            CopyLogRequested?.Invoke(this, e);
        }

        private void RefreshDataButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRequested?.Invoke(this, e);
        }
    }
}
