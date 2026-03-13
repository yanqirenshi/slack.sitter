using Microsoft.UI.Xaml.Data;
using System;
using System.Text.RegularExpressions;

namespace SlackSitter.Converters
{
    public class SlackMessageTextConverter : IValueConverter
    {
        private static readonly Regex SlackLinkWithLabelRegex = new Regex(@"<(?<url>https?://[^|>]+)\|(?<label>[^>]+)>", RegexOptions.Compiled);
        private static readonly Regex SlackLinkRegex = new Regex(@"<(?<url>https?://[^>]+)>", RegexOptions.Compiled);

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var converted = SlackLinkWithLabelRegex.Replace(text, match => match.Groups["label"].Value);
            converted = SlackLinkRegex.Replace(converted, match => match.Groups["url"].Value);

            return converted;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
