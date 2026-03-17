namespace SlackSitter.Models
{
    public sealed class MessageReactionItem
    {
        public string Name { get; }
        public int Count { get; }

        public MessageReactionItem(string name, int count)
        {
            Name = name;
            Count = count;
        }
    }
}
