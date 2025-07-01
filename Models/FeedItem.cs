using System;
using System.Collections.Generic;

namespace MyNewsFeeder.Models
{
    public class FeedItem
    {
        public string FeedName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Link { get; set; }
        public DateTime PublicationDate { get; set; }
    }
}
