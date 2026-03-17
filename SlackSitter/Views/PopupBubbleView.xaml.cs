using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SlackSitter.Views
{
    public sealed partial class PopupBubbleView : UserControl
    {
        public static readonly DependencyProperty PopupContentProperty =
            DependencyProperty.Register(nameof(PopupContent), typeof(object), typeof(PopupBubbleView), new PropertyMetadata(null));

        public static readonly DependencyProperty BubbleBackgroundProperty =
            DependencyProperty.Register(nameof(BubbleBackground), typeof(Brush), typeof(PopupBubbleView), new PropertyMetadata(null));

        public static readonly DependencyProperty BubbleBorderBrushProperty =
            DependencyProperty.Register(nameof(BubbleBorderBrush), typeof(Brush), typeof(PopupBubbleView), new PropertyMetadata(null));

        public static readonly DependencyProperty BubbleBorderThicknessProperty =
            DependencyProperty.Register(nameof(BubbleBorderThickness), typeof(Thickness), typeof(PopupBubbleView), new PropertyMetadata(new Thickness(1)));

        public static readonly DependencyProperty BubbleCornerRadiusProperty =
            DependencyProperty.Register(nameof(BubbleCornerRadius), typeof(CornerRadius), typeof(PopupBubbleView), new PropertyMetadata(new CornerRadius(8)));

        public static readonly DependencyProperty BubblePaddingProperty =
            DependencyProperty.Register(nameof(BubblePadding), typeof(Thickness), typeof(PopupBubbleView), new PropertyMetadata(new Thickness(16)));

        public static readonly DependencyProperty BubbleMinWidthProperty =
            DependencyProperty.Register(nameof(BubbleMinWidth), typeof(double), typeof(PopupBubbleView), new PropertyMetadata(0d));

        public static readonly DependencyProperty BubbleMaxWidthProperty =
            DependencyProperty.Register(nameof(BubbleMaxWidth), typeof(double), typeof(PopupBubbleView), new PropertyMetadata(double.PositiveInfinity));

        public static readonly DependencyProperty PointerStrokeThicknessProperty =
            DependencyProperty.Register(nameof(PointerStrokeThickness), typeof(double), typeof(PopupBubbleView), new PropertyMetadata(1d));

        public PopupBubbleView()
        {
            InitializeComponent();
        }

        public object? PopupContent
        {
            get => GetValue(PopupContentProperty);
            set => SetValue(PopupContentProperty, value);
        }

        public Brush? BubbleBackground
        {
            get => (Brush?)GetValue(BubbleBackgroundProperty);
            set => SetValue(BubbleBackgroundProperty, value);
        }

        public Brush? BubbleBorderBrush
        {
            get => (Brush?)GetValue(BubbleBorderBrushProperty);
            set => SetValue(BubbleBorderBrushProperty, value);
        }

        public Thickness BubbleBorderThickness
        {
            get => (Thickness)GetValue(BubbleBorderThicknessProperty);
            set => SetValue(BubbleBorderThicknessProperty, value);
        }

        public CornerRadius BubbleCornerRadius
        {
            get => (CornerRadius)GetValue(BubbleCornerRadiusProperty);
            set => SetValue(BubbleCornerRadiusProperty, value);
        }

        public Thickness BubblePadding
        {
            get => (Thickness)GetValue(BubblePaddingProperty);
            set => SetValue(BubblePaddingProperty, value);
        }

        public double BubbleMinWidth
        {
            get => (double)GetValue(BubbleMinWidthProperty);
            set => SetValue(BubbleMinWidthProperty, value);
        }

        public double BubbleMaxWidth
        {
            get => (double)GetValue(BubbleMaxWidthProperty);
            set => SetValue(BubbleMaxWidthProperty, value);
        }

        public double PointerStrokeThickness
        {
            get => (double)GetValue(PointerStrokeThicknessProperty);
            set => SetValue(PointerStrokeThicknessProperty, value);
        }
    }
}
