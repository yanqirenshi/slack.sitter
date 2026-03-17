using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SlackSitter.Views
{
    public sealed class CircleActionButtonView : UserControl
    {
        private readonly Button _button;
        private readonly Border _innerBorder;
        private readonly Grid _contentGrid;
        private readonly FontIcon _fontIcon;
        private readonly TextBlock _textBlock;

        public static readonly DependencyProperty DiameterProperty =
            DependencyProperty.Register(nameof(Diameter), typeof(double), typeof(CircleActionButtonView), new PropertyMetadata(48d, OnVisualPropertyChanged));

        public static readonly DependencyProperty InnerBackgroundProperty =
            DependencyProperty.Register(nameof(InnerBackground), typeof(Brush), typeof(CircleActionButtonView), new PropertyMetadata(new SolidColorBrush(Microsoft.UI.Colors.White), OnVisualPropertyChanged));

        public static readonly DependencyProperty InnerBorderBrushProperty =
            DependencyProperty.Register(nameof(InnerBorderBrush), typeof(Brush), typeof(CircleActionButtonView), new PropertyMetadata(null, OnVisualPropertyChanged));

        public static readonly DependencyProperty InnerBorderThicknessProperty =
            DependencyProperty.Register(nameof(InnerBorderThickness), typeof(Thickness), typeof(CircleActionButtonView), new PropertyMetadata(new Thickness(2), OnVisualPropertyChanged));

        public static readonly DependencyProperty CenterTextProperty =
            DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(CircleActionButtonView), new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

        public static readonly DependencyProperty CenterGlyphProperty =
            DependencyProperty.Register(nameof(CenterGlyph), typeof(string), typeof(CircleActionButtonView), new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

        public static readonly DependencyProperty ContentForegroundProperty =
            DependencyProperty.Register(nameof(ContentForeground), typeof(Brush), typeof(CircleActionButtonView), new PropertyMetadata(new SolidColorBrush(Microsoft.UI.Colors.Black), OnVisualPropertyChanged));

        public static readonly DependencyProperty ContentFontSizeProperty =
            DependencyProperty.Register(nameof(ContentFontSize), typeof(double), typeof(CircleActionButtonView), new PropertyMetadata(24d, OnVisualPropertyChanged));

        public event RoutedEventHandler? Click;

        public CircleActionButtonView()
        {
            _fontIcon = new FontIcon
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _textBlock = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            _contentGrid = new Grid();
            _contentGrid.Children.Add(_fontIcon);
            _contentGrid.Children.Add(_textBlock);

            _innerBorder = new Border
            {
                Child = _contentGrid
            };

            _button = new Button
            {
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderThickness = new Thickness(0),
                Content = _innerBorder
            };
            _button.Click += Button_Click;

            Content = _button;
            UpdateVisuals();
        }

        public double Diameter
        {
            get => (double)GetValue(DiameterProperty);
            set => SetValue(DiameterProperty, value);
        }

        public Brush? InnerBackground
        {
            get => (Brush?)GetValue(InnerBackgroundProperty);
            set => SetValue(InnerBackgroundProperty, value);
        }

        public Brush? InnerBorderBrush
        {
            get => (Brush?)GetValue(InnerBorderBrushProperty);
            set => SetValue(InnerBorderBrushProperty, value);
        }

        public Thickness InnerBorderThickness
        {
            get => (Thickness)GetValue(InnerBorderThicknessProperty);
            set => SetValue(InnerBorderThicknessProperty, value);
        }

        public string CenterText
        {
            get => (string)GetValue(CenterTextProperty);
            set => SetValue(CenterTextProperty, value);
        }

        public string CenterGlyph
        {
            get => (string)GetValue(CenterGlyphProperty);
            set => SetValue(CenterGlyphProperty, value);
        }

        public Brush? ContentForeground
        {
            get => (Brush?)GetValue(ContentForegroundProperty);
            set => SetValue(ContentForegroundProperty, value);
        }

        public double ContentFontSize
        {
            get => (double)GetValue(ContentFontSizeProperty);
            set => SetValue(ContentFontSizeProperty, value);
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircleActionButtonView view)
            {
                view.UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            Width = Diameter;
            Height = Diameter;

            _button.Width = Diameter;
            _button.Height = Diameter;
            _button.CornerRadius = new CornerRadius(Diameter / 2);

            _innerBorder.Width = Diameter;
            _innerBorder.Height = Diameter;
            _innerBorder.CornerRadius = new CornerRadius(Diameter / 2);
            _innerBorder.Background = InnerBackground;
            _innerBorder.BorderBrush = InnerBorderBrush;
            _innerBorder.BorderThickness = InnerBorderThickness;

            _fontIcon.Glyph = CenterGlyph ?? string.Empty;
            _fontIcon.FontSize = ContentFontSize;
            _fontIcon.Foreground = ContentForeground;
            _fontIcon.Visibility = string.IsNullOrWhiteSpace(CenterGlyph) ? Visibility.Collapsed : Visibility.Visible;

            _textBlock.Text = CenterText ?? string.Empty;
            _textBlock.FontSize = ContentFontSize;
            _textBlock.Foreground = ContentForeground;
            _textBlock.Visibility = string.IsNullOrWhiteSpace(CenterText) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Click?.Invoke(this, e);
        }
    }
}
