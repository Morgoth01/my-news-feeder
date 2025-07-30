using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MyNewsFeeder.ViewModels
{
    public class CategoryGroupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private bool _isExpanded = true;
        private string _name;
        
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public ObservableCollection<FeedGroupViewModel> Feeds { get; set; }
        
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
        
        public CategoryGroupViewModel()
        {
            Feeds = new ObservableCollection<FeedGroupViewModel>();
        }
        
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}