using System;
using System.ComponentModel;

namespace MyNewsFeeder.Models
{
    public class FeedItem : INotifyPropertyChanged
    {
        private bool _isRead;

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

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}