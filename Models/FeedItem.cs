using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MyNewsFeeder.Models
{
    public class FeedItem : INotifyPropertyChanged
    {
        private bool _isRead;
        private bool _isPinned;
        private bool _isReadLater;
        private bool _isArchived;
        private DateTime? _archivedAt;
        private bool _isSelected;
        private List<ArticleLabelDefinition> _labels = new List<ArticleLabelDefinition>();
        private string _note = string.Empty;
        private string _title = string.Empty;
        private string _description = string.Empty;
        private string _categoryName = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FeedName { get; set; }
        public string CategoryName
        {
            get => _categoryName;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_categoryName, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _categoryName = normalized;
                OnPropertyChanged(nameof(CategoryName));
            }
        }
        public string FeedUrl { get; set; }
        public string Title
        {
            get => _title;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_title, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _title = normalized;
                InvalidateArticlePreviewCache();
                OnPropertyChanged(nameof(Title));
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_description, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _description = normalized;
                InvalidateArticlePreviewCache();
                OnPropertyChanged(nameof(Description));
            }
        }

        public string Link { get; set; }
        public DateTime PublicationDate { get; set; }
        public bool IsAdvertisement { get; set; }

        [JsonIgnore]
        public string CachedArticleSummaryHtml { get; set; } = string.Empty;

        [JsonIgnore]
        public string CachedArticlePlainText { get; set; } = string.Empty;

        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (_isRead == value) return;
                _isRead = value;
                OnPropertyChanged(nameof(IsRead));
                OnPropertyChanged(nameof(IsUnread));
            }
        }

        public bool IsUnread => !IsRead;

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                OnPropertyChanged(nameof(IsPinned));
            }
        }

        public bool IsReadLater
        {
            get => _isReadLater;
            set
            {
                if (_isReadLater == value) return;
                _isReadLater = value;
                OnPropertyChanged(nameof(IsReadLater));
            }
        }

        public bool IsArchived
        {
            get => _isArchived;
            set
            {
                if (_isArchived == value) return;
                _isArchived = value;
                OnPropertyChanged(nameof(IsArchived));
            }
        }

        public DateTime? ArchivedAt
        {
            get => _archivedAt;
            set
            {
                if (_archivedAt == value) return;
                _archivedAt = value;
                OnPropertyChanged(nameof(ArchivedAt));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public string Note
        {
            get => _note;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_note, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _note = normalized;
                OnPropertyChanged(nameof(Note));
            }
        }

        [JsonIgnore]
        public IReadOnlyList<ArticleLabelDefinition> Labels => _labels;

        [JsonIgnore]
        public string LabelsText => _labels.Count == 0
            ? string.Empty
            : string.Join(", ", _labels.Select(label => label.Name));

        public void SetLabels(IEnumerable<ArticleLabelDefinition> labels)
        {
            _labels = labels?
                .Where(label => label != null && !string.IsNullOrWhiteSpace(label.Name))
                .GroupBy(label => label.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First().Clone())
                .OrderBy(label => label.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                ?? new List<ArticleLabelDefinition>();
            OnPropertyChanged(nameof(Labels));
            OnPropertyChanged(nameof(LabelsText));
        }

        public void InvalidateArticlePreviewCache()
        {
            CachedArticleSummaryHtml = string.Empty;
            CachedArticlePlainText = string.Empty;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}