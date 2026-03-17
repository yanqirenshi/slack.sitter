using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SlackSitter.Views
{
    public sealed partial class MainControllerView : UserControl
    {
        private readonly Brush _defaultAccentBrush;
        private readonly Brush _defaultPrimaryTextBrush;

        public event RoutedEventHandler? LoadingIndicatorClick;
        public event RoutedEventHandler? GearIconClick;
        public event RoutedEventHandler? UserAvatarClick;
        public event RoutedEventHandler? PlusIconClick;
        public event RoutedEventHandler? CircleIcon1Click;
        public event RoutedEventHandler? CircleIcon2Click;

        public MainControllerView()
        {
            InitializeComponent();
            _defaultAccentBrush = GetThemeBrush("AccentFillColorDefaultBrush", new SolidColorBrush(Microsoft.UI.Colors.Blue));
            _defaultPrimaryTextBrush = GetThemeBrush("TextFillColorPrimaryBrush", new SolidColorBrush(Microsoft.UI.Colors.Black));
        }

        public void ShowLoadingIndicatorBusy()
        {
            LoadingIndicatorButton.Visibility = Visibility.Visible;
            LoadingIndicatorButton.InnerBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
            LoadingIndicatorButton.ContentForeground = new SolidColorBrush(Microsoft.UI.Colors.Red);
        }

        public void SetLoadingIndicatorIdle()
        {
            LoadingIndicatorButton.Visibility = Visibility.Visible;
            LoadingIndicatorButton.InnerBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            LoadingIndicatorButton.ContentForeground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public void SetUserAvatarImage(ImageSource? imageSource)
        {
            UserAvatarButton.InnerBackground = imageSource == null
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : new ImageBrush
                {
                    ImageSource = imageSource,
                    Stretch = Stretch.UniformToFill
                };
        }

        public void ShowUserActionButtons()
        {
            GearIconButton.Visibility = Visibility.Visible;
            UserAvatarButton.Visibility = Visibility.Visible;
            PlusIconButton.Visibility = Visibility.Visible;
            CircleIcon1Button.Visibility = Visibility.Visible;
            CircleIcon2Button.Visibility = Visibility.Visible;
        }

        public void HideUserActionButtons()
        {
            GearIconButton.Visibility = Visibility.Collapsed;
            UserAvatarButton.Visibility = Visibility.Collapsed;
            PlusIconButton.Visibility = Visibility.Collapsed;
            CircleIcon1Button.Visibility = Visibility.Collapsed;
            CircleIcon2Button.Visibility = Visibility.Collapsed;
        }

        public void Reset()
        {
            SetUserAvatarImage(null);
            HideUserActionButtons();
            SetFilterButtonState(false, false);
            LoadingIndicatorButton.Visibility = Visibility.Collapsed;
            LoadingIndicatorButton.InnerBorderBrush = _defaultAccentBrush;
            LoadingIndicatorButton.ContentForeground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public void SetFilterButtonState(bool isJoinedOnlySelected, bool isNotJoinedOnlySelected)
        {
            var selectedBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);

            CircleIcon1Button.InnerBorderBrush = isJoinedOnlySelected ? selectedBrush : _defaultAccentBrush;
            CircleIcon1Button.ContentForeground = isJoinedOnlySelected ? selectedBrush : _defaultPrimaryTextBrush;

            CircleIcon2Button.InnerBorderBrush = isNotJoinedOnlySelected ? selectedBrush : _defaultAccentBrush;
            CircleIcon2Button.ContentForeground = isNotJoinedOnlySelected ? selectedBrush : _defaultPrimaryTextBrush;
        }

        private void LoadingIndicatorButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingIndicatorClick?.Invoke(this, e);
        }

        private void UserAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            UserAvatarClick?.Invoke(this, e);
        }

        private void GearIconButton_Click(object sender, RoutedEventArgs e)
        {
            GearIconClick?.Invoke(this, e);
        }

        private void PlusIconButton_Click(object sender, RoutedEventArgs e)
        {
            PlusIconClick?.Invoke(this, e);
        }

        private void CircleIcon1Button_Click(object sender, RoutedEventArgs e)
        {
            CircleIcon1Click?.Invoke(this, e);
        }

        private void CircleIcon2Button_Click(object sender, RoutedEventArgs e)
        {
            CircleIcon2Click?.Invoke(this, e);
        }

        private static Brush GetThemeBrush(string resourceKey, Brush fallback)
        {
            return Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
                ? brush
                : fallback;
        }
    }
}
