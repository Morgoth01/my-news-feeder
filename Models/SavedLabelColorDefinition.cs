namespace MyNewsFeeder.Models
{
    public class SavedLabelColorDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#7C3AED";

        public SavedLabelColorDefinition Clone()
        {
            return new SavedLabelColorDefinition
            {
                Name = Name,
                ColorHex = ColorHex
            };
        }
    }
}