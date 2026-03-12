using Microsoft.UI.Xaml.Data;
using System;

namespace SlackSitter.Converters
{
    public class TimestampToDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string timestamp && !string.IsNullOrEmpty(timestamp))
            {
                try
                {
                    // Unix timestamp形式 (1234567890.123456) を DateTime に変換
                    var parts = timestamp.Split('.');
                    if (parts.Length > 0 && long.TryParse(parts[0], out long seconds))
                    {
                        var dateTime = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                        return dateTime.ToString("yyyy/MM/dd HH:mm");
                    }
                }
                catch
                {
                    // エラーの場合は空文字列を返す
                }
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
