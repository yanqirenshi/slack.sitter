using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SlackSitter.Views
{
    public sealed partial class MainControllerView : UserControl
    {
        private const double CustomChannelButtonsBaseOffset = 590d;
        private const double CustomChannelButtonSpacing = 118d;
        private readonly Brush _defaultAccentBrush;
        private readonly Brush _defaultPrimaryTextBrush;
        private readonly List<string> _allAvailableChannels = new();
        private readonly List<CircleActionButtonView> _customChannelButtons = new();
        private bool _areCustomChannelButtonsVisible;
        private bool _areUserActionButtonsVisible;

        public event RoutedEventHandler? GearIconClick;
        public event RoutedEventHandler? PlusIconClick;
        public event RoutedEventHandler? CircleIcon1Click;
        public event RoutedEventHandler? CircleIcon2Click;
        public event RoutedEventHandler? CustomChannelClick;
        public event RoutedEventHandler? AddChannelsRequested;
        public event RoutedEventHandler? CopyLogRequested;
        public event RoutedEventHandler? RefreshRequested;
        public event TypedEventHandler<UserPopupView, string>? UpdateTokenRequested;
        public event RoutedEventHandler? LogoutRequested;
        public event RoutedEventHandler? AutoRefreshToggleRequested;

        public ObservableCollection<string> FilteredAvailableChannels { get; } = new();
        public ObservableCollection<string> SelectedChannels { get; } = new();
        public string PendingCustomBoardName => PlusChannelNameTextBox.Text?.Trim() ?? string.Empty;

        public MainControllerView()
        {
            InitializeComponent();
            _defaultAccentBrush = GetThemeBrush("AccentFillColorDefaultBrush", new SolidColorBrush(Microsoft.UI.Colors.Blue));
            _defaultPrimaryTextBrush = GetThemeBrush("TextFillColorPrimaryBrush", new SolidColorBrush(Microsoft.UI.Colors.Black));

            ActivityLogPopupBorder.SetTitle("ログ");
            ActivityLogPopupBorder.SetCopyVisible(true);
            ActivityLogPopupBorder.SetLogContentVisible(true);
            ActivityLogPopupBorder.SetRefreshVisible(false);
            ActivityLogPopupBorder.Visibility = Visibility.Collapsed;

            UserPopupBorder.UpdateTokenRequested += UserPopupBorder_UpdateTokenRequested;
            UserPopupBorder.LogoutRequested += UserPopupBorder_LogoutRequested;
            UserPopupBorder.AutoRefreshToggleRequested += UserPopupBorder_AutoRefreshToggleRequested;
            ActivityLogPopupBorder.CopyLogRequested += LogPopupBorder_CopyLogRequested;
            SelectedChannels.CollectionChanged += SelectedChannels_CollectionChanged;
            UpdateAddButtonState();
        }

        public void SetLogItemsSource(object itemsSource)
        {
            ActivityLogPopupBorder.SetLogItemsSource(itemsSource);
        }

        public void SetAvailableChannels(IEnumerable<string> channelNames)
        {
            _allAvailableChannels.Clear();
            _allAvailableChannels.AddRange(channelNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            RefreshAvailableChannels();
        }

        public void ResetPlusPopupInputs()
        {
            PlusChannelNameTextBox.Text = string.Empty;
            PlusChannelFilterTextBox.Text = string.Empty;
            AvailableChannelsListView.SelectedItem = null;
            SelectedChannelsListView.SelectedItem = null;
            SelectedChannels.Clear();
            RefreshAvailableChannels();
        }

        public void SetSelectedChannels(IEnumerable<string> channelNames)
        {
            SelectedChannels.Clear();

            foreach (var channelName in channelNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                SelectedChannels.Add(channelName);
            }

            RefreshAvailableChannels();
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
            PlusPopupBorder.Visibility = Visibility.Collapsed;
            UserPopupBorder.Visibility = Visibility.Collapsed;
            ActivityLogPopupBorder.Visibility = Visibility.Collapsed;
        }

        public void ShowUserActionButtons()
        {
            _areUserActionButtonsVisible = true;
            GearIconButton.Visibility = Visibility.Visible;
            UserAvatarButton.Visibility = Visibility.Visible;
            PlusIconButton.Visibility = Visibility.Visible;
            CircleIcon1Button.Visibility = Visibility.Visible;
            CircleIcon2Button.Visibility = Visibility.Visible;
            UpdateCustomChannelButtonsVisibility();
        }

        public void HideUserActionButtons()
        {
            _areUserActionButtonsVisible = false;
            GearIconButton.Visibility = Visibility.Collapsed;
            UserAvatarButton.Visibility = Visibility.Collapsed;
            PlusIconButton.Visibility = Visibility.Collapsed;
            CircleIcon1Button.Visibility = Visibility.Collapsed;
            CircleIcon2Button.Visibility = Visibility.Collapsed;

            foreach (var button in _customChannelButtons)
            {
                button.Visibility = Visibility.Collapsed;
            }
        }

        public void Reset()
        {
            SetUserAvatarImage(null);
            HideUserActionButtons();
            SetFilterButtonState(false, false);
            UserPopupBorder.Reset();
            SelectedChannels.Clear();
            PlusChannelFilterTextBox.Text = string.Empty;
            RefreshAvailableChannels();
            HideAllPopups();
            _areCustomChannelButtonsVisible = false;
            CustomChannelButtonsHost.Children.Clear();
            _customChannelButtons.Clear();
            LogIconButton.Visibility = Visibility.Collapsed;
            LoadingIndicatorButton.Visibility = Visibility.Collapsed;
            LoadingIndicatorButton.InnerBorderBrush = _defaultAccentBrush;
            LoadingIndicatorButton.ContentForeground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public IReadOnlyList<string> SelectedChannelNames => SelectedChannels.ToList();

        public void SetCustomChannelButtons(IReadOnlyList<string> customBoardNames, int? selectedIndex = null)
        {
            CustomChannelButtonsHost.Children.Clear();
            _customChannelButtons.Clear();

            for (int i = 0; i < customBoardNames.Count; i++)
            {
                var button = new CircleActionButtonView
                {
                    Diameter = 48,
                    IsCircular = false,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(CustomChannelButtonsBaseOffset + (CustomChannelButtonSpacing * i), 0, 0, 20),
                    InnerBorderBrush = _defaultAccentBrush,
                    ContentForeground = _defaultPrimaryTextBrush,
                    CenterText = GetCustomBoardButtonText(i),
                    Tag = i
                };

                var boardName = customBoardNames[i];
                if (!string.IsNullOrWhiteSpace(boardName))
                {
                    ToolTipService.SetToolTip(button, boardName);
                }

                button.Click += CustomChannelButton_Click;
                _customChannelButtons.Add(button);
                CustomChannelButtonsHost.Children.Add(button);
            }

            UpdateCustomChannelButtonsSelection(selectedIndex);
            UpdateCustomChannelButtonsVisibility();
        }

        public void SetCustomChannelButtonVisible(bool isVisible)
        {
            _areCustomChannelButtonsVisible = isVisible;
            UpdateCustomChannelButtonsVisibility();
        }

        public void SetFilterButtonState(bool isJoinedOnlySelected, bool isNotJoinedOnlySelected, bool isCustomSelected = false, int? selectedCustomIndex = null)
        {
            var selectedBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);

            CircleIcon1Button.InnerBorderBrush = isJoinedOnlySelected ? selectedBrush : _defaultAccentBrush;
            CircleIcon1Button.ContentForeground = isJoinedOnlySelected ? selectedBrush : _defaultPrimaryTextBrush;

            CircleIcon2Button.InnerBorderBrush = isNotJoinedOnlySelected ? selectedBrush : _defaultAccentBrush;
            CircleIcon2Button.ContentForeground = isNotJoinedOnlySelected ? selectedBrush : _defaultPrimaryTextBrush;
            UpdateCustomChannelButtonsSelection(isCustomSelected ? selectedCustomIndex : null);
        }

        private void LogIconButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePopup(ActivityLogPopupBorder, sender as CircleActionButtonView, UserPopupBorder, PlusPopupBorder);
        }

        private void LoadingIndicatorButton_Click(object sender, RoutedEventArgs e)
        {
            HideAllPopups();
            RefreshRequested?.Invoke(sender, e);
        }

        private void UserAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePopup(UserPopupBorder, sender as CircleActionButtonView, ActivityLogPopupBorder, PlusPopupBorder);
        }

        private void GearIconButton_Click(object sender, RoutedEventArgs e)
        {
            GearIconClick?.Invoke(sender, e);
        }

        private void PlusIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlusPopupBorder.Visibility != Visibility.Visible)
            {
                ResetPlusPopupInputs();
            }

            TogglePopup(PlusPopupBorder, sender as CircleActionButtonView, UserPopupBorder, ActivityLogPopupBorder);
            PlusIconClick?.Invoke(sender, e);
        }

        private void PlusChannelFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshAvailableChannels();
        }

        private void AvailableChannelsListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (AvailableChannelsListView.SelectedItem is not string channelName)
            {
                return;
            }

            if (SelectedChannels.Any(name => string.Equals(name, channelName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            SelectedChannels.Add(channelName);
            AvailableChannelsListView.SelectedItem = null;
            RefreshAvailableChannels();
        }

        private void SelectedChannelsListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (SelectedChannelsListView.SelectedItem is not string channelName)
            {
                return;
            }

            SelectedChannels.Remove(channelName);
            SelectedChannelsListView.SelectedItem = null;
            RefreshAvailableChannels();
        }

        private void SelectedChannels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateAddButtonState();
        }

        private void PlusCancelButton_Click(object sender, RoutedEventArgs e)
        {
            PlusPopupBorder.Visibility = Visibility.Collapsed;
        }

        private void AddChannelsButton_Click(object sender, RoutedEventArgs e)
        {
            AddChannelsRequested?.Invoke(this, e);
        }

        private void CircleIcon1Button_Click(object sender, RoutedEventArgs e)
        {
            CircleIcon1Click?.Invoke(sender, e);
        }

        private void CircleIcon2Button_Click(object sender, RoutedEventArgs e)
        {
            CircleIcon2Click?.Invoke(sender, e);
        }

        private void CustomChannelButton_Click(object sender, RoutedEventArgs e)
        {
            CustomChannelClick?.Invoke(sender, e);
        }

        private void UpdateCustomChannelButtonsSelection(int? selectedIndex)
        {
            var selectedBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);

            for (int i = 0; i < _customChannelButtons.Count; i++)
            {
                var isSelected = selectedIndex.HasValue && selectedIndex.Value == i;
                _customChannelButtons[i].InnerBorderBrush = isSelected ? selectedBrush : _defaultAccentBrush;
                _customChannelButtons[i].ContentForeground = isSelected ? selectedBrush : _defaultPrimaryTextBrush;
            }
        }

        private void UpdateCustomChannelButtonsVisibility()
        {
            var isVisible = _areCustomChannelButtonsVisible && _areUserActionButtonsVisible && _customChannelButtons.Count > 0;

            foreach (var button in _customChannelButtons)
            {
                button.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static string GetCustomBoardButtonText(int index)
        {
            string[] labels = ["③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩", "⑪", "⑫"];
            return index >= 0 && index < labels.Length ? labels[index] : (index + 3).ToString();
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
                case PopupBubbleView popupBubble:
                    popupBubble.PointerHorizontalOffset = pointerOffset;
                    break;
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

        private void RefreshAvailableChannels()
        {
            var filter = PlusChannelFilterTextBox?.Text?.Trim() ?? string.Empty;
            var selectedChannelSet = SelectedChannels.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var filteredChannels = _allAvailableChannels
                .Where(name => !selectedChannelSet.Contains(name))
                .Where(name => string.IsNullOrWhiteSpace(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            FilteredAvailableChannels.Clear();
            foreach (var channelName in filteredChannels)
            {
                FilteredAvailableChannels.Add(channelName);
            }
        }

        private void UpdateAddButtonState()
        {
            AddChannelsButton.IsEnabled = SelectedChannels.Count > 0;
        }

        private static Brush GetThemeBrush(string resourceKey, Brush fallback)
        {
            return Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
                ? brush
                : fallback;
        }
    }
}
