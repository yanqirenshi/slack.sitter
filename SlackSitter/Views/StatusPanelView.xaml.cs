using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SlackSitter.Views
{
    public sealed partial class StatusPanelView : UserControl
    {
        public StatusPanelView()
        {
            InitializeComponent();
        }

        public void ShowLoadingMessage(string message)
        {
            StatusText.Text = message;
            StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            ErrorText.Visibility = Visibility.Collapsed;
            RequiredScopesPanel.Visibility = Visibility.Collapsed;
            RootPanel.Visibility = Visibility.Visible;
        }

        public void HidePanel()
        {
            RootPanel.Visibility = Visibility.Collapsed;
        }

        public void ShowWarningMessage(string message, Brush foreground)
        {
            StatusText.Text = message;
            StatusText.Foreground = foreground;
            ErrorText.Visibility = Visibility.Collapsed;
            RequiredScopesPanel.Visibility = Visibility.Collapsed;
            RootPanel.Visibility = Visibility.Visible;
        }

        public void ShowErrorMessage(string message, bool showRequiredScopes)
        {
            StatusText.Text = string.Empty;
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            RequiredScopesPanel.Visibility = showRequiredScopes ? Visibility.Visible : Visibility.Collapsed;
            RootPanel.Visibility = Visibility.Visible;
        }
    }
}
