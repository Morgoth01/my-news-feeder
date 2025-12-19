using MyNewsFeeder.Models;

namespace MyNewsFeeder.ViewModels
{
    public enum FeedMoveDirection
    {
        Up,
        Down
    }

    public class FeedMoveRequest
    {
        public Feed Feed { get; set; }
        public FeedMoveDirection Direction { get; set; }
    }
}