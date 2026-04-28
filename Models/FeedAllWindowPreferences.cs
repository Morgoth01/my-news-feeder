namespace MyNewsFeeder.Models
{
    public class FeedAllWindowPreferences
    {
        public string WindowState { get; set; } = "normal";
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        public FeedAllWindowPreferences Clone()
        {
            return new FeedAllWindowPreferences
            {
                WindowState = WindowState,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop
            };
        }
    }
}