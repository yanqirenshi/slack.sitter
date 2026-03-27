using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SlackSitter.Views
{
    /// <summary>
    /// 水平方向の仮想化レイアウト。
    /// ItemsRepeater と組み合わせて使用し、ビューポート内のアイテムのみを実体化する。
    /// 固定幅のカードを水平に並べるボード表示に最適化されている。
    /// </summary>
    public sealed class HorizontalVirtualizingLayout : VirtualizingLayout
    {
        /// <summary>
        /// カード間のスペース（ピクセル）
        /// </summary>
        public double Spacing { get; set; } = 16;

        /// <summary>
        /// アイテムの推定幅（ピクセル）。レイアウト計算に使用する。
        /// </summary>
        public double EstimatedItemWidth { get; set; } = 356;

        private readonly Dictionary<int, double> _measuredWidths = new();

        protected override void InitializeForContextCore(VirtualizingLayoutContext context)
        {
            base.InitializeForContextCore(context);
            _measuredWidths.Clear();
        }

        protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
        {
            base.UninitializeForContextCore(context);
            _measuredWidths.Clear();
        }

        protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
        {
            var itemCount = context.ItemCount;
            if (itemCount == 0)
            {
                return new Size(0, availableSize.Height);
            }

            var realizationRect = context.RealizationRect;
            var availableHeight = double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height;
            var itemExtent = EstimatedItemWidth + Spacing;

            // ビューポートに重なるアイテムの範囲を計算（前後1つ余裕を持たせる）
            var firstVisibleIndex = Math.Max(0, (int)(realizationRect.X / itemExtent) - 1);
            var lastVisibleIndex = Math.Min(itemCount - 1, (int)((realizationRect.X + realizationRect.Width) / itemExtent) + 1);

            // ビューポート内のアイテムのみを実体化して測定
            for (var i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                var element = context.GetOrCreateElementAt(i);
                element.Measure(new Size(double.PositiveInfinity, availableHeight));
                _measuredWidths[i] = element.DesiredSize.Width;
            }

            // 全体の幅を計算（推定値ベース）
            var totalWidth = itemCount * itemExtent - Spacing;

            return new Size(totalWidth, availableHeight);
        }

        protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
        {
            var itemCount = context.ItemCount;
            if (itemCount == 0)
            {
                return finalSize;
            }

            var realizationRect = context.RealizationRect;
            var itemExtent = EstimatedItemWidth + Spacing;
            var firstVisibleIndex = Math.Max(0, (int)(realizationRect.X / itemExtent) - 1);
            var lastVisibleIndex = Math.Min(itemCount - 1, (int)((realizationRect.X + realizationRect.Width) / itemExtent) + 1);

            for (var i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                var element = context.GetOrCreateElementAt(i);
                var x = i * itemExtent;
                var width = _measuredWidths.TryGetValue(i, out var w) ? w : EstimatedItemWidth;

                element.Arrange(new Rect(x, 0, width, finalSize.Height));
            }

            return finalSize;
        }
    }
}
