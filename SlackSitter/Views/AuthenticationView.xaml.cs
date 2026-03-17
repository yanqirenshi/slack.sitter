using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SlackSitter.Views
{
    public sealed partial class AuthenticationView : UserControl
    {
        public static readonly DependencyProperty StatusMessageProperty =
            DependencyProperty.Register(nameof(StatusMessage), typeof(string), typeof(AuthenticationView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty StatusBrushProperty =
            DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(AuthenticationView), new PropertyMetadata(null));

        public string StatusMessage
        {
            get => (string)GetValue(StatusMessageProperty);
            set => SetValue(StatusMessageProperty, value);
        }

        public Brush StatusBrush
        {
            get => (Brush)GetValue(StatusBrushProperty);
            set => SetValue(StatusBrushProperty, value);
        }

        public event TypedEventHandler<AuthenticationView, string>? AuthenticateRequested;

        public AuthenticationView()
        {
            InitializeComponent();
        }

        public void SetToken(string token)
        {
            AccessTokenPasswordBox.Password = token;
        }

        public void ResetStatus()
        {
            StatusMessage = string.Empty;
            SetToken(string.Empty);
        }

        public void SetBusy(bool isBusy)
        {
            AuthenticateButton.IsEnabled = !isBusy;
        }

        private void AuthenticateButton_Click(object sender, RoutedEventArgs e)
        {
            var accessToken = AccessTokenPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                StatusMessage = "User OAuth Token を入力してください";
                StatusBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
                return;
            }

            AuthenticateButton.IsEnabled = false;
            StatusMessage = "認証中...";
            StatusBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

            AuthenticateRequested?.Invoke(this, accessToken);
        }
    }
}
