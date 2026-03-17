using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SlackSitter.Views
{
    public sealed partial class UserPopupView : UserControl
    {
        public event TypedEventHandler<UserPopupView, string>? UpdateTokenRequested;
        public event RoutedEventHandler? LogoutRequested;
        public event RoutedEventHandler? AutoRefreshToggleRequested;

        public UserPopupView()
        {
            InitializeComponent();
        }

        public string UserNameText
        {
            get => PopupUserNameText.Text;
            set => PopupUserNameText.Text = value ?? string.Empty;
        }

        public string UserIdText
        {
            get => PopupUserIdText.Text;
            set => PopupUserIdText.Text = value ?? string.Empty;
        }

        public string EnvironmentPathText
        {
            get => EnvPathText.Text;
            set => EnvPathText.Text = value ?? string.Empty;
        }

        public string AutoRefreshStatusText
        {
            get => PopupAutoRefreshStatusText.Text;
            set => PopupAutoRefreshStatusText.Text = value ?? string.Empty;
        }

        public string AutoRefreshButtonText
        {
            get => AutoRefreshToggleButton.Content?.ToString() ?? string.Empty;
            set => AutoRefreshToggleButton.Content = value;
        }

        public void SetAvatarImage(ImageSource imageSource)
        {
            PopupAvatarBrush.ImageSource = imageSource;
        }

        public void SetUpdateTokenBusy(bool isBusy)
        {
            PopupUpdateTokenButton.IsEnabled = !isBusy;
        }

        public void ShowTokenStatus(string message, Brush foreground)
        {
            PopupTokenStatusText.Text = message;
            PopupTokenStatusText.Foreground = foreground;
            PopupTokenStatusText.Visibility = Visibility.Visible;
        }

        public void HideTokenStatus()
        {
            PopupTokenStatusText.Text = string.Empty;
            PopupTokenStatusText.Visibility = Visibility.Collapsed;
        }

        public void ClearPendingAccessToken()
        {
            PopupAccessTokenPasswordBox.Password = string.Empty;
        }

        public void Reset()
        {
            ClearPendingAccessToken();
            HideTokenStatus();
        }

        private void PopupUpdateTokenButton_Click(object sender, RoutedEventArgs e)
        {
            var newAccessToken = PopupAccessTokenPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                ShowTokenStatus("トークンを入力してください", new SolidColorBrush(Microsoft.UI.Colors.Red));
                return;
            }

            SetUpdateTokenBusy(true);
            ShowTokenStatus("認証中...", new SolidColorBrush(Microsoft.UI.Colors.Gray));
            UpdateTokenRequested?.Invoke(this, newAccessToken);
        }

        private void PopupLogoutButton_Click(object sender, RoutedEventArgs e)
        {
            LogoutRequested?.Invoke(this, e);
        }

        private void AutoRefreshToggleButton_Click(object sender, RoutedEventArgs e)
        {
            AutoRefreshToggleRequested?.Invoke(this, e);
        }
    }
}
