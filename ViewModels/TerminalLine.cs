using System;

namespace MyNewsFeeder.ViewModels
{
    public enum TerminalLineType
    {
        Normal,
        Command,
        Error,
        Success,
        Warning,
        Dim,
        Header
    }

    public class TerminalLine
    {
        public string Text { get; set; }
        public TerminalLineType Type { get; set; } = TerminalLineType.Normal;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public TerminalLine(string text, TerminalLineType type = TerminalLineType.Normal)
        {
            Text = text ?? string.Empty;
            Type = type;
        }

        public static TerminalLine Normal(string text) => new TerminalLine(text, TerminalLineType.Normal);
        public static TerminalLine Command(string text) => new TerminalLine(text, TerminalLineType.Command);
        public static TerminalLine Error(string text) => new TerminalLine(text, TerminalLineType.Error);
        public static TerminalLine Success(string text) => new TerminalLine(text, TerminalLineType.Success);
        public static TerminalLine Warning(string text) => new TerminalLine(text, TerminalLineType.Warning);
        public static TerminalLine Dim(string text) => new TerminalLine(text, TerminalLineType.Dim);
        public static TerminalLine Header(string text) => new TerminalLine(text, TerminalLineType.Header);
    }
}