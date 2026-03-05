using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Windows.Input;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.ViewModels
{
    public class FeedGroupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isExpanded = true;
        private string _name;
        private string _category = "Default";
        private string _iconKind = "Rss";
        private ObservableCollection<FeedItem> _items;
        private readonly ObservableCollection<object> _pagedItems;
        private int _currentPageSize = 20;
        private int _loadedItemsCount;
        private readonly LoadMoreItem _loadMoreMarker = new LoadMoreItem();
        private bool _hideUnreadIndicators;
        private int _batchUpdateDepth;
        private bool _pendingRefreshPagedItems;
        private bool _pendingResetLoadedCount;
        private bool _pendingCountNotification;

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

        public string IconKind
        {
            get => _iconKind;
            set
            {
                _iconKind = value;
                OnPropertyChanged(nameof(IconKind));
            }
        }

        public ObservableCollection<FeedItem> Items
        {
            get => _items;
            set
            {
                if (_items == value) return;

                if (_items != null)
                {
                    _items.CollectionChanged -= ItemsOnCollectionChanged;
                    foreach (var item in _items)
                    {
                        DetachItem(item);
                    }
                }

                _items = value;

                if (_items != null)
                {
                    _items.CollectionChanged += ItemsOnCollectionChanged;
                    foreach (var item in _items)
                    {
                        AttachItem(item);
                    }
                }

                OnPropertyChanged(nameof(Items));
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(UnreadCount));
                OnPropertyChanged(nameof(HasUnread));
                RefreshPagedItems(resetLoadedCount: true);
            }
        }

        public int ItemCount => Items?.Count ?? 0;
        public int UnreadCount => Items?.Count(item => !item.IsRead) ?? 0;
        public bool HasUnread => UnreadCount > 0;
        public bool HasMoreItems => Items != null && _loadedItemsCount < (Items?.Count ?? 0);
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

        public ObservableCollection<object> PagedItems => _pagedItems;

        public ICommand LoadMoreCommand { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                if (_isExpanded)
                {
                    // Collapse -> expand should start from the paged view again.
                    _loadedItemsCount = 0;
                    RefreshPagedItems(resetLoadedCount: true);
                }
            }
        }

        public FeedGroupViewModel()
        {
            _pagedItems = new ObservableCollection<object>();
            Items = new ObservableCollection<FeedItem>();
            LoadMoreCommand = new RelayCommand(_ => LoadMore(), _ => HasMoreItems);
            RefreshPagedItems(resetLoadedCount: true);
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ItemsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FeedItem item in e.OldItems)
                {
                    DetachItem(item);
                }
            }

            if (e.NewItems != null)
            {
                foreach (FeedItem item in e.NewItems)
                {
                    AttachItem(item);
                }
            }

            var shouldResetPaging = _loadedItemsCount == 0 || (e.Action == NotifyCollectionChangedAction.Reset);
            if (_batchUpdateDepth > 0)
            {
                _pendingCountNotification = true;
                _pendingRefreshPagedItems = true;
                _pendingResetLoadedCount |= shouldResetPaging;
                return;
            }

            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(HasUnread));
            RefreshPagedItems(resetLoadedCount: shouldResetPaging);
        }

        private void RefreshPagedItems(bool resetLoadedCount)
        {
            if (Items == null || Items.Count == 0)
            {
                SyncPagedItems(Array.Empty<object>());
                _loadedItemsCount = 0;
                OnPropertyChanged(nameof(HasMoreItems));
                (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
                return;
            }

            var totalCount = Items.Count;
            // page size is the larger of 30% or a floor of 20 items
            var calculated = (int)System.Math.Ceiling(totalCount * 0.3);
            _currentPageSize = System.Math.Max(20, calculated);

            if (resetLoadedCount || _loadedItemsCount == 0)
            {
                _loadedItemsCount = System.Math.Min(_currentPageSize, totalCount);
            }
            else
            {
                _loadedItemsCount = System.Math.Min(System.Math.Max(_loadedItemsCount, _currentPageSize), totalCount);
            }

            var desiredItems = new List<object>(_loadedItemsCount + 1);
            for (int i = 0; i < _loadedItemsCount; i++)
            {
                desiredItems.Add(Items[i]);
            }

            // append load-more marker if needed
            if (HasMoreItems)
            {
                _loadMoreMarker.LoadMoreCommand = LoadMoreCommand;
                desiredItems.Add(_loadMoreMarker);
            }

            SyncPagedItems(desiredItems);

            OnPropertyChanged(nameof(HasMoreItems));
            (LoadMoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void LoadMore()
        {
            if (!HasMoreItems)
            {
                return;
            }

            _loadedItemsCount = System.Math.Min(Items.Count, _loadedItemsCount + _currentPageSize);
            RefreshPagedItems(resetLoadedCount: false);
        }

        private void AttachItem(FeedItem item)
        {
            if (item != null)
            {
                item.PropertyChanged += ItemOnPropertyChanged;
            }
        }

        private void DetachItem(FeedItem item)
        {
            if (item != null)
            {
                item.PropertyChanged -= ItemOnPropertyChanged;
            }
        }

        private void ItemOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeedItem.IsRead) || e.PropertyName == nameof(FeedItem.IsUnread))
            {
                OnPropertyChanged(nameof(UnreadCount));
                OnPropertyChanged(nameof(HasUnread));
            }
        }

        private void SyncPagedItems(IList<object> desiredItems)
        {
            if (desiredItems == null)
            {
                return;
            }

            var desiredSet = new HashSet<object>(desiredItems);
            for (var index = _pagedItems.Count - 1; index >= 0; index--)
            {
                if (!desiredSet.Contains(_pagedItems[index]))
                {
                    _pagedItems.RemoveAt(index);
                }
            }

            for (var desiredIndex = 0; desiredIndex < desiredItems.Count; desiredIndex++)
            {
                var item = desiredItems[desiredIndex];
                if (desiredIndex < _pagedItems.Count && ReferenceEquals(_pagedItems[desiredIndex], item))
                {
                    continue;
                }

                var currentIndex = _pagedItems.IndexOf(item);
                if (currentIndex < 0)
                {
                    _pagedItems.Insert(Math.Min(desiredIndex, _pagedItems.Count), item);
                    continue;
                }

                if (currentIndex > desiredIndex)
                {
                    _pagedItems.Move(currentIndex, desiredIndex);
                }
            }
        }

        public IDisposable BeginItemsBatchUpdate()
        {
            _batchUpdateDepth++;
            return new ItemsBatchScope(this);
        }

        private void EndItemsBatchUpdate()
        {
            if (_batchUpdateDepth == 0)
            {
                return;
            }

            _batchUpdateDepth--;
            if (_batchUpdateDepth > 0)
            {
                return;
            }

            if (_pendingCountNotification)
            {
                _pendingCountNotification = false;
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(UnreadCount));
                OnPropertyChanged(nameof(HasUnread));
            }

            if (_pendingRefreshPagedItems)
            {
                var resetLoadedCount = _pendingResetLoadedCount;
                _pendingRefreshPagedItems = false;
                _pendingResetLoadedCount = false;
                RefreshPagedItems(resetLoadedCount);
            }
        }

        private sealed class ItemsBatchScope : IDisposable
        {
            private FeedGroupViewModel _owner;

            public ItemsBatchScope(FeedGroupViewModel owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.EndItemsBatchUpdate();
            }
        }
    }
}