using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class LabelManagerWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private sealed class LabelColorOption
        {
            public string Name { get; init; } = string.Empty;
            public string ColorHex { get; init; } = "#7C3AED";
            public bool IsBuiltIn { get; init; }
        }

        private static readonly IReadOnlyList<LabelColorOption> DefaultColors = new[]
        {
            new LabelColorOption { Name = "Purple", ColorHex = "#7C3AED", IsBuiltIn = true },
            new LabelColorOption { Name = "Blue", ColorHex = "#2563EB", IsBuiltIn = true },
            new LabelColorOption { Name = "Teal", ColorHex = "#0F766E", IsBuiltIn = true },
            new LabelColorOption { Name = "Green", ColorHex = "#15803D", IsBuiltIn = true },
            new LabelColorOption { Name = "Amber", ColorHex = "#D97706", IsBuiltIn = true },
            new LabelColorOption { Name = "Orange", ColorHex = "#EA580C", IsBuiltIn = true },
            new LabelColorOption { Name = "Rose", ColorHex = "#E11D48", IsBuiltIn = true },
            new LabelColorOption { Name = "Slate", ColorHex = "#475569", IsBuiltIn = true }
        };

        private readonly MainViewModel _viewModel;
        private readonly ObservableCollection<LabelColorOption> _colorOptions = new ObservableCollection<LabelColorOption>();
        private string _selectedOriginalName;

        public LabelManagerWindow(MainViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            SourceInitialized += (_, __) => EnableDarkTitleBar();
            DataContext = _viewModel;
            ColorComboBox.ItemsSource = _colorOptions;
            RefreshColorOptions();
            RefreshLabels();
            UpdatePreview();
        }

        private void EnableDarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf<int>()) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, Marshal.SizeOf<int>());
            }
        }

        private void RefreshLabels()
        {
            LabelsListView.ItemsSource = _viewModel.GetArticleLabels();
        }

        private void LabelsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LabelsListView.SelectedItem is not ArticleLabelDefinition label)
            {
                return;
            }

            _selectedOriginalName = label.Name;
            LabelNameTextBox.Text = label.Name;
            RefreshColorOptions(label.ColorHex, null);
            CustomColorTextBox.Text = label.ColorHex;
            CustomColorNameTextBox.Text = string.Empty;
            UpdatePreview();
        }

        private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorComboBox.SelectedItem is LabelColorOption option)
            {
                CustomColorTextBox.Text = option.ColorHex;
                CustomColorNameTextBox.Text = option.IsBuiltIn ? string.Empty : option.Name;
            }

            UpdatePreview();
        }

        private void LabelNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void CustomColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void RefreshColorOptions(string preferredColorHex = null, string preferredName = null)
        {
            _colorOptions.Clear();

            foreach (var option in DefaultColors)
            {
                _colorOptions.Add(option);
            }

            foreach (var color in _viewModel.GetSavedLabelColors())
            {
                if (color == null ||
                    string.IsNullOrWhiteSpace(color.Name) ||
                    string.IsNullOrWhiteSpace(color.ColorHex) ||
                    _colorOptions.Any(option => string.Equals(option.Name, color.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _colorOptions.Add(new LabelColorOption
                {
                    Name = color.Name,
                    ColorHex = color.ColorHex
                });
            }

            var normalizedPreferred = NormalizeColorHex(preferredColorHex);
            var normalizedPreferredName = preferredName?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedPreferred) &&
                !string.IsNullOrWhiteSpace(normalizedPreferredName) &&
                !_colorOptions.Any(option => string.Equals(option.Name, normalizedPreferredName, StringComparison.OrdinalIgnoreCase)))
            {
                _colorOptions.Add(new LabelColorOption
                {
                    Name = normalizedPreferredName,
                    ColorHex = normalizedPreferred
                });
            }

            var selected = _colorOptions.FirstOrDefault(option =>
                (!string.IsNullOrWhiteSpace(normalizedPreferredName) && string.Equals(option.Name, normalizedPreferredName, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(option.ColorHex, normalizedPreferred, StringComparison.OrdinalIgnoreCase));
            ColorComboBox.SelectedItem = selected ?? _colorOptions.FirstOrDefault();
        }

        private static string NormalizeColorHex(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(input.Trim());
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch
            {
                return null;
            }
        }

        private string GetCurrentColorHex(bool requireValidCustomInput)
        {
            var customColor = NormalizeColorHex(CustomColorTextBox.Text);
            if (!string.IsNullOrWhiteSpace(CustomColorTextBox.Text))
            {
                if (!string.IsNullOrWhiteSpace(customColor))
                {
                    return customColor;
                }

                if (requireValidCustomInput)
                {
                    MessageBox.Show(this,
                        "Enter a valid custom color in the format #RRGGBB.",
                        "Custom Color",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    CustomColorTextBox.Focus();
                    CustomColorTextBox.SelectAll();
                    return null;
                }
            }

            return (ColorComboBox.SelectedItem as LabelColorOption)?.ColorHex ?? "#7C3AED";
        }

        private string GetCurrentCustomColorName(bool requireName)
        {
            var name = CustomColorNameTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (ColorComboBox.SelectedItem is LabelColorOption selectedOption && !selectedOption.IsBuiltIn)
            {
                return selectedOption.Name;
            }

            if (requireName)
            {
                MessageBox.Show(this,
                    "Enter a name for the custom color first.",
                    "Custom Color",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                CustomColorNameTextBox.Focus();
                return null;
            }

            return string.Empty;
        }

        private void UpdatePreview()
        {
            var text = string.IsNullOrWhiteSpace(LabelNameTextBox.Text) ? "Label" : LabelNameTextBox.Text.Trim();
            var colorHex = GetCurrentColorHex(requireValidCustomInput: false) ?? "#7C3AED";
            PreviewChipText.Text = text;
            PreviewChip.Background = (Brush)new BrushConverter().ConvertFromString(colorHex);
        }

        private void SaveLabelButton_Click(object sender, RoutedEventArgs e)
        {
            var name = LabelNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this,
                    "Enter a label name first.",
                    "Save Label",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LabelNameTextBox.Focus();
                return;
            }

            var colorHex = GetCurrentColorHex(requireValidCustomInput: true);
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return;
            }

            var customColorName = CustomColorNameTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(customColorName))
            {
                _viewModel.SaveCustomLabelColor(customColorName, colorHex);
            }

            if (!string.IsNullOrWhiteSpace(_selectedOriginalName) &&
                !string.Equals(_selectedOriginalName, name, StringComparison.OrdinalIgnoreCase))
            {
                _viewModel.RenameArticleLabel(_selectedOriginalName, name, colorHex);
            }
            else
            {
                _viewModel.SaveArticleLabel(name, colorHex);
            }

            RefreshLabels();
            RefreshColorOptions(colorHex, customColorName);
            LabelsListView.SelectedItem = _viewModel.GetArticleLabels()
                .FirstOrDefault(label => string.Equals(label.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveCustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            var colorName = GetCurrentCustomColorName(requireName: true);
            if (string.IsNullOrWhiteSpace(colorName))
            {
                return;
            }

            var colorHex = GetCurrentColorHex(requireValidCustomInput: true);
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return;
            }

            _viewModel.SaveCustomLabelColor(colorName, colorHex);
            RefreshColorOptions(colorHex, colorName);
            CustomColorNameTextBox.Text = colorName;
            CustomColorTextBox.Text = colorHex;
            UpdatePreview();
        }

        private void PickCustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            var initialHex = GetCurrentColorHex(requireValidCustomInput: false) ?? "#7C3AED";
            var initialColor = System.Drawing.ColorTranslator.FromHtml(initialHex);

            using var dialog = new Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = false,
                Color = initialColor
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                return;
            }

            var selectedHex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            CustomColorTextBox.Text = selectedHex;
            UpdatePreview();
        }

        private void DeleteCustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            var colorName = GetCurrentCustomColorName(requireName: true);
            if (string.IsNullOrWhiteSpace(colorName))
            {
                return;
            }

            var selectedOption = ColorComboBox.SelectedItem as LabelColorOption;
            if (selectedOption?.IsBuiltIn == true && string.Equals(selectedOption.Name, colorName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    "Built-in colors cannot be deleted.",
                    "Delete Color",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Delete the custom color \"{colorName}\"?{Environment.NewLine}{Environment.NewLine}Labels that already use this color keep it, but the preset will be removed from the picker.",
                "Delete Color",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            if (_viewModel.DeleteCustomLabelColor(colorName))
            {
                CustomColorNameTextBox.Text = string.Empty;
                CustomColorTextBox.Text = string.Empty;
                RefreshColorOptions();
                UpdatePreview();
            }
        }

        private void NewLabelButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedOriginalName = null;
            LabelsListView.SelectedItem = null;
            LabelNameTextBox.Text = string.Empty;
            CustomColorNameTextBox.Text = string.Empty;
            CustomColorTextBox.Text = string.Empty;
            RefreshColorOptions();
            UpdatePreview();
            LabelNameTextBox.Focus();
        }

        private void DeleteLabelButton_Click(object sender, RoutedEventArgs e)
        {
            if (LabelsListView.SelectedItem is not ArticleLabelDefinition label)
            {
                MessageBox.Show(this,
                    "Select a label to delete.",
                    "Delete Label",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Delete the label \"{label.Name}\"?{Environment.NewLine}{Environment.NewLine}It will be removed from all assigned articles.",
                "Delete Label",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            if (_viewModel.DeleteArticleLabel(label.Name))
            {
                _selectedOriginalName = null;
                NewLabelButton_Click(sender, e);
                RefreshLabels();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}