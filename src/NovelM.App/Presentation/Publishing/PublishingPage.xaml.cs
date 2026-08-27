using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Publishing;
using Windows.System;

namespace NovelM_App.Presentation.Publishing;

public sealed partial class PublishingPage : Page
{
    private static readonly string[] ImageFileTypes =
        [".png", ".jpg", ".jpeg", ".webp"];
    private static readonly HashSet<string> ImageFileTypeSet =
        new(ImageFileTypes, StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _pageCancellation;
    private bool _hasLoadedInitialData;
    private bool _isInitialLoadInProgress;
    private bool _isSubscribed;
    private bool _isLoaded;
    private bool _isUpdatingControls;
    private bool _isUpdatingTags;
    private bool _isComicSelectionInProgress;
    private bool _isChapterSelectionInProgress;
    private bool _isFileOperationInProgress;
    private bool _isDialogOpen;
    private string? _dismissedNoticeMessage;
    private string? _localErrorMessage;
    private string? _draggedChapterImage;
    private int _draggedChapterImageIndex = -1;

    public PublishingPage(PublishingViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += PublishingPage_Loaded;
        Unloaded += PublishingPage_Unloaded;
    }

    public event EventHandler? AccountNavigationRequested;

    public PublishingViewModel ViewModel { get; }

    public async Task<bool> ConfirmNavigationAwayAsync()
    {
        if (!Editor.HasUnsavedChanges)
        {
            return true;
        }

        return await ConfirmDiscardAsync("离开发布管理将丢弃未保存修改。");
    }

    private ComicEditorViewModel Editor => ViewModel.Editor;

    private async void PublishingPage_Loaded(object sender, RoutedEventArgs args)
    {
        _pageCancellation ??= new CancellationTokenSource();
        Subscribe();
        _isLoaded = true;
        SynchronizeEditorControls();
        UpdateViewState();

        if (_hasLoadedInitialData || _isInitialLoadInProgress)
        {
            return;
        }

        var pageCancellation = _pageCancellation;
        _isInitialLoadInProgress = true;
        try
        {
            var didLoad = await ExecuteAsync(ViewModel.LoadAsync);
            if (ReferenceEquals(_pageCancellation, pageCancellation))
            {
                _hasLoadedInitialData = didLoad;
            }
        }
        finally
        {
            if (ReferenceEquals(_pageCancellation, pageCancellation))
            {
                _isInitialLoadInProgress = false;
            }
        }
    }

    private void PublishingPage_Unloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
        _isInitialLoadInProgress = false;
        Unsubscribe();

        var cancellation = _pageCancellation;
        _pageCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Subscribe()
    {
        if (_isSubscribed)
        {
            return;
        }

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.AccountNavigationRequested += ViewModel_AccountNavigationRequested;
        Editor.PropertyChanged += Editor_PropertyChanged;
        Editor.Tags.CollectionChanged += Tags_CollectionChanged;
        Editor.ChapterImages.CollectionChanged += ChapterImages_CollectionChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.AccountNavigationRequested -= ViewModel_AccountNavigationRequested;
        Editor.PropertyChanged -= Editor_PropertyChanged;
        Editor.Tags.CollectionChanged -= Tags_CollectionChanged;
        Editor.ChapterImages.CollectionChanged -= ChapterImages_CollectionChanged;
        _isSubscribed = false;
    }

    private void ViewModel_AccountNavigationRequested(object? sender, EventArgs args)
    {
        AccountNavigationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        RunOnUiThread(() =>
        {
            if (string.IsNullOrEmpty(args.PropertyName)
                || args.PropertyName == nameof(PublishingViewModel.SelectedComic)
                || args.PropertyName == nameof(PublishingViewModel.Comics))
            {
                SynchronizeComicSelection();
            }

            UpdateViewState();
        });
    }

    private void Editor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        RunOnUiThread(() =>
        {
            if (string.IsNullOrEmpty(args.PropertyName)
                || args.PropertyName is nameof(ComicEditorViewModel.Cover)
                    or nameof(ComicEditorViewModel.CategoryId)
                    or nameof(ComicEditorViewModel.Level)
                    or nameof(ComicEditorViewModel.InteriorLevel)
                    or nameof(ComicEditorViewModel.MaximumInteriorLevel)
                    or nameof(ComicEditorViewModel.SubjectId)
                    or nameof(ComicEditorViewModel.SeriesId)
                    or nameof(ComicEditorViewModel.SelectedChapter)
                    or nameof(ComicEditorViewModel.IsCreatingChapter)
                    or nameof(ComicEditorViewModel.NewChapterSortNum)
                    or nameof(ComicEditorViewModel.IsLoaded))
            {
                SynchronizeEditorControls();
            }

            UpdateViewState();
        });
    }

