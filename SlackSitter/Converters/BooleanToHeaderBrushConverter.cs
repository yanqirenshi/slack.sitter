using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace SlackSitter.Converters
{
    public class BooleanToHeaderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isMember)
            {
                if (isMember)
                {
                    // ユーザーが参加している場合は青（アクセントカラー）
                    return Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
                }
                else
                {
                    // ユーザーが参加していない場合はグレー
                    return new SolidColorBrush(Microsoft.UI.Colors.Gray);
                }
            }
            return Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
