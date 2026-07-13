using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace MyNewsFeeder.ViewModels
{
    public class TerminalTheme
    {
        public string Name { get; set; }
        public Color Background { get; set; }
        public Color Foreground { get; set; }
        public Color Command { get; set; }
        public Color Error { get; set; }
        public Color Success { get; set; }
        public Color Warning { get; set; }
        public Color Dim { get; set; }
        public Color Header { get; set; }
        public Color Panel { get; set; }
        public Color Border { get; set; }
        public Color Selection { get; set; }
        public string FontFamily { get; set; } = "Consolas";

        public TerminalTheme(string name, 
                            Color background, Color foreground,
                            Color command, Color error, Color success, Color warning,
                            Color dim, Color header, Color panel, Color border, Color selection,
                            string fontFamily = "Consolas")
        {
            Name = name;
            Background = background;
            Foreground = foreground;
            Command = command;
            Error = error;
            Success = success;
            Warning = warning;
            Dim = dim;
            Header = header;
            Panel = panel;
            Border = border;
            Selection = selection;
            FontFamily = fontFamily;
        }

        // Standard-Themes
        public static readonly TerminalTheme Default = new TerminalTheme(
            "Default",
            Color.FromRgb(0x02, 0x06, 0x02),   // Hintergrund
            Color.FromRgb(0x5E, 0xE7, 0x5E),   // Text
            Color.FromRgb(0xFF, 0x55, 0xFF),   // Command (Pink)
            Color.FromRgb(0xFF, 0x55, 0x55),   // Error (Red)
            Color.FromRgb(0x55, 0xFF, 0x55),   // Success (Light Green)
            Color.FromRgb(0xFF, 0xFF, 0x55),   // Warning (Yellow)
            Color.FromRgb(0x2F, 0x8F, 0x2F),   // Dim (Dark Green)
            Color.FromRgb(0x55, 0xFF, 0xFF),   // Header (Cyan)
            Color.FromRgb(0x02, 0x09, 0x02),   // Panel
            Color.FromRgb(0x2A, 0xA5, 0x2A),   // Border
            Color.FromRgb(0x15, 0x48, 0x15)    // Selection
        );

        public static readonly TerminalTheme Matrix = new TerminalTheme(
            "Matrix",
            Color.FromRgb(0x00, 0x04, 0x00),   // Schwarzgrün
            Color.FromRgb(0x22, 0xE8, 0x55),   // Matrix Grün
            Color.FromRgb(0x66, 0xFF, 0x99),   // Command
            Color.FromRgb(0xFF, 0x00, 0x00),   // Rot
            Color.FromRgb(0x00, 0xFF, 0x66),   // Grün
            Color.FromRgb(0xFF, 0xFF, 0x00),   // Gelb
            Color.FromRgb(0x00, 0x7A, 0x22),   // Dunkelgrün
            Color.FromRgb(0x9A, 0xFF, 0xB5),   // Header
            Color.FromRgb(0x00, 0x08, 0x02),   // Panel
            Color.FromRgb(0x00, 0x8A, 0x24),   // Rand
            Color.FromRgb(0x04, 0x3A, 0x13)    // Auswahl
        );

        public static readonly TerminalTheme Crt = new TerminalTheme(
            "CRT",
            Color.FromRgb(0x00, 0x08, 0x02),
            Color.FromRgb(0x6B, 0xFF, 0x6B),
            Color.FromRgb(0xC7, 0xFF, 0x7A),
            Color.FromRgb(0xFF, 0x5F, 0x5F),
            Color.FromRgb(0x7C, 0xFF, 0x7C),
            Color.FromRgb(0xF0, 0xE6, 0x72),
            Color.FromRgb(0x2B, 0x8A, 0x36),
            Color.FromRgb(0xB7, 0xFF, 0xB7),
            Color.FromRgb(0x00, 0x10, 0x04),
            Color.FromRgb(0x32, 0xC8, 0x46),
            Color.FromRgb(0x18, 0x3E, 0x20)
        );

        public static readonly TerminalTheme Amber = new TerminalTheme(
            "Amber",
            Color.FromRgb(0x12, 0x08, 0x00),
            Color.FromRgb(0xFF, 0xB0, 0x32),
            Color.FromRgb(0xFF, 0xD3, 0x78),
            Color.FromRgb(0xFF, 0x66, 0x48),
            Color.FromRgb(0xFF, 0xC4, 0x4D),
            Color.FromRgb(0xFF, 0xE0, 0x7A),
            Color.FromRgb(0xA0, 0x68, 0x1C),
            Color.FromRgb(0xFF, 0xE2, 0xA0),
            Color.FromRgb(0x1C, 0x0D, 0x00),
            Color.FromRgb(0xC4, 0x78, 0x1A),
            Color.FromRgb(0x4D, 0x2A, 0x06)
        );

        public static readonly TerminalTheme Dos = new TerminalTheme(
            "DOS",
            Color.FromRgb(0x00, 0x00, 0x80),
            Color.FromRgb(0xC0, 0xC0, 0xC0),
            Color.FromRgb(0xFF, 0xFF, 0x00),
            Color.FromRgb(0xFF, 0x55, 0x55),
            Color.FromRgb(0x55, 0xFF, 0x55),
            Color.FromRgb(0xFF, 0xFF, 0x55),
            Color.FromRgb(0x80, 0x80, 0x80),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x00, 0x00, 0xAA),
            Color.FromRgb(0x80, 0x80, 0x80),
            Color.FromRgb(0x00, 0x00, 0x55)
        );

        public static readonly TerminalTheme SolarizedDark = new TerminalTheme(
            "SolarizedDark",
            Color.FromRgb(0x00, 0x2B, 0x36),   // Base03
            Color.FromRgb(0x93, 0xA1, 0xA1),   // Base1
            Color.FromRgb(0xDC, 0x32, 0x2F),   // Red
            Color.FromRgb(0xDC, 0x32, 0x2F),   // Red
            Color.FromRgb(0x85, 0x99, 0x00),   // Green
            Color.FromRgb(0xB5, 0x89, 0x00),   // Yellow
            Color.FromRgb(0x65, 0x7B, 0x83),   // Base01
            Color.FromRgb(0x26, 0x8B, 0xD2),   // Blue
            Color.FromRgb(0x07, 0x36, 0x42),   // Base02
            Color.FromRgb(0x00, 0x2B, 0x36),   // Base03
            Color.FromRgb(0x26, 0x8B, 0xD2)    // Blue
        );

        public static readonly TerminalTheme Dracula = new TerminalTheme(
            "Dracula",
            Color.FromRgb(0x28, 0x2A, 0x36),   // Hintergrund
            Color.FromRgb(0xF8, 0xF8, 0xF2),   // Text
            Color.FromRgb(0xFF, 0x79, 0xC6),   // Pink
            Color.FromRgb(0xFF, 0x55, 0x55),   // Red
            Color.FromRgb(0x50, 0xFA, 0x7B),   // Green
            Color.FromRgb(0xFF, 0xB8, 0x6C),   // Yellow
            Color.FromRgb(0x62, 0x72, 0xA4),   // Dim
            Color.FromRgb(0xBD, 0x93, 0xF9),   // Purple
            Color.FromRgb(0x44, 0x47, 0x5A),   // Panel
            Color.FromRgb(0xFF, 0x79, 0xC6),   // Border (Pink)
            Color.FromRgb(0x44, 0x47, 0x5A)    // Selection
        );

        public static readonly TerminalTheme Paper = new TerminalTheme(
            "Paper",
            Color.FromRgb(0xF4, 0xF1, 0xE8),
            Color.FromRgb(0x1F, 0x29, 0x33),
            Color.FromRgb(0x5B, 0x2C, 0x83),
            Color.FromRgb(0xB0, 0x00, 0x20),
            Color.FromRgb(0x1F, 0x7A, 0x3A),
            Color.FromRgb(0xA1, 0x66, 0x00),
            Color.FromRgb(0x6B, 0x72, 0x80),
            Color.FromRgb(0x0B, 0x5F, 0x6A),
            Color.FromRgb(0xFF, 0xFC, 0xF4),
            Color.FromRgb(0xB8, 0xB2, 0xA6),
            Color.FromRgb(0xD9, 0xE8, 0xEA)
        );

        public static readonly IReadOnlyList<TerminalTheme> AllThemes = new List<TerminalTheme>
        {
            Default,
            Crt,
            Amber,
            Dos,
            Matrix,
            SolarizedDark,
            Dracula,
            Paper
        };

        public static TerminalTheme GetByName(string name)
        {
            foreach (var theme in AllThemes)
            {
                if (string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return theme;
                }
            }
            return Default;
        }

        public SolidColorBrush GetBrush(TerminalLineType type)
        {
            return type switch
            {
                TerminalLineType.Command => new SolidColorBrush(Command),
                TerminalLineType.Error => new SolidColorBrush(Error),
                TerminalLineType.Success => new SolidColorBrush(Success),
                TerminalLineType.Warning => new SolidColorBrush(Warning),
                TerminalLineType.Dim => new SolidColorBrush(Dim),
                TerminalLineType.Header => new SolidColorBrush(Header),
                _ => new SolidColorBrush(Foreground)
            };
        }
    }
}