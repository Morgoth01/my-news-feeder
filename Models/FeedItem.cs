using System;
using System.ComponentModel;

namespace MyNewsFeeder.Models
{
    public class FeedItem : INotifyPropertyChanged
    {
        private bool _isRead;
        private bool _isPinned;
        private bool _isReadLater;
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FeedName { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Link { get; set; }
        public DateTime PublicationDate { get; set; }
        public bool IsAdvertisement { get; set; }

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

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}