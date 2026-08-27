using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Presentation.Publishing;

public sealed partial class ComicEditorViewModel
{
    private readonly HashSet<PendingComicImage> _observedPendingChapterImages = [];
    private readonly HashSet<PendingComicChapter> _observedPendingBatchChapters = [];

    public ObservableCollection<PendingComicImage> PendingChapterImages { get; } = [];

    public ObservableCollection<PendingComicChapter> PendingBatchChapters { get; } = [];

    public bool HasPendingChapterImages => PendingChapterImages.Count > 0;

    public bool HasPendingBatchChapters =>
        PendingBatchChapters.Any(
            chapter => chapter.State != ComicChapterUploadState.Completed);

    public bool CanUploadPendingChapterImages =>
        HasPendingChapterImages
        && PendingChapterImages.Any(
            item => item.State == ComicImageUploadState.Pending)
        && !IsBusy
        && !IsUploading;

    public bool CanUploadBatchChapters =>
        HasPendingBatchChapters
        && PendingBatchChapters.All(
            chapter => chapter.HasValidImages && chapter.ErrorMessage is null)
        && !IsBusy
        && !IsUploading;

    public void StageChapterImages(IReadOnlyList<LocalImageSource> sources)
    {
        if (!IsLoaded
            || BookId is null
            || (SelectedChapter is null && !IsCreatingChapter)
            || IsBusy
            || IsUploading)
        {
            return;
        }

        var paths = PendingChapterImages
            .Select(item => NormalizePath(item.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (paths.Add(NormalizePath(source.FilePath)))
            {
                PendingChapterImages.Add(new PendingComicImage(source, 0));
            }
        }

        SortAndRenumber(PendingChapterImages);
        if (PendingChapterImages.Count > 0)
        {
            ChapterHasUnsavedChanges = true;
        }
    }

    public void StageBatchChapters(
        IReadOnlyList<LocalComicChapterSelection> selections)
    {
        if (!IsLoaded || BookId is null || IsBusy || IsUploading)
        {
            return;
        }

        var folders = PendingBatchChapters
            .Select(chapter => NormalizePath(chapter.FolderPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            if (!folders.Add(NormalizePath(selection.FolderPath)))
            {
                continue;
            }

            var images = selection.Images
                .OrderBy(image => image.FileName, NaturalNameComparer.Instance)
                .Select((image, index) => new PendingComicImage(image, index))
                .ToArray();
            PendingBatchChapters.Add(new PendingComicChapter(
                Guid.NewGuid(),
                selection.FolderPath,
                selection.Title,
                images,
                selection.ErrorMessage));
        }

        SortCollection(PendingBatchChapters, chapter => chapter.Title);
    }

    private void ObserveUploadQueues()
    {
        PendingChapterImages.CollectionChanged += OnPendingChapterImagesChanged;
        PendingBatchChapters.CollectionChanged += OnPendingBatchChaptersChanged;
    }

    private void OnPendingChapterImagesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        UpdatePendingChapterImageSubscriptions(args);
        OnPropertyChanged(nameof(HasPendingChapterImages));
        OnPropertyChanged(nameof(CanUploadPendingChapterImages));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void UpdatePendingChapterImageSubscriptions(
        NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var image in _observedPendingChapterImages.ToArray())
            {
                StopObservingPendingChapterImage(image);
            }

            foreach (var image in PendingChapterImages)
            {
                ObservePendingChapterImage(image);
            }

            return;
        }

        if (args.OldItems is not null)
        {
            foreach (PendingComicImage image in args.OldItems)
            {
                StopObservingPendingChapterImage(image);
            }
        }

        if (args.NewItems is not null)
        {
            foreach (PendingComicImage image in args.NewItems)
            {
                ObservePendingChapterImage(image);
            }
        }
    }

    private void ObservePendingChapterImage(PendingComicImage image)
    {
        if (_observedPendingChapterImages.Add(image))
        {
            image.PropertyChanged += OnPendingChapterImagePropertyChanged;
        }
    }

    private void StopObservingPendingChapterImage(PendingComicImage image)
    {
        if (_observedPendingChapterImages.Remove(image))
        {
            image.PropertyChanged -= OnPendingChapterImagePropertyChanged;
        }
    }

    private void OnPendingChapterImagePropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName == nameof(PendingComicImage.State))
        {
            OnPropertyChanged(nameof(CanUploadPendingChapterImages));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private void OnPendingBatchChaptersChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        UpdatePendingBatchChapterSubscriptions(args);
        OnPropertyChanged(nameof(HasPendingBatchChapters));
        OnPropertyChanged(nameof(CanUploadBatchChapters));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void UpdatePendingBatchChapterSubscriptions(
        NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var chapter in _observedPendingBatchChapters.ToArray())
            {
                StopObservingPendingBatchChapter(chapter);
            }

            foreach (var chapter in PendingBatchChapters)
            {
                ObservePendingBatchChapter(chapter);
            }

            return;
        }

        if (args.OldItems is not null)
        {
            foreach (PendingComicChapter chapter in args.OldItems)
            {
                StopObservingPendingBatchChapter(chapter);
            }
        }

        if (args.NewItems is not null)
        {
            foreach (PendingComicChapter chapter in args.NewItems)
            {
                ObservePendingBatchChapter(chapter);
            }
        }
    }

    private void ObservePendingBatchChapter(PendingComicChapter chapter)
    {
        if (_observedPendingBatchChapters.Add(chapter))
        {
            chapter.PropertyChanged += OnPendingBatchChapterPropertyChanged;
        }
    }

    private void StopObservingPendingBatchChapter(PendingComicChapter chapter)
    {
        if (_observedPendingBatchChapters.Remove(chapter))
        {
            chapter.PropertyChanged -= OnPendingBatchChapterPropertyChanged;
        }
    }

    private void OnPendingBatchChapterPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName == nameof(PendingComicChapter.State))
        {
            OnPropertyChanged(nameof(HasPendingBatchChapters));
            OnPropertyChanged(nameof(CanUploadBatchChapters));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            return;
        }

        if (args.PropertyName is nameof(PendingComicChapter.ErrorMessage)
            or nameof(PendingComicChapter.HasValidImages))
        {
            OnPropertyChanged(nameof(CanUploadBatchChapters));
        }
    }

    private void NotifyUploadAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanUploadPendingChapterImages));
        OnPropertyChanged(nameof(CanUploadBatchChapters));
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    private static void SortAndRenumber(
        ObservableCollection<PendingComicImage> images)
    {
        var ordered = images
            .OrderBy(item => item.FileName, NaturalNameComparer.Instance)
            .ToArray();
        images.Clear();
        for (var index = 0; index < ordered.Length; index++)
        {
            ordered[index].Position = index;
            images.Add(ordered[index]);
        }
    }

    private static void SortCollection<T>(
        ObservableCollection<T> collection,
        Func<T, string> key)
    {
        var ordered = collection
            .OrderBy(key, NaturalNameComparer.Instance)
            .ToArray();
        collection.Clear();
        foreach (var item in ordered)
        {
            collection.Add(item);
        }
    }

    private void ClearPendingChapterImagesCore()
    {
        PendingChapterImages.Clear();
    }

    private void ClearPendingBatchChaptersCore()
    {
        PendingBatchChapters.Clear();
    }
}
