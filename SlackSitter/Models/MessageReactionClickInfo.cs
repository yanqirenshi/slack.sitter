namespace SlackSitter.Models
{
    public sealed class MessageReactionClickInfo
    {
        public MessageDisplayItem Message { get; }
        public MessageReactionItem Reaction { get; }

        public MessageReactionClickInfo(MessageDisplayItem message, MessageReactionItem reaction)
        {
            Message = message;
            Reaction = reaction;
        }
    }
}
