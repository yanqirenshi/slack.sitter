using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SlackSitter.Views
{
    public sealed partial class MainControllerView : UserControl
    {
        private readonly Brush _defaultAccentBrush;
        private readonly Brush _defaultPrimaryTextBrush;

        public event RoutedEventHandler? GearIconClick;
        public event RoutedEventHandler? PlusIconClick;
        public event RoutedEventHandler? CircleIcon1Click;
        public event RoutedEventHandler? CircleIcon2Click;
        public event RoutedEventHandler? CopyLogRequested;
        public event RoutedEventHandler? RefreshRequested;
        public event TypedEventHandler<UserPopupView, string>? UpdateTokenRequested;
        public event RoutedEventHandler? LogoutRequested;
        public event RoutedEventHandler? AutoRefreshToggleRequested;

        public MainControllerView()
        {
            InitializeComponent();
            _defaultAccentBrush = GetThemeBrush("AccentFillColorDefaultBrush", new SolidColorBrush(Microsoft.UI.Colors.Blue));
            _defaultPrimaryTextBrush = GetThemeBrush("TextFillColorPrimaryBrush", new SolidColorBrush(Microsoft.UI.Colors.Black));

            DataFetchPopupBorder.SetTitle("データ取得");
            DataFetchPopupBorder.SetCopyVisible(false);
            DataFetchPopupBorder.SetLogContentVisible(false);
            DataFetchPopupBorder.SetRefreshVisible(true);
            DataFetchPopupBorder.Visibility = Visibility.Collapsed;

            ActivityLogPopupBorder.SetTitle("ログ");
            ActivityLogPopupBorder.SetCopyVisible(true);
            ActivityLogPopupBorder.SetLogContentVisible(true);
            ActivityLogPopupBorder.SetRefreshVisible(false);
            ActivityLogPopupBorder.Visibility = Visibility.Collapsed;

            UserPopupBorder.UpdateTokenRequested += UserPopupBorder_UpdateTokenRequested;
            UserPopupBorder.LogoutRequested += UserPopupBorder_LogoutRequested;
            UserPopupBorder.AutoRefreshToggleRequested += UserPopupBorder_AutoRefreshToggleRequested;
            DataFetchPopupBorder.CopyLogRequested += LogPopupBorder_CopyLogRequested;
            DataFetchPopupBorder.RefreshRequested += LogPopupBorder_RefreshRequested;
            ActivityLogPopupBorder.CopyLogRequested += LogPopupBorder_CopyLogRequested;
        }

        public void SetLogItemsSource(object itemsSource)
        {
            DataFetchPopupBorder.SetLogItemsSource(itemsSource);
            ActivityLogPopupBorder.SetLogItemsSource(itemsSource);
        }

        public void ShowLoadingIndicatorBusy()
        {
            LogIconButton.Visibility = Visibility.Visible;
            LoadingIndicatorButton.Visibility = Visibility.Visible;
            LoadingIndicatorButton.InnerBorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
            LoadingIndicatorButton.ContentForeground = new SolidColorBrush(Microsoft.UI.Colors.Red);
        }

        public void SetLoadingIndicatorIdle()
        {
            LogIconButton.Visibility = Visibility.Visible;
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

            if (imageSource != null)
            {
                UserPopupBorder.SetAvatarImage(imageSource);
            }
        }

        public void SetUserInfo(string? userName, string? userId)
        {
            UserPopupBorder.UserNameText = userName ?? "Unknown";
            UserPopupBorder.UserIdText = userId ?? string.Empty;
        }

        public void SetEnvironmentPathText(string text)
        {
            UserPopupBorder.EnvironmentPathText = text;
        }

        public void SetAutoRefreshState(string buttonText, string statusText)
        {
            UserPopupBorder.AutoRefreshButtonText = buttonText;
            UserPopupBorder.AutoRefreshStatusText = statusText;
        }

        public void SetUpdateTokenBusy(bool isBusy)
        {
            UserPopupBorder.SetUpdateTokenBusy(isBusy);
        }

        public void ShowTokenStatus(string message, Brush foreground)
        {
            UserPopupBorder.ShowTokenStatus(message, foreground);
        }

        public void HideTokenStatus()
        {
            UserPopupBorder.HideTokenStatus();
        }

        public void ClearPendingAccessToken()
        {
            UserPopupBorder.ClearPendingAccessToken();
        }

        public void HideAllPopups()
        {
            UserPopupBorder.Visibility = Visibility.Collapsed;
            DataFetchPopupBorder.Visibility = Visibility.Collapsed;
            ActivityLogPopupBorder.Visibility = Visibility.Collapsed;
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
            UserPopupBorder.Reset();
            HideAllPopups();
            LogIconButton.Visibility = Visibility.Collapsed;
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

        private void LogIconButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePopup(ActivityLogPopupBorder, sender as CircleActionButtonView, UserPopupBorder, DataFetchPopupBorder);
        }

        private void LoadingIndicatorButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePopup(DataFetchPopupBorder, sender as CircleActionButtonView, UserPopupBorder, ActivityLogPopupBorder);
        }

        private void UserAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePopup(UserPopupBorder, sender as CircleActionButtonView, DataFetchPopupBorder, ActivityLogPopupBorder);
        }

        private void GearIconButton_Click(object sender, RoutedEventArgs e)
        {
            GearIconClick?.Invoke(sender, e);
        }

        private void PlusIconButton_Click(object sender, RoutedEventArgs e)
        {
            PlusIconClick?.Invoke(sender, e);
        }

        private void CircleIcon1Button_Click(object sender, RoutedEventArgs e)
        {
            CircleIcon1Click?.Invoke(sender, e);
        }

        private void CircleIcon2Button_Click(object sender, RoutedEventArgs e)
        {
            CircleIcon2Click?.Invoke(sender, e);
        }

        private void UserPopupBorder_UpdateTokenRequested(UserPopupView sender, string newAccessToken)
        {
            UpdateTokenRequested?.Invoke(sender, newAccessToken);
        }

        private void UserPopupBorder_LogoutRequested(object sender, RoutedEventArgs e)
        {
            LogoutRequested?.Invoke(sender, e);
        }

        private void UserPopupBorder_AutoRefreshToggleRequested(object sender, RoutedEventArgs e)
        {
            AutoRefreshToggleRequested?.Invoke(sender, e);
        }

        private void LogPopupBorder_CopyLogRequested(object sender, RoutedEventArgs e)
        {
            CopyLogRequested?.Invoke(sender, e);
        }

        private void LogPopupBorder_RefreshRequested(object sender, RoutedEventArgs e)
        {
            RefreshRequested?.Invoke(sender, e);
        }

        private void TogglePopup(FrameworkElement popup, CircleActionButtonView? button, params FrameworkElement[] otherPopups)
        {
            if (popup.Visibility == Visibility.Visible)
            {
                popup.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var otherPopup in otherPopups)
            {
                otherPopup.Visibility = Visibility.Collapsed;
            }

            if (button != null)
            {
                ShowPopupAtButton(popup, button);
            }
            else
            {
                popup.Visibility = Visibility.Visible;
            }
        }

        private void ShowPopupAtButton(FrameworkElement popup, CircleActionButtonView button)
        {
            popup.Visibility = Visibility.Visible;
            popup.HorizontalAlignment = HorizontalAlignment.Left;
            popup.VerticalAlignment = VerticalAlignment.Top;
            popup.UpdateLayout();

            var popupSize = MeasureElement(popup);
            var buttonOrigin = button.TransformToVisual(this).TransformPoint(new Point(0, 0));
            var buttonCenterX = buttonOrigin.X + (button.ActualWidth / 2);
            var left = Math.Clamp(
                buttonCenterX - (popupSize.Width / 2),
                0,
                Math.Max(0, ActualWidth - popupSize.Width));
            var top = Math.Max(0, buttonOrigin.Y - popupSize.Height);

            popup.Margin = new Thickness(left, top, 0, 0);
            SetPopupPointerOffset(popup, popupSize.Width, buttonCenterX - left);
        }

        private static void SetPopupPointerOffset(FrameworkElement popup, double popupWidth, double pointerCenterX)
        {
            var clampedPointerCenterX = Math.Clamp(pointerCenterX, 20d, Math.Max(20d, popupWidth - 20d));
            var pointerOffset = clampedPointerCenterX - (popupWidth / 2);

            switch (popup)
            {
                case UserPopupView userPopup:
                    userPopup.SetPointerHorizontalOffset(pointerOffset);
                    break;
                case LogPopupView logPopup:
                    logPopup.SetPointerHorizontalOffset(pointerOffset);
                    break;
            }
        }

        private static Size MeasureElement(FrameworkElement element)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = element.ActualWidth > 0 ? element.ActualWidth : element.DesiredSize.Width;
            var height = element.ActualHeight > 0 ? element.ActualHeight : element.DesiredSize.Height;
            return new Size(width, height);
        }

        private static Brush GetThemeBrush(string resourceKey, Brush fallback)
        {
            return Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
                ? brush
                : fallback;
        }
    }
}