    private void Tags_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        RunOnUiThread(() =>
        {
            SynchronizeTagsText();
            UpdateViewState();
        });
    }

    private void ChapterImages_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        RunOnUiThread(UpdateViewState);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.SearchAsync);
    }

    private async void ComicSearchBox_KeyDown(
        object sender,
        KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        await ExecuteAsync(ViewModel.SearchAsync);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.RefreshAsync);
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.PreviousPageAsync);
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.NextPageAsync);
    }

    private void AccountButton_Click(object sender, RoutedEventArgs args)
    {
        ViewModel.RequestAccountNavigation();
    }

    private async void ComicList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (!_isLoaded || _isUpdatingControls || _isComicSelectionInProgress)
        {
            return;
        }

        if (ComicList.SelectedItem is not MyComicSummary requested)
        {
            SynchronizeComicSelection();
            return;
        }

        var original = ViewModel.SelectedComic;
        if (original?.Id == requested.Id)
        {
            return;
        }

        _isComicSelectionInProgress = true;
        UpdateViewState();
        try
        {
            if (Editor.HasUnsavedChanges
                && !await ConfirmDiscardAsync("切换漫画将放弃当前未保存的修改。"))
            {
                SynchronizeComicSelection();
                return;
            }

            var selected = await ExecuteAsync(
                token => ViewModel.SelectComicAsync(
                    requested,
                    discardUnsavedChanges: true,
                    token));
            if (!selected)
            {
                SynchronizeComicSelection();
            }
        }
        finally
        {
            _isComicSelectionInProgress = false;
            SynchronizeComicSelection();
            UpdateViewState();
        }
    }

    private async void CreateComicButton_Click(object sender, RoutedEventArgs args)
    {
        if (Editor.HasUnsavedChanges
            && !await ConfirmDiscardAsync("发布新漫画将放弃当前未保存的修改。"))
        {
            return;
        }

        await ShowCreateComicDialogAsync();
    }

    private async void DeleteComicButton_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedComic is not { } selected)
        {
            return;
        }

        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "删除漫画",
            Content = $"确定删除《{selected.Title}》吗？此操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        });
        if (result == ContentDialogResult.Primary)
        {
            await ExecuteAsync(ViewModel.DeleteSelectedAsync);
        }
    }

    private void ComicCover_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateComicCover(image, image.DataContext);
        }
    }

    private void ComicCover_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateComicCover(image, args.NewValue);
        }
    }

    private void CategoryComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (!_isUpdatingControls
            && CategoryComboBox.SelectedItem is ComicCategory category)
        {
            Editor.CategoryId = category.Id;
        }
    }

    private async void SelectCoverButton_Click(object sender, RoutedEventArgs args)
    {
        var pageCancellation = _pageCancellation;
        var cancellation = CurrentCancellation();
        var bookId = Editor.BookId;
        if (cancellation is null || bookId is null)
        {
            return;
        }

        ClearLocalError();
        _isFileOperationInProgress = true;
        UpdateViewState();
        try
        {
            var picker = CreateImagePicker("选择封面");
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var path = file.Path;
            if (!ReferenceEquals(_pageCancellation, pageCancellation)
                || Editor.BookId != bookId)
            {
                return;
            }

            await Editor.UploadCoverAsync(
                new LocalImageSource(Guid.NewGuid(), Path.GetFileName(path), path),
                cancellation.Value);
        }
        catch (OperationCanceledException) when (cancellation.Value.IsCancellationRequested)
        {
        }
        catch
        {
            SetLocalError("读取或上传本地封面失败，请重试。");
        }
        finally
        {
            _isFileOperationInProgress = false;
            UpdateViewState();
        }
    }

    private async void SaveInfoButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(Editor.SaveInfoAsync);
    }

    private void LevelSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!_isUpdatingControls)
        {
            Editor.Level = Math.Clamp((int)Math.Round(args.NewValue), 0, 6);
        }
    }

    private void LevelNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (!_isUpdatingControls && !double.IsNaN(args.NewValue))
        {
            Editor.Level = Math.Clamp((int)Math.Round(args.NewValue), 0, 6);
        }
    }

    private void InteriorLevelNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (!_isUpdatingControls && !double.IsNaN(args.NewValue))
        {
            Editor.InteriorLevel = Math.Clamp(
                (int)Math.Round(args.NewValue),
                0,
                Editor.MaximumInteriorLevel);
        }
    }

    private void SubjectIdNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (!_isUpdatingControls)
        {
            Editor.SubjectId = ToNullableInt64(args.NewValue);
        }
    }

    private void SeriesIdNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (!_isUpdatingControls)
        {
            Editor.SeriesId = ToNullableInt64(args.NewValue);
        }
    }

    private void TagsTextBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_isUpdatingTags)
        {
            return;
        }

        var tags = TagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .ToArray();
        if (Editor.Tags.SequenceEqual(tags))
        {
            return;
        }

        _isUpdatingTags = true;
        try
        {
            Editor.Tags.Clear();
            foreach (var tag in tags)
            {
                Editor.Tags.Add(tag);
            }
        }
        finally
        {
            _isUpdatingTags = false;
        }
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(Editor.SaveSettingsAsync);
    }

    private async void ChapterList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (!_isLoaded || _isUpdatingControls || _isChapterSelectionInProgress)
        {
            return;
        }

        if (ChapterList.SelectedItem is not ComicChapterSummary requested)
        {
            SynchronizeChapterSelection();
            return;
        }

        if (!Editor.IsCreatingChapter && Editor.SelectedChapter?.Id == requested.Id)
        {
            return;
        }

        _isChapterSelectionInProgress = true;
        UpdateViewState();
        try
        {
            if (Editor.ChapterHasUnsavedChanges
                && !await ConfirmDiscardAsync("切换章节将放弃当前章节的未保存修改。"))
            {
                SynchronizeChapterSelection();
                return;
            }

            var selected = await ExecuteAsync(
                token => Editor.SelectChapterAsync(
                    requested,
                    discardChapterChanges: true,
                    token));
            if (!selected)
            {
                SynchronizeChapterSelection();
            }
        }
        finally
        {
            _isChapterSelectionInProgress = false;
            SynchronizeChapterSelection();
            UpdateViewState();
        }
    }

    private async void NewChapterButton_Click(object sender, RoutedEventArgs args)
    {
        if (Editor.ChapterHasUnsavedChanges
            && !await ConfirmDiscardAsync("新增章节将放弃当前章节的未保存修改。"))
        {
            return;
        }

        if (Editor.BeginNewChapter(discardChapterChanges: true))
        {
            SynchronizeEditorControls();
            UpdateViewState();
        }
    }

    private async void DeleteChapterButton_Click(object sender, RoutedEventArgs args)
    {
        if (Editor.SelectedChapter is not { } selected)
        {
            return;
        }

        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "删除章节",
            Content = $"确定删除章节“{selected.Title}”吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        });
        if (result == ContentDialogResult.Primary)
        {
            await ExecuteAsync(Editor.DeleteSelectedChapterAsync);
        }
    }

    private async void MoveChapterUpButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(token => Editor.MoveSelectedChapterAsync(-1, token));
    }

    private async void MoveChapterDownButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(token => Editor.MoveSelectedChapterAsync(1, token));
    }

    private async void SelectChapterImagesButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        var pageCancellation = _pageCancellation;
        var cancellation = CurrentCancellation();
        var bookId = Editor.BookId;
        var chapterId = Editor.SelectedChapter?.Id;
        var wasCreating = Editor.IsCreatingChapter;
        if (cancellation is null
            || bookId is null
            || (chapterId is null && !wasCreating))
        {
            return;
        }

        ClearLocalError();
        _isFileOperationInProgress = true;
        UpdateViewState();
        try
        {
            var picker = CreateImagePicker("选择图片");
            var files = await picker.PickMultipleFilesAsync();
            if (files is null || files.Count == 0)
            {
                return;
            }

            var localFiles = files
                .Select(file => new LocalImageSource(
                    Guid.NewGuid(),
                    Path.GetFileName(file.Path),
                    file.Path))
                .ToArray();

            if (!ReferenceEquals(_pageCancellation, pageCancellation)
                || Editor.BookId != bookId
                || Editor.SelectedChapter?.Id != chapterId
                || Editor.IsCreatingChapter != wasCreating)
            {
                return;
            }

            Editor.StageChapterImages(localFiles);
        }
        catch (OperationCanceledException) when (cancellation.Value.IsCancellationRequested)
        {
        }
        catch
        {
            SetLocalError("选择本地章节图片失败，请重试。");
        }
        finally
        {
            _isFileOperationInProgress = false;
            UpdateViewState();
        }
    }

    private async void UploadChapterImagesButton_Click(
        object sender,
        RoutedEventArgs args) =>
        await ExecuteAsync(Editor.UploadPendingChapterImagesAsync);

    private void ClearPendingChapterImagesButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        Editor.ClearPendingChapterImages();
        UpdateViewState();
    }

    private void RemovePendingChapterImageButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: PendingComicImage image })
        {
            Editor.RemovePendingChapterImage(image.Id);
            UpdateViewState();
        }
    }

    private async void ReplacePendingChapterImageButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: PendingComicImage image })
        {
            return;
        }

        var replacement = await PickReplacementImageAsync();
        if (replacement is null)
        {
            return;
        }

        await ExecuteAsync(token => Editor.ReplaceFailedChapterImageAsync(
            image.Id,
            replacement.Value.FileName,
            replacement.Value.FilePath,
            token));
    }

    private async void SelectBatchChapterFoldersButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        var pageCancellation = _pageCancellation;
        var cancellation = CurrentCancellation();
        var bookId = Editor.BookId;
        if (cancellation is null || bookId is null)
        {
            return;
        }

        ClearLocalError();
        _isFileOperationInProgress = true;
        UpdateViewState();
        try
        {
            var folders = await CreateChapterFolderPicker().PickMultipleFoldersAsync();
            if (folders is null || folders.Count == 0)
            {
                return;
            }

            var selections = folders
                .Select(folder => ScanChapterFolder(folder.Path))
                .ToArray();
            if (ReferenceEquals(_pageCancellation, pageCancellation)
                && Editor.BookId == bookId)
            {
                Editor.StageBatchChapters(selections);
            }
        }
        catch (OperationCanceledException) when (cancellation.Value.IsCancellationRequested)
        {
        }
        catch
        {
            SetLocalError("读取章节文件夹失败，请重试。");
        }
        finally
        {
            _isFileOperationInProgress = false;
            UpdateViewState();
        }
    }

    private async void UploadBatchChaptersButton_Click(
        object sender,
        RoutedEventArgs args) =>
        await ExecuteAsync(Editor.UploadBatchChaptersAsync);

    private async void RemoveBatchChapterButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: PendingComicChapter chapter })
        {
            await ExecuteAsync(token => Editor.RemoveBatchChapterAsync(
                chapter.Id,
                token));
        }
    }

    private async void RemoveBatchImageButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: PendingComicImage image })
        {
            await ExecuteAsync(token => Editor.RemoveBatchImageAsync(
                image.Id,
                token));
        }
    }

    private async void ReplaceBatchImageButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: PendingComicImage image })
        {
            return;
        }

        var replacement = await PickReplacementImageAsync();
        if (replacement is null)
        {
            return;
        }

        await ExecuteAsync(token => Editor.ReplaceFailedBatchImageAsync(
            image.Id,
            replacement.Value.FileName,
            replacement.Value.FilePath,
            token));
    }

    private async void RetryBatchChapterButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is FrameworkElement
            {
                DataContext: PendingComicChapter { CanRetryCreate: true } chapter
            })
        {
            await ExecuteAsync(token => Editor.RetryBatchChapterCreationAsync(
                chapter.Id,
                token));
        }
    }

    private async void ClearChapterImagesButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        if (Editor.ChapterImages.Count == 0)
        {
            return;
        }

        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "清空章节图片",
            Content = "确定移除当前章节中的全部图片吗？保存章节后才会提交此顺序。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        });
        if (result == ContentDialogResult.Primary)
        {
            Editor.ClearChapterImages();
        }
    }

    private void RemoveChapterImageButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var container = FindAncestor<GridViewItem>(element);
        var index = container is null
            ? -1
            : ChapterImagesGridView.IndexFromContainer(container);
        Editor.RemoveChapterImageAt(index);
    }

    private void ChapterImage_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateChapterImage(image, image.DataContext);
        }
    }

    private void ChapterImage_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateChapterImage(image, args.NewValue);
        }
    }

    private void ChapterImagesGridView_DragItemsStarting(
        object sender,
        DragItemsStartingEventArgs args)
    {
        _draggedChapterImage = null;
        _draggedChapterImageIndex = -1;
        if (Editor.IsBusy
            || Editor.IsUploading
            || args.Items.Count != 1
            || args.Items[0] is not string image)
        {
            args.Cancel = true;
            return;
        }

        _draggedChapterImage = image;
        _draggedChapterImageIndex = Editor.ChapterImages.IndexOf(image);
    }

    private void ChapterImagesGridView_DragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        try
        {
            if (_draggedChapterImage is null || _draggedChapterImageIndex < 0)
            {
                return;
            }

            var controlOrder = ChapterImagesGridView.Items
                .OfType<string>()
                .ToArray();
            if (controlOrder.Length != Editor.ChapterImages.Count
                || controlOrder.SequenceEqual(Editor.ChapterImages))
            {
                return;
            }

            var newIndex = Array.IndexOf(controlOrder, _draggedChapterImage);
            if (newIndex >= 0)
            {
                Editor.MoveChapterImage(_draggedChapterImageIndex, newIndex);
            }
        }
        finally
        {
            _draggedChapterImage = null;
            _draggedChapterImageIndex = -1;
            UpdateViewState();
        }
    }

    private async void SaveChapterButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(Editor.SaveChapterAsync);
    }

    private void NoticeInfoBar_Closed(object sender, InfoBarClosedEventArgs args)
    {
        _dismissedNoticeMessage = CurrentNoticeMessage();
    }

    private async Task ShowCreateComicDialogAsync()
    {
        var coverBox = new TextBox
        {
            Header = "封面 HTTPS 地址",
            PlaceholderText = "https://example.com/cover.jpg"
        };
        AutomationProperties.SetName(coverBox, "新漫画封面 HTTPS 地址");
        var titleBox = new TextBox { Header = "标题" };
        AutomationProperties.SetName(titleBox, "新漫画标题");
        var authorBox = new TextBox { Header = "作者" };
        AutomationProperties.SetName(authorBox, "新漫画作者");
        var introductionBox = new TextBox
        {
            Header = "简介",
            AcceptsReturn = true,
            MinHeight = 96,
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetName(introductionBox, "新漫画简介");
        var categoryBox = new ComboBox
        {
            Header = "分类",
            ItemsSource = new[] { "原创", "连载", "完结" },
            SelectedIndex = 0
        };
        AutomationProperties.SetName(categoryBox, "新漫画分类");
        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetLiveSetting(errorText, AutomationLiveSetting.Assertive);

        var content = new StackPanel { Spacing = 10, MinWidth = 360 };
        content.Children.Add(coverBox);
        content.Children.Add(titleBox);
        content.Children.Add(authorBox);
        content.Children.Add(introductionBox);
        content.Children.Add(categoryBox);
        content.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            Title = "发布漫画",
            Content = content,
            PrimaryButtonText = "发布",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            try
            {
                if (!Uri.TryCreate(coverBox.Text.Trim(), UriKind.Absolute, out var coverUri)
                    || !coverUri.Scheme.Equals(
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ShowDialogError(errorText, "请输入有效的 HTTPS 封面地址。");
                    return;
                }

                if (string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    ShowDialogError(errorText, "标题不能为空。");
                    return;
                }

                var cancellation = CurrentCancellation();
                if (cancellation is null)
                {
                    return;
                }

                ClearLocalError();
                var draft = new CreateComicDraft(
                    coverUri.AbsoluteUri,
                    titleBox.Text.Trim(),
                    authorBox.Text.Trim(),
                    introductionBox.Text.Trim(),
                    categoryBox.SelectedItem as string ?? "原创");
                var busyStarts = 0;
                PropertyChangedEventHandler busyObserver = (_, changedArgs) =>
                {
                    if (changedArgs.PropertyName == nameof(PublishingViewModel.IsBusy)
                        && ViewModel.IsBusy)
                    {
                        busyStarts++;
                    }
                };
                ViewModel.PropertyChanged += busyObserver;
                try
                {
                    await ViewModel.CreateComicAsync(draft, cancellation.Value);
                }
                finally
                {
                    ViewModel.PropertyChanged -= busyObserver;
                }

                var creationSucceeded = busyStarts >= 2
                    || (busyStarts == 1 && !ViewModel.HasError);
                if (!creationSucceeded)
                {
                    ShowDialogError(
                        errorText,
                        ViewModel.ErrorMessage ?? "发布失败，请重试。");
                    return;
                }

                args.Cancel = false;
            }
            catch (OperationCanceledException) when (
                CurrentCancellation() is not { } token || token.IsCancellationRequested)
            {
            }
            catch
            {
                SetLocalError("发布漫画失败，请重试。");
                ShowDialogError(errorText, "发布漫画失败，请重试。");
            }
            finally
            {
                deferral.Complete();
                UpdateViewState();
            }
        };

        await ShowDialogAsync(dialog);
    }

    private async Task<bool> ConfirmDiscardAsync(string message)
    {
        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "放弃未保存修改",
            Content = message,
            PrimaryButtonText = "放弃修改",
            CloseButtonText = "继续编辑",
            DefaultButton = ContentDialogButton.Close
        });
        return result == ContentDialogResult.Primary;
    }

    private async Task<ContentDialogResult?> ShowDialogAsync(ContentDialog dialog)
    {
        if (_isDialogOpen || RootGrid.XamlRoot is null)
        {
            return null;
        }

        _isDialogOpen = true;
        dialog.XamlRoot = RootGrid.XamlRoot;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (InvalidOperationException)
        {
            SetLocalError("暂时无法显示确认窗口，请稍后重试。");
            return null;
        }
        finally
        {
            _isDialogOpen = false;
            UpdateViewState();
        }
    }

    private void SynchronizeEditorControls()
    {
        if (!_isLoaded)
        {
            return;
        }

        _isUpdatingControls = true;
        try
        {
            CoverPreviewImage.Source = null;
            CoverPreviewImage.Source = CreateHttpImage(Editor.Cover);
            CategoryComboBox.SelectedItem = Editor.Categories.FirstOrDefault(
                category => category.Id == Editor.CategoryId);
            LevelSlider.Value = Editor.Level;
            LevelNumberBox.Value = Editor.Level;
            InteriorLevelNumberBox.Maximum = Editor.MaximumInteriorLevel;
            InteriorLevelNumberBox.Value = Editor.InteriorLevel;
            InteriorLevelNumberBox.Visibility = Editor.IsInteriorLevelVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            SubjectIdNumberBox.Value = Editor.SubjectId is { } subjectId
                ? subjectId
                : double.NaN;
            SeriesIdNumberBox.Value = Editor.SeriesId is { } seriesId
                ? seriesId
                : double.NaN;
            NewChapterSortNumberBox.Value = Editor.NewChapterSortNum;
            NewChapterSortNumberBox.Visibility = Editor.IsCreatingChapter
                ? Visibility.Visible
                : Visibility.Collapsed;
            ChapterEditorPanel.Visibility = Editor.SelectedChapter is not null
                || Editor.IsCreatingChapter
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            NoChapterText.Visibility = ChapterEditorPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            SynchronizeComicSelectionCore();
            SynchronizeChapterSelectionCore();
        }
        finally
        {
            _isUpdatingControls = false;
        }

        SynchronizeTagsText();
    }

    private void SynchronizeComicSelection()
    {
        if (!_isLoaded)
        {
            return;
        }

        _isUpdatingControls = true;
        try
        {
            SynchronizeComicSelectionCore();
        }
        finally
        {
            _isUpdatingControls = false;
        }
    }

    private void SynchronizeComicSelectionCore()
    {
        if (ComicList.SelectedItem is MyComicSummary selected
            && selected.Id == ViewModel.SelectedComic?.Id)
        {
            return;
        }

        ComicList.SelectedItem = ViewModel.SelectedComic;
    }

    private void SynchronizeChapterSelection()
    {
        if (!_isLoaded)
        {
            return;
        }

        _isUpdatingControls = true;
        try
        {
            SynchronizeChapterSelectionCore();
        }
        finally
        {
            _isUpdatingControls = false;
        }
    }

    private void SynchronizeChapterSelectionCore()
    {
        if (Editor.IsCreatingChapter)
        {
            ChapterList.SelectedItem = null;
            return;
        }

        if (ChapterList.SelectedItem is ComicChapterSummary selected
            && selected.Id == Editor.SelectedChapter?.Id)
        {
            return;
        }

        ChapterList.SelectedItem = Editor.SelectedChapter;
    }

    private void SynchronizeTagsText()
    {
        if (_isUpdatingTags)
        {
            return;
        }

        var tagsText = string.Join(", ", Editor.Tags);
        if (TagsTextBox.Text == tagsText)
        {
            return;
        }

        _isUpdatingTags = true;
        TagsTextBox.Text = tagsText;
        _isUpdatingTags = false;
    }

    private void UpdateViewState()
    {
        if (!_isLoaded)
        {
            return;
        }

        CheckingSessionPanel.Visibility = ViewModel.IsCheckingSession
            ? Visibility.Visible
            : Visibility.Collapsed;
        SignedOutPanel.Visibility = ViewModel.IsSignedOut && !ViewModel.IsCheckingSession
            ? Visibility.Visible
            : Visibility.Collapsed;
        WorkbenchPanel.Visibility = ViewModel.IsWorkbenchVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        var errorMessage = ViewModel.ErrorMessage
            ?? Editor.ErrorMessage
            ?? _localErrorMessage;
        ErrorInfoBar.Message = errorMessage ?? string.Empty;
        ErrorInfoBar.IsOpen = !string.IsNullOrWhiteSpace(errorMessage);

        var noticeMessage = CurrentNoticeMessage();
        NoticeInfoBar.Message = noticeMessage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(noticeMessage))
        {
            _dismissedNoticeMessage = null;
            NoticeInfoBar.IsOpen = false;
        }
        else if (ErrorInfoBar.IsOpen)
        {
            NoticeInfoBar.IsOpen = false;
        }
        else if (!string.Equals(
                     noticeMessage,
                     _dismissedNoticeMessage,
                     StringComparison.Ordinal))
        {
            NoticeInfoBar.IsOpen = true;
        }

        var isBusy = ViewModel.IsBusy
            || Editor.IsBusy
            || Editor.IsUploading
            || _isComicSelectionInProgress
            || _isChapterSelectionInProgress
            || _isFileOperationInProgress;
        BusyIndicator.IsActive = isBusy;
        ComicSearchBox.IsEnabled = !isBusy;
        SearchButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        CreateComicButton.IsEnabled = !isBusy;
        ComicList.IsEnabled = !isBusy;
        DeleteComicButton.IsEnabled = !isBusy && ViewModel.SelectedComic is not null;
        PreviousPageButton.IsEnabled = !isBusy && ViewModel.CurrentPage > 1;
        NextPageButton.IsEnabled = !isBusy && ViewModel.CurrentPage < ViewModel.TotalPages;
        PageNumberText.Text = $"{ViewModel.CurrentPage}/{ViewModel.TotalPages}";

        EditorTabs.IsEnabled = Editor.IsLoaded && !isBusy;
        SelectCoverButton.IsEnabled = Editor.IsLoaded && !isBusy;
        SaveInfoButton.IsEnabled = Editor.IsLoaded && !isBusy;
        SaveSettingsButton.IsEnabled = Editor.IsLoaded && !isBusy;
        NewChapterButton.IsEnabled = Editor.IsLoaded && !isBusy;
        ChapterList.IsEnabled = Editor.IsLoaded && !isBusy;
        SelectChapterImagesButton.IsEnabled = Editor.IsLoaded
            && (Editor.IsCreatingChapter || Editor.SelectedChapter is not null)
            && !isBusy;
        ClearChapterImagesButton.IsEnabled = Editor.ChapterImages.Count > 0 && !isBusy;
        ChapterImagesGridView.CanReorderItems = !isBusy;
        SaveChapterButton.IsEnabled = Editor.CanSaveChapter && !isBusy;

        var chapterIndex = Editor.SelectedChapter is null
            ? -1
            : Editor.Chapters.IndexOf(Editor.SelectedChapter);
        MoveChapterUpButton.IsEnabled = !isBusy && chapterIndex > 0;
        MoveChapterDownButton.IsEnabled = !isBusy
            && chapterIndex >= 0
            && chapterIndex < Editor.Chapters.Count - 1;
        DeleteChapterButton.IsEnabled = !isBusy && Editor.SelectedChapter is not null;
    }

    private string? CurrentNoticeMessage()
    {
        return ViewModel.NoticeMessage ?? Editor.NoticeMessage;
    }

    private async Task<bool> ExecuteAsync(Func<CancellationToken, Task> operation)
    {
        var cancellation = CurrentCancellation();
        if (cancellation is null)
        {
            return false;
        }

        ClearLocalError();
        try
        {
            await operation(cancellation.Value);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.Value.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            SetLocalError("操作失败，请重试。");
            return false;
        }
        finally
        {
            UpdateViewState();
        }
    }

    private async Task<bool> ExecuteAsync(
        Func<CancellationToken, Task<bool>> operation)
    {
        var cancellation = CurrentCancellation();
        if (cancellation is null)
        {
            return false;
        }

        ClearLocalError();
        try
        {
            return await operation(cancellation.Value);
        }
        catch (OperationCanceledException) when (cancellation.Value.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            SetLocalError("操作失败，请重试。");
            return false;
        }
        finally
        {
            UpdateViewState();
        }
    }

    private CancellationToken? CurrentCancellation()
    {
        var cancellation = _pageCancellation;
        return cancellation is null || cancellation.IsCancellationRequested
            ? null
            : cancellation.Token;
    }

    private Microsoft.Windows.Storage.Pickers.FileOpenPicker CreateImagePicker(
        string commitButtonText)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(
            App.MainWindow.AppWindow.Id)
        {
            CommitButtonText = commitButtonText
        };
        foreach (var fileType in ImageFileTypes)
        {
            picker.FileTypeFilter.Add(fileType);
        }

        return picker;
    }

    private Microsoft.Windows.Storage.Pickers.FolderPicker CreateChapterFolderPicker() =>
        new(App.MainWindow.AppWindow.Id)
        {
            Title = "选择章节文件夹",
            CommitButtonText = "选择文件夹"
        };

    private async Task<(string FileName, string FilePath)?> PickReplacementImageAsync()
    {
        var cancellation = CurrentCancellation();
        if (cancellation is null)
        {
            return null;
        }

        _isFileOperationInProgress = true;
        UpdateViewState();
        try
        {
            var file = await CreateImagePicker("选择替代图片").PickSingleFileAsync();
            return file is null
                ? null
                : (Path.GetFileName(file.Path), file.Path);
        }
        catch (OperationCanceledException) when (cancellation.Value.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            SetLocalError("选择替代图片失败，请重试。");
            return null;
        }
        finally
        {
            _isFileOperationInProgress = false;
            UpdateViewState();
        }
    }

    private void ClearLocalError()
    {
        _localErrorMessage = null;
        UpdateViewState();
    }

    private void SetLocalError(string message)
    {
        _localErrorMessage = message;
        UpdateViewState();
    }

    private void RunOnUiThread(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isLoaded)
                {
                    action();
                }
            });
        }
    }

    private static void UpdateComicCover(Image image, object? dataContext)
    {
        image.Source = null;
        if (dataContext is MyComicSummary comic)
        {
            AutomationProperties.SetName(image, comic.Title);
            image.Source = CreateHttpImage(comic.Cover);
        }
    }

    private static void UpdateChapterImage(Image image, object? dataContext)
    {
        image.Source = null;
        var url = dataContext as string;
        AutomationProperties.SetName(image, url ?? "章节图片");
        image.Source = CreateHttpImage(url);
    }

    private async void PendingImage_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is Image image)
        {
            await UpdatePendingImageAsync(image, image.DataContext);
        }
    }

    private async void PendingImage_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            await UpdatePendingImageAsync(image, args.NewValue);
        }
    }

    private static async Task UpdatePendingImageAsync(
        Image image,
        object? dataContext)
    {
        image.Source = null;
        if (dataContext is not PendingComicImage item)
        {
            return;
        }

        AutomationProperties.SetName(image, item.FileName);
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(
                item.FilePath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (ReferenceEquals(image.DataContext, item))
            {
                image.Source = bitmap;
            }
        }
        catch
        {
            if (ReferenceEquals(image.DataContext, item))
            {
                image.Source = null;
            }
        }
    }

    private static BitmapImage? CreateHttpImage(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new BitmapImage(uri);
    }

    internal static long? ToNullableInt64(double value)
    {
        const double firstInt64OverflowValue = 9_223_372_036_854_775_808d;
        if (!double.IsFinite(value)
            || value < 0
            || value >= firstInt64OverflowValue)
        {
            return null;
        }

        return Convert.ToInt64(Math.Round(value));
    }

    internal static LocalComicChapterSelection ScanChapterFolder(string folderPath)
    {
        var title = Path.GetFileName(folderPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        try
        {
            var images = Directory
                .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => ImageFileTypeSet.Contains(Path.GetExtension(path)))
                .OrderBy(
                    path => Path.GetFileName(path),
                    NaturalNameComparer.Instance)
                .Select(path => new LocalImageSource(
                    Guid.NewGuid(),
                    Path.GetFileName(path),
                    path))
                .ToArray();
            return new LocalComicChapterSelection(
                folderPath,
                title,
                images,
                images.Length == 0 ? "没有支持的图片。" : null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new LocalComicChapterSelection(
                folderPath,
                title,
                [],
                exception.Message);
        }
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = element; current is not null;)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void ShowDialogError(TextBlock errorText, string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }
}
