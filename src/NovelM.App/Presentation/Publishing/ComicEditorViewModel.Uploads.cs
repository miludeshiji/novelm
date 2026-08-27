using System.Collections.ObjectModel;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Presentation.Publishing;

public sealed partial class ComicEditorViewModel
{
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
        RefreshUploadProperties();
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
        RefreshUploadProperties();
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
        RefreshUploadProperties();
    }

    private void ClearPendingBatchChaptersCore()
    {
        PendingBatchChapters.Clear();
        RefreshUploadProperties();
    }

    private void RefreshUploadProperties()
    {
        OnPropertyChanged(nameof(HasPendingChapterImages));
        OnPropertyChanged(nameof(HasPendingBatchChapters));
        OnPropertyChanged(nameof(CanUploadPendingChapterImages));
        OnPropertyChanged(nameof(CanUploadBatchChapters));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
