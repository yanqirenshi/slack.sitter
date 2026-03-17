using System.Collections.Generic;
using System.Linq;
using System;

namespace SlackSitter.Models
{
    public sealed class MessageImageItem
    {
        public IReadOnlyList<string> CandidateUrls { get; }

        public MessageImageItem(IEnumerable<string> candidateUrls)
        {
            CandidateUrls = candidateUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
