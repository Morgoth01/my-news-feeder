using System;

namespace MyNewsFeeder.Models
{
    public class ArchiveViewPreferences
    {
        public string GroupingMode { get; set; } = "feed";
        public string SortField { get; set; } = "archived";
        public string SortDirection { get; set; } = "desc";
        public string SelectedSavedViewName { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public string SelectedCategory { get; set; } = "All categories";
        public string SelectedFeed { get; set; } = "All feeds";
        public string SelectedLabel { get; set; } = "All labels";
        public string ReadState { get; set; } = "All";
        public DateTime? ArchivedFromDate { get; set; }
        public DateTime? ArchivedToDate { get; set; }
        public bool ShowArchivedColumn { get; set; } = true;
        public bool ShowPublishedColumn { get; set; } = true;
        public bool ShowCategoryColumn { get; set; } = true;
        public bool ShowFeedColumn { get; set; } = true;
        public bool ShowLabelsColumn { get; set; } = true;
        public bool ShowConfigurationPanel { get; set; } = true;
        public bool ShowPreviewPanel { get; set; } = true;
        public string WindowState { get; set; } = "maximized";
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public int ReadColumnIndex { get; set; } = 0;
        public int ArchivedColumnIndex { get; set; } = 1;
        public int PublishedColumnIndex { get; set; } = 2;
        public int CategoryColumnIndex { get; set; } = 3;
        public int FeedColumnIndex { get; set; } = 4;
        public int LabelsColumnIndex { get; set; } = 5;
        public int TitleColumnIndex { get; set; } = 6;

        public ArchiveViewPreferences Clone()
        {
            return new ArchiveViewPreferences
            {
                GroupingMode = GroupingMode,
                SortField = SortField,
                SortDirection = SortDirection,
                SelectedSavedViewName = SelectedSavedViewName,
                SearchText = SearchText,
                SelectedCategory = SelectedCategory,
                SelectedFeed = SelectedFeed,
                SelectedLabel = SelectedLabel,
                ReadState = ReadState,
                ArchivedFromDate = ArchivedFromDate,
                ArchivedToDate = ArchivedToDate,
                ShowArchivedColumn = ShowArchivedColumn,
                ShowPublishedColumn = ShowPublishedColumn,
                ShowCategoryColumn = ShowCategoryColumn,
                ShowFeedColumn = ShowFeedColumn,
                ShowLabelsColumn = ShowLabelsColumn,
                ShowConfigurationPanel = ShowConfigurationPanel,
                ShowPreviewPanel = ShowPreviewPanel,
                WindowState = WindowState,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop,
                ReadColumnIndex = ReadColumnIndex,
                ArchivedColumnIndex = ArchivedColumnIndex,
                PublishedColumnIndex = PublishedColumnIndex,
                CategoryColumnIndex = CategoryColumnIndex,
                FeedColumnIndex = FeedColumnIndex,
                LabelsColumnIndex = LabelsColumnIndex,
                TitleColumnIndex = TitleColumnIndex
            };
        }
    }
}