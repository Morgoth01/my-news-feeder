using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace MyNewsFeeder.ViewModels
{
    public class ArticleSectionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _name;
        private string _iconKind;
        private bool _isExpanded = true;
        private int _unreadCount;
        private bool _hideUnreadIndicators;
        private bool _opensInWindow;

        public ArticleSectionViewModel()
        {
            Items = new ObservableCollection<object>();
            Items.CollectionChanged += ItemsOnCollectionChanged;
        }

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

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public ObservableCollection<object> Items { get; }

        public int UnreadCount
        {
            get => _unreadCount;
            private set
            {
                if (_unreadCount != value)
                {
                    _unreadCount = value;
                    OnPropertyChanged(nameof(UnreadCount));
                    OnPropertyChanged(nameof(HasUnread));
                }
            }
        }

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

        public bool OpensInWindow
        {
            get => _opensInWindow;
            set
            {
                if (_opensInWindow != value)
                {
                    _opensInWindow = value;
                    OnPropertyChanged(nameof(OpensInWindow));
                }
            }
        }
        public string Category => Name;

        private void ItemsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    Unsubscribe(item);
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    Subscribe(item);
                }
            }

            UpdateUnreadCount();
        }

        private void Subscribe(object item)
        {
            if (item is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += ChildOnPropertyChanged;
            }
        }

        private void Unsubscribe(object item)
        {
            if (item is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged -= ChildOnPropertyChanged;
            }
        }

        private void ChildOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeedGroupViewModel.UnreadCount) ||
                e.PropertyName == nameof(CategoryGroupViewModel.UnreadCount))
            {
                UpdateUnreadCount();
            }
        }

        public void UpdateUnreadCount()
        {
            var fromFeeds = Items.OfType<FeedGroupViewModel>().Sum(f => f.UnreadCount);
            var fromCategories = Items.OfType<CategoryGroupViewModel>().Sum(c => c.UnreadCount);
            UnreadCount = fromFeeds + fromCategories;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}