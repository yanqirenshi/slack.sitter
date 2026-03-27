using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using System.Linq;
using SlackSitter.Converters;
using SlackSitter.Models;

namespace SlackSitter.Views
{
    public sealed partial class ChannelCardView : UserControl
    {
        private static readonly BooleanToHeaderBrushConverter HeaderBrushConverter = new BooleanToHeaderBrushConverter();
        private const double DefaultCardWidth = 300d;
        private const double ThreadedCardWidth = 356d;

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

        /// <summary>
        /// 画像表示イベント（ユーザー操作起点のため維持）
        /// </summary>
        public event TypedEventHandler<ChannelCardView, Button>? ShowImageRequested;

        public ChannelCardView()
        {
            InitializeComponent();
            Width = DefaultCardWidth;
        }

        private static void OnChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ChannelCardView view)
            {
                view.UpdateContent();
            }
        }

        /// <summary>
        /// Channel プロパティの変更時に XAML 要素を更新する。
        /// UI 構造は XAML で定義済みなので、データバインディングのみ行う。
        /// </summary>
        private void UpdateContent()
        {
            if (Channel == null)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;

            // カード幅の動的計算（スレッド有無で切替）
            var cardWidth = Channel.Messages.Any(message => message.Replies.Count > 0)
                ? ThreadedCardWidth
                : DefaultCardWidth;
            Width = cardWidth;
            OuterBorder.Width = cardWidth;

            // ヘッダーの更新
            HeaderBorder.Background = HeaderBrushConverter.Convert(Channel.IsMember, typeof(Brush), null, string.Empty) as Brush;
            HeaderLink.NavigateUri = Channel.ChannelUri;
            HeaderText.Text = Channel.Name;

            // メッセージ一覧の更新
            MessagesPanel.Children.Clear();
            foreach (var message in Channel.Messages)
            {
                var messageItemView = new MessageItemView
                {
                    Message = message
                };
                messageItemView.ShowImageRequested += MessageItemView_ShowImageRequested;
                MessagesPanel.Children.Add(messageItemView);
            }
        }

        private void MessageItemView_ShowImageRequested(MessageItemView sender, Button button)
        {
            ShowImageRequested?.Invoke(this, button);
        }
    }
}
