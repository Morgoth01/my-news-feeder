using System.Collections.ObjectModel;
using System.ComponentModel;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.ViewModels
{
    public class FeedGroupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isExpanded = true;
        private string _name;
        private string _category = "Default";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Category
        {
            get => _category;
            set
            {
                _category = value;
                OnPropertyChanged(nameof(Category));
            }
        }

        public ObservableCollection<FeedItem> Items { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public FeedGroupViewModel()
        {
            Items = new ObservableCollection<FeedItem>();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}