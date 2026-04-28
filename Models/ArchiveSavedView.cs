namespace MyNewsFeeder.Models
{
    public class ArchiveSavedView
    {
        public string Name { get; set; } = string.Empty;
        public ArchiveViewPreferences Preferences { get; set; } = new ArchiveViewPreferences();

        public ArchiveSavedView Clone()
        {
            return new ArchiveSavedView
            {
                Name = Name,
                Preferences = Preferences?.Clone() ?? new ArchiveViewPreferences()
            };
        }
    }
}