namespace MyNewsFeeder.Services
{
    internal static class AppThemeColors
    {
        public const string DarkBackground = "#17181c";
        public const string DarkSurface = "#23262c";
        public const string DarkText = "#d9dde3";
        public const string DarkSecondaryText = "#aeb5c0";
        public const string DarkAccent = "#a78bfa";
        public const string DarkAccentHover = "#b9a3ff";
        public const string DarkSuccess = "#6ee7a8";

        private static readonly ArticlePalette DarkArticlePalette = new(
            DarkBackground,
            DarkText,
            DarkAccentHover,
            DarkSecondaryText,
            DarkSurface,
            DarkAccent,
            DarkSuccess);

        private static readonly ArticlePalette LightArticlePalette = new(
            "#ffffff",
            "#333333",
            "#673ab7",
            "#666666",
            "#f8f8f8",
            "#673ab7",
            "#673ab7");

        private static readonly BrowserChromePalette DarkBrowserChromePalette = new(
            DarkBackground,
            DarkText,
            DarkAccent);

        private static readonly BrowserChromePalette LightBrowserChromePalette = new(
            "#f5f5f5",
            "#666666",
            "#7b5bd6");

        public static ArticlePalette Article(bool useDarkMode) =>
            useDarkMode ? DarkArticlePalette : LightArticlePalette;

        public static BrowserChromePalette BrowserChrome(bool useDarkMode) =>
            useDarkMode ? DarkBrowserChromePalette : LightBrowserChromePalette;

        internal readonly record struct ArticlePalette(
            string Background,
            string Text,
            string Heading,
            string SecondaryText,
            string Surface,
            string Border,
            string Link);

        internal readonly record struct BrowserChromePalette(
            string Background,
            string Text,
            string Accent);
    }
}