using System;

namespace SlackSitter.Models
{
    public sealed class MessageInlineSegment
    {
        public MessageInlineSegmentType Type { get; }
        public string Text { get; }
        public Uri? Uri { get; }
        public bool IsBold { get; }
        public bool IsItalic { get; }
        public bool IsStrikethrough { get; }
        public bool IsCode { get; }

        public MessageInlineSegment(
            MessageInlineSegmentType type,
            string text,
            Uri? uri = null,
            bool isBold = false,
            bool isItalic = false,
            bool isStrikethrough = false,
            bool isCode = false)
        {
            Type = type;
            Text = text;
            Uri = uri;
            IsBold = isBold;
            IsItalic = isItalic;
            IsStrikethrough = isStrikethrough;
            IsCode = isCode;
        }
    }
}
