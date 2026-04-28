using System;
using System.ComponentModel;
using System.Collections.Generic;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.ViewModels
{
    public sealed class ArchiveEntryViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly FeedItem _item;
        private bool _isSelected;

        public ArchiveEntryViewModel(FeedItem item, string category)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            Category = string.IsNullOrWhiteSpace(category) ? "Default" : category.Trim();
            _item.PropertyChanged += ItemOnPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FeedItem Item => _item;
        public string Category { get; }
        public string FeedName => _item.FeedName;
        public string Title => _item.Title;
        public string Description => _item.Description;
        public string Link => _item.Link;
        public DateTime PublicationDate => _item.PublicationDate;
        public DateTime? ArchivedAt => _item.ArchivedAt;
        public string ArchiveDayGroup => ArchivedAt?.ToString("yyyy-MM-dd  dddd, dd MMM yyyy") ?? "Unknown archive day";
        public string ArchiveMonthGroup => ArchivedAt?.ToString("yyyy-MM  MMMM yyyy") ?? "Unknown archive month";
        public bool IsRead => _item.IsRead;
        public bool IsUnread => _item.IsUnread;
        public IReadOnlyList<ArticleLabelDefinition> Labels => _item.Labels;
        public string LabelsText => _item.LabelsText;
        public string Note => _item.Note;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public void Dispose()
        {
            _item.PropertyChanged -= ItemOnPropertyChanged;
        }

        private void ItemOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FeedItem.Title):
                    OnPropertyChanged(nameof(Title));
                    break;
                case nameof(FeedItem.Description):
                    OnPropertyChanged(nameof(Description));
                    break;
                case nameof(FeedItem.Link):
                    OnPropertyChanged(nameof(Link));
                    break;
                case nameof(FeedItem.PublicationDate):
                    OnPropertyChanged(nameof(PublicationDate));
                    break;
                case nameof(FeedItem.ArchivedAt):
                    OnPropertyChanged(nameof(ArchivedAt));
                    OnPropertyChanged(nameof(ArchiveDayGroup));
                    OnPropertyChanged(nameof(ArchiveMonthGroup));
                    break;
                case nameof(FeedItem.IsRead):
                case nameof(FeedItem.IsUnread):
                    OnPropertyChanged(nameof(IsRead));
                    OnPropertyChanged(nameof(IsUnread));
                    break;
                case nameof(FeedItem.FeedName):
                    OnPropertyChanged(nameof(FeedName));
                    break;
                case nameof(FeedItem.Labels):
                case nameof(FeedItem.LabelsText):
                    OnPropertyChanged(nameof(Labels));
                    OnPropertyChanged(nameof(LabelsText));
                    break;
                case nameof(FeedItem.Note):
                    OnPropertyChanged(nameof(Note));
                    break;
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
