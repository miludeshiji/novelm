using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Presentation.Publishing;

public enum ComicImageUploadState
{
    Pending,
    Uploading,
    Uploaded,
    Failed
}

public enum ComicChapterUploadState
{
    Ready,
    UploadingImages,
    WaitingForPreviousChapter,
    CreatingChapter,
    Completed,
    Failed
}

public sealed record LocalComicChapterSelection(
    string FolderPath,
    string Title,
    IReadOnlyList<LocalImageSource> Images,
    string? ErrorMessage);

public sealed class PendingComicImage : ObservableObject
{
    private string _fileName;
    private string _filePath;
    private int _position;
    private ComicImageUploadState _state;
    private string? _uploadedUrl;
    private string? _errorMessage;

    internal PendingComicImage(LocalImageSource source, int position)
    {
        Id = source.Id;
        _fileName = source.FileName;
        _filePath = source.FilePath;
        _position = position;
    }

    public Guid Id { get; }

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public int Position
    {
        get => _position;
        internal set => SetProperty(ref _position, value);
    }

    public ComicImageUploadState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                RaiseDerived();
            }
        }
    }

    public string? UploadedUrl
    {
        get => _uploadedUrl;
        private set => SetProperty(ref _uploadedUrl, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool CanRemove =>
        State is ComicImageUploadState.Pending or ComicImageUploadState.Failed;

    public bool CanReplace => State == ComicImageUploadState.Failed;

    public string StatusText => State switch
    {
        ComicImageUploadState.Pending => "待上传",
        ComicImageUploadState.Uploading => "上传中",
        ComicImageUploadState.Uploaded => "已上传",
        ComicImageUploadState.Failed => "上传失败",
        _ => string.Empty
    };

    internal LocalImageSource ToSource() => new(Id, FileName, FilePath);

    internal void BeginUpload()
    {
        ErrorMessage = null;
        State = ComicImageUploadState.Uploading;
    }

    internal void Complete(string url)
    {
        UploadedUrl = url;
        ErrorMessage = null;
        State = ComicImageUploadState.Uploaded;
    }

    internal void Fail(string message)
    {
        UploadedUrl = null;
        ErrorMessage = message;
        State = ComicImageUploadState.Failed;
    }

    internal void Replace(string fileName, string filePath)
    {
        FileName = fileName;
        FilePath = filePath;
        UploadedUrl = null;
        ErrorMessage = null;
        State = ComicImageUploadState.Pending;
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanReplace));
        OnPropertyChanged(nameof(StatusText));
    }
}

public sealed class PendingComicChapter : ObservableObject
{
    private readonly HashSet<PendingComicImage> _observedImages = [];
    private ComicChapterUploadState _state;
    private string? _errorMessage;

    internal PendingComicChapter(
        Guid id,
        string folderPath,
        string title,
        IEnumerable<PendingComicImage> images,
        string? errorMessage)
    {
        Id = id;
        FolderPath = folderPath;
        Title = title;
        Images = new ObservableCollection<PendingComicImage>(images);
        _errorMessage = errorMessage;
        _state = errorMessage is null
            ? ComicChapterUploadState.Ready
            : ComicChapterUploadState.Failed;
        Images.CollectionChanged += OnImagesCollectionChanged;
        foreach (var image in Images)
        {
            ObserveImage(image);
        }
    }

    public Guid Id { get; }

    public string FolderPath { get; }

    public string Title { get; }

    public ObservableCollection<PendingComicImage> Images { get; }

    public ComicChapterUploadState State
    {
        get => _state;
        internal set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanRetryCreate));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set => SetProperty(ref _errorMessage, value);
    }

    public bool HasValidImages => Images.Count > 0;

    public bool AllImagesUploaded =>
        Images.Count > 0
        && Images.All(item => item.State == ComicImageUploadState.Uploaded);

    public bool CanRetryCreate =>
        State == ComicChapterUploadState.Failed
        && AllImagesUploaded;

    public string StatusText => State switch
    {
        ComicChapterUploadState.Ready => "待上传",
        ComicChapterUploadState.UploadingImages => "正在上传图片",
        ComicChapterUploadState.WaitingForPreviousChapter => "等待前序章节",
        ComicChapterUploadState.CreatingChapter => "正在创建章节",
        ComicChapterUploadState.Completed => "已完成",
        ComicChapterUploadState.Failed => "处理失败",
        _ => string.Empty
    };

    private void OnImagesCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var image in _observedImages.ToArray())
            {
                StopObservingImage(image);
            }

            foreach (var image in Images)
            {
                ObserveImage(image);
            }
        }
        else
        {
            if (args.OldItems is not null)
            {
                foreach (PendingComicImage image in args.OldItems)
                {
                    StopObservingImage(image);
                }
            }

            if (args.NewItems is not null)
            {
                foreach (PendingComicImage image in args.NewItems)
                {
                    ObserveImage(image);
                }
            }
        }

        OnPropertyChanged(nameof(HasValidImages));
        OnPropertyChanged(nameof(AllImagesUploaded));
        OnPropertyChanged(nameof(CanRetryCreate));
    }

    private void ObserveImage(PendingComicImage image)
    {
        if (_observedImages.Add(image))
        {
            image.PropertyChanged += OnImagePropertyChanged;
        }
    }

    private void StopObservingImage(PendingComicImage image)
    {
        if (_observedImages.Remove(image))
        {
            image.PropertyChanged -= OnImagePropertyChanged;
        }
    }

    private void OnImagePropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName == nameof(PendingComicImage.State))
        {
            OnPropertyChanged(nameof(AllImagesUploaded));
            OnPropertyChanged(nameof(CanRetryCreate));
        }
    }
}
