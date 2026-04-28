using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;

namespace MyNewsFeeder.ViewModels
{
    public class CategoryGroupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private bool _isExpanded = true;
        private string _name;
        private string _iconKind = "FolderMultipleOutline";
        private ObservableCollection<FeedGroupViewModel> _feeds;
        private int _unreadCount;
        private bool _hideUnreadIndicators;
        private bool _isNavigationSelected;
        
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string IconKind
        {
            get => _iconKind;
            set
            {
                _iconKind = value;
                OnPropertyChanged(nameof(IconKind));
            }
        }

        public ObservableCollection<FeedGroupViewModel> Feeds
        {
            get => _feeds;
            set
            {
                if (_feeds == value) return;

                if (_feeds != null)
                {
                    _feeds.CollectionChanged -= FeedsOnCollectionChanged;
                    foreach (var feed in _feeds)
                    {
                        UnsubscribeFromFeed(feed);
                    }
                }

                _feeds = value;

                if (_feeds != null)
                {
                    _feeds.CollectionChanged += FeedsOnCollectionChanged;
                    foreach (var feed in _feeds)
                    {
                        SubscribeToFeed(feed);
                    }
                }

                OnPropertyChanged(nameof(Feeds));
                OnPropertyChanged(nameof(ArticleCount));
                UpdateUnreadCount();
            }
        }
        
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public int ArticleCount => Feeds?.Sum(feed => feed.Items?.Count ?? 0) ?? 0;
        public int UnreadCount => _unreadCount;
        public bool HasUnread => UnreadCount > 0;
        public bool HideUnreadIndicators
        {
            get => _hideUnreadIndicators;
            set
            {
                if (_hideUnreadIndicators != value)
                {
                    _hideUnreadIndicators = value;
                    OnPropertyChanged(nameof(HideUnreadIndicators));
                }
            }
        }

        public bool IsNavigationSelected
        {
            get => _isNavigationSelected;
            set
            {
                if (_isNavigationSelected != value)
                {
                    _isNavigationSelected = value;
                    OnPropertyChanged(nameof(IsNavigationSelected));
                }
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

        private void FeedsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FeedGroupViewModel feed in e.OldItems)
                {
                    UnsubscribeFromFeed(feed);
                }
            }

            if (e.NewItems != null)
            {
                foreach (FeedGroupViewModel feed in e.NewItems)
                {
                    SubscribeToFeed(feed);
                }
            }

            OnPropertyChanged(nameof(ArticleCount));
            UpdateUnreadCount();
        }

        private void SubscribeToFeed(FeedGroupViewModel feed)
        {
            if (feed?.Items != null)
            {
                feed.Items.CollectionChanged += FeedItemsOnCollectionChanged;
            }
            feed.PropertyChanged += FeedOnPropertyChanged;
            UpdateUnreadCount();
        }

        private void UnsubscribeFromFeed(FeedGroupViewModel feed)
        {
            if (feed?.Items != null)
            {
                feed.Items.CollectionChanged -= FeedItemsOnCollectionChanged;
            }
            feed.PropertyChanged -= FeedOnPropertyChanged;
            UpdateUnreadCount();
        }

        private void FeedItemsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ArticleCount));
            UpdateUnreadCount();
        }

        private void FeedOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeedGroupViewModel.UnreadCount) ||
                e.PropertyName == nameof(FeedGroupViewModel.HasUnread) ||
                e.PropertyName == nameof(FeedGroupViewModel.ItemCount))
            {
                UpdateUnreadCount();
                OnPropertyChanged(nameof(ArticleCount));
            }
        }

        private void UpdateUnreadCount()
        {
            var newUnread = Feeds?.Sum(feed => feed.UnreadCount) ?? 0;
            if (newUnread != _unreadCount)
            {
                _unreadCount = newUnread;
                OnPropertyChanged(nameof(UnreadCount));
                OnPropertyChanged(nameof(HasUnread));
            }
        }
    }
}