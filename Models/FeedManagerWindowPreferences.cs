namespace MyNewsFeeder.Models
{
    public class FeedManagerWindowPreferences
    {
        public string WindowState { get; set; } = "normal";
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        public FeedManagerWindowPreferences Clone()
        {
            return new FeedManagerWindowPreferences
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