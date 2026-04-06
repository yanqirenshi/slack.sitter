namespace SlackSitter.Models
{
    public sealed class SlackUserProfileInfo
    {
        public string UserId { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string RealName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public string ImageUrl { get; init; } = string.Empty;
    }
}
