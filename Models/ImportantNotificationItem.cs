using System;

namespace MyNewsFeeder.Models
{
    public sealed class ImportantNotificationItem
    {
        public string FeedName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Link { get; init; } = string.Empty;
        public DateTime PublicationDate { get; init; }
        public DateTime ReceivedAt { get; init; }
    }
}