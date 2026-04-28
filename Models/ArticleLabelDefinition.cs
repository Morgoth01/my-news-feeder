namespace MyNewsFeeder.Models
{
    public class ArticleLabelDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#7C3AED";

        public ArticleLabelDefinition Clone()
        {
            return new ArticleLabelDefinition
            {
                Name = Name,
                ColorHex = ColorHex
            };
        }
    }
}