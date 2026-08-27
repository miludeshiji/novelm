using System.Globalization;
using System.Runtime.CompilerServices;
using NovelM_App.Presentation.Publishing;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class PublishingPageTests
{
    [TestMethod]
    public void ToNullableInt64_UnsetOrInvalidValue_ReturnsNull()
    {
        Assert.IsNull(PublishingPage.ToNullableInt64(double.NaN));
        Assert.IsNull(PublishingPage.ToNullableInt64(double.PositiveInfinity));
        Assert.IsNull(PublishingPage.ToNullableInt64(double.NegativeInfinity));
        Assert.IsNull(PublishingPage.ToNullableInt64(-1));
    }

    [TestMethod]
    public void ToNullableInt64_ValidValue_UsesExistingMidpointToEvenRounding()
    {
        Assert.AreEqual(0L, PublishingPage.ToNullableInt64(0));
        Assert.AreEqual(42L, PublishingPage.ToNullableInt64(42.4));
        Assert.AreEqual(42L, PublishingPage.ToNullableInt64(42.5));
        Assert.AreEqual(43L, PublishingPage.ToNullableInt64(42.6));
    }

    [TestMethod]
    public void ToNullableInt64_LongMaximumParsedAsDouble_ReturnsNullWithoutThrowing()
    {
        var parsedLongMaximum = double.Parse(
            long.MaxValue.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        var twoToThePowerOf63 = Math.Pow(2, 63);

        Assert.AreEqual(twoToThePowerOf63, parsedLongMaximum);
        Assert.IsNull(PublishingPage.ToNullableInt64(parsedLongMaximum));
        Assert.IsNull(PublishingPage.ToNullableInt64(twoToThePowerOf63));
        Assert.IsNull(PublishingPage.ToNullableInt64(double.MaxValue));
    }

    [TestMethod]
    public void UpdateViewState_SaveChapterButtonUsesEditorSaveGate()
    {
        var pageSource = ReadPageSource();

        StringAssert.Contains(
            pageSource,
            "SaveChapterButton.IsEnabled = Editor.CanSaveChapter && !isBusy;");
    }

    [TestMethod]
    public void ScanChapterFolder_UsesFolderNameNaturalImageOrderAndIgnoresChildren()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"novelm-folder-{Guid.NewGuid():N}");
        var chapterFolder = Path.Combine(testRoot, "第2章");
        Directory.CreateDirectory(Path.Combine(chapterFolder, "nested"));
        try
        {
            File.WriteAllBytes(Path.Combine(chapterFolder, "10.jpg"), [1]);
            File.WriteAllBytes(Path.Combine(chapterFolder, "2.PNG"), [2]);
            File.WriteAllText(Path.Combine(chapterFolder, "note.txt"), "ignored");
            File.WriteAllBytes(Path.Combine(chapterFolder, "nested", "1.jpg"), [3]);

            var result = PublishingPage.ScanChapterFolder(chapterFolder);

            Assert.AreEqual("第2章", result.Title);
            Assert.IsNull(result.ErrorMessage);
            CollectionAssert.AreEqual(
                new[] { "2.PNG", "10.jpg" },
                result.Images.Select(item => item.FileName).ToArray());
            Assert.IsFalse(result.Images.Any(item => item.FilePath.Contains(
                "nested",
                StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public void ScanChapterFolder_EmptyFolderReturnsVisibleValidationMessage()
    {
        var chapterFolder = Path.Combine(
            Path.GetTempPath(),
            $"novelm-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(chapterFolder);
        try
        {
            var result = PublishingPage.ScanChapterFolder(chapterFolder);

            Assert.AreEqual(0, result.Images.Count);
            Assert.AreEqual("没有支持的图片。", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(chapterFolder, recursive: true);
        }
    }

    [TestMethod]
    public void ScanChapterFolder_SupportsAllImageExtensionsCaseInsensitively()
    {
        var chapterFolder = Path.Combine(
            Path.GetTempPath(),
            $"novelm-extensions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(chapterFolder);
        try
        {
            foreach (var fileName in new[]
                     {
                         "1.png", "2.JPG", "3.JpEg", "4.WEBP", "5.gif"
                     })
            {
                File.WriteAllBytes(Path.Combine(chapterFolder, fileName), [1]);
            }

            var result = PublishingPage.ScanChapterFolder(chapterFolder);

            CollectionAssert.AreEqual(
                new[] { "1.png", "2.JPG", "3.JpEg", "4.WEBP" },
                result.Images.Select(item => item.FileName).ToArray());
        }
        finally
        {
            Directory.Delete(chapterFolder, recursive: true);
        }
    }

    [TestMethod]
    public void ScanChapterFolder_NaturalTiesUseOrdinalFileNameOrder()
    {
        var chapterFolder = Path.Combine(
            Path.GetTempPath(),
            $"novelm-ties-{Guid.NewGuid():N}");
        Directory.CreateDirectory(chapterFolder);
        try
        {
            File.WriteAllBytes(Path.Combine(chapterFolder, "1.jpg"), [1]);
            File.WriteAllBytes(Path.Combine(chapterFolder, "01.jpg"), [2]);

            var result = PublishingPage.ScanChapterFolder(chapterFolder);

            CollectionAssert.AreEqual(
                new[] { "01.jpg", "1.jpg" },
                result.Images.Select(item => item.FileName).ToArray());
            var scannerSource = ExtractSourceRange(
                ReadPageSource(),
                "internal static LocalComicChapterSelection ScanChapterFolder(",
                "internal static string GetChapterFolderTitle(");
            StringAssert.Contains(
                scannerSource,
                ".ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)");
            StringAssert.Contains(
                scannerSource,
                ".ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)");
        }
        finally
        {
            Directory.Delete(chapterFolder, recursive: true);
        }
    }

    [TestMethod]
    public void ScanChapterFolder_MissingFolderReturnsSelectionErrorWithoutThrowing()
    {
        var chapterFolder = Path.Combine(
            Path.GetTempPath(),
            $"novelm-missing-{Guid.NewGuid():N}",
            "第3章");

        var result = PublishingPage.ScanChapterFolder(chapterFolder);

        Assert.AreEqual("第3章", result.Title);
        Assert.AreEqual(0, result.Images.Count);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [TestMethod]
    public void ScanChapterFolder_PreCanceledTokenThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PublishingPage.ScanChapterFolder(
                Path.Combine(Path.GetTempPath(), $"novelm-canceled-{Guid.NewGuid():N}"),
                cancellation.Token));
    }

    [TestMethod]
    public void GetChapterFolderTitle_RootPathsReturnNonEmptyNames()
    {
        Assert.AreEqual("C:", PublishingPage.GetChapterFolderTitle(@"C:\"));
        Assert.AreEqual(
            "share",
            PublishingPage.GetChapterFolderTitle(@"\\server\share\"));
        Assert.AreEqual(
            "第2章",
            PublishingPage.GetChapterFolderTitle(@"C:\comics\第2章\"));
    }

    [TestMethod]
    public void ChapterPickerHandlers_StagePathsAndExposeExplicitUploadCommands()
    {
        var pageSource = ReadPageSource();
        var selectionHandler = ExtractSourceRange(
            pageSource,
            "private async void SelectChapterImagesButton_Click(",
            "private async void UploadChapterImagesButton_Click(");
        var coverHandler = ExtractSourceRange(
            pageSource,
            "private async void SelectCoverButton_Click(",
            "private async void SaveInfoButton_Click(");
        var uploadChapterHandler = ExtractSourceRange(
            pageSource,
            "private async void UploadChapterImagesButton_Click(",
            "private void ClearPendingChapterImagesButton_Click(");
        var replaceChapterHandler = ExtractSourceRange(
            pageSource,
            "private async void ReplacePendingChapterImageButton_Click(",
            "private async void SelectBatchChapterFoldersButton_Click(");
        var uploadBatchHandler = ExtractSourceRange(
            pageSource,
            "private async void UploadBatchChaptersButton_Click(",
            "private async void RemoveBatchChapterButton_Click(");
        var replaceBatchHandler = ExtractSourceRange(
            pageSource,
            "private async void ReplaceBatchImageButton_Click(",
            "private async void RetryBatchChapterButton_Click(");
        var retryBatchHandler = ExtractSourceRange(
            pageSource,
            "private async void RetryBatchChapterButton_Click(",
            "private async void ClearChapterImagesButton_Click(");

        StringAssert.Contains(selectionHandler, "Editor.StageChapterImages(");
        Assert.IsFalse(selectionHandler.Contains("ReadAllBytes", StringComparison.Ordinal));
        Assert.IsFalse(selectionHandler.Contains(
            "UploadPendingChapterImagesAsync",
            StringComparison.Ordinal));
        StringAssert.Contains(coverHandler, "new LocalImageSource(");
        StringAssert.Contains(coverHandler, "Editor.UploadCoverAsync(");
        StringAssert.Contains(
            uploadChapterHandler,
            "ExecuteAsync(Editor.UploadPendingChapterImagesAsync)");
        StringAssert.Contains(replaceChapterHandler, "PickReplacementImageAsync()");
        StringAssert.Contains(
            replaceChapterHandler,
            "Editor.ReplaceFailedChapterImageAsync(");
        StringAssert.Contains(
            uploadBatchHandler,
            "ExecuteAsync(Editor.UploadBatchChaptersAsync)");
        StringAssert.Contains(replaceBatchHandler, "PickReplacementImageAsync()");
        StringAssert.Contains(
            replaceBatchHandler,
            "Editor.ReplaceFailedBatchImageAsync(");
        StringAssert.Contains(retryBatchHandler, "CanRetryCreate: true");
        StringAssert.Contains(
            retryBatchHandler,
            "Editor.RetryBatchChapterCreationAsync(");
    }

    [TestMethod]
    public void BatchFolderPickerHandler_OffloadsScanningAndRechecksPageContext()
    {
        var handler = ExtractSourceRange(
            ReadPageSource(),
            "private async void SelectBatchChapterFoldersButton_Click(",
            "private async void UploadBatchChaptersButton_Click(");

        StringAssert.Contains(handler, "var folderPaths = folders");
        StringAssert.Contains(handler, ".Select(folder => folder.Path)");
        StringAssert.Contains(handler, "await Task.Run(");
        StringAssert.Contains(handler, ".Select(path =>");
        StringAssert.Contains(
            handler,
            "cancellation.Value.ThrowIfCancellationRequested();");
        StringAssert.Contains(
            handler,
            "ScanChapterFolder(path, cancellation.Value)");
        StringAssert.Contains(
            handler,
            "ReferenceEquals(_pageCancellation, pageCancellation)");
        Assert.IsTrue(
            handler.IndexOf("var folderPaths = folders", StringComparison.Ordinal)
            < handler.IndexOf("await Task.Run(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PendingImagePreview_UsesStorageFileWithoutChangingUploadState()
    {
        var pageSource = ReadPageSource();
        var previewHelper = ExtractSourceRange(
            pageSource,
            "private async Task UpdatePendingImageAsync(",
            "private static BitmapImage? CreateHttpImage(");

        StringAssert.Contains(previewHelper, "var filePath = item.FilePath;");
        StringAssert.Contains(
            previewHelper,
            "StorageFile.GetFileFromPathAsync(filePath)");
        StringAssert.Contains(previewHelper, "bitmap.SetSourceAsync");
        Assert.IsTrue(
            CountOccurrences(
                previewHelper,
                "IsCurrentPendingImagePreview(image, item, filePath)") >= 2);
        Assert.IsFalse(previewHelper.Contains("item.Fail(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PendingImagePreview_RebindsOnPathChangeAndUnsubscribes()
    {
        var pageSource = ReadPageSource();
        var loadedHandler = ExtractSourceRange(
            pageSource,
            "private void PendingImage_Loaded(",
            "private void PendingImage_DataContextChanged(");
        var dataContextHandler = ExtractSourceRange(
            pageSource,
            "private void PendingImage_DataContextChanged(",
            "private void PendingImage_Unloaded(");
        var unloadedHandler = ExtractSourceRange(
            pageSource,
            "private void PendingImage_Unloaded(",
            "private void BindPendingImage(");
        var bindHelper = ExtractSourceRange(
            pageSource,
            "private void BindPendingImage(",
            "private void UnbindPendingImage(");
        var unbindHelper = ExtractSourceRange(
            pageSource,
            "private void UnbindPendingImage(",
            "private void PendingImage_PropertyChanged(");
        var propertyHandler = ExtractSourceRange(
            pageSource,
            "private void PendingImage_PropertyChanged(",
            "private async Task UpdatePendingImageAsync(");
        var currentGuard = ExtractSourceRange(
            pageSource,
            "private bool IsCurrentPendingImagePreview(",
            "internal static bool IsPendingImagePreviewCurrent(");
        var pageUnloadedHandler = ExtractSourceRange(
            pageSource,
            "private void PublishingPage_Unloaded(",
            "private void Subscribe(");

        StringAssert.Contains(loadedHandler, "BindPendingImage(");
        StringAssert.Contains(dataContextHandler, "BindPendingImage(");
        StringAssert.Contains(unloadedHandler, "UnbindPendingImage(");
        StringAssert.Contains(bindHelper, "UnbindPendingImage(image);");
        StringAssert.Contains(bindHelper, "!_isLoaded");
        StringAssert.Contains(bindHelper, "!image.IsLoaded");
        StringAssert.Contains(
            bindHelper,
            "dataContext is not PendingComicImage item");
        StringAssert.Contains(
            bindHelper,
            "item.PropertyChanged += PendingImage_PropertyChanged;");
        StringAssert.Contains(
            unbindHelper,
            "item.PropertyChanged -= PendingImage_PropertyChanged;");
        StringAssert.Contains(
            propertyHandler,
            "nameof(PendingComicImage.FilePath)");
        StringAssert.Contains(
            propertyHandler,
            "UpdatePendingImageAsync(image, item)");
        StringAssert.Contains(pageUnloadedHandler, "UnbindPendingImage(image);");
        StringAssert.Contains(currentGuard, "_pendingImageBindings.TryGetValue(");
        StringAssert.Contains(currentGuard, "ReferenceEquals(boundItem, item)");
        StringAssert.Contains(currentGuard, "_isLoaded");
        StringAssert.Contains(currentGuard, "image.IsLoaded");
        StringAssert.Contains(
            currentGuard,
            "ReferenceEquals(image.DataContext, item)");
    }

    [TestMethod]
    public void IsPendingImagePreviewCurrent_RequiresActiveLifecycleBindingAndPath()
    {
        const string originalPath = @"C:\images\1.jpg";

        Assert.IsTrue(PublishingPage.IsPendingImagePreviewCurrent(
            true,
            true,
            true,
            true,
            originalPath,
            @"c:\IMAGES\1.jpg"));
        Assert.IsFalse(PublishingPage.IsPendingImagePreviewCurrent(
            false, true, true, true, originalPath, originalPath));
        Assert.IsFalse(PublishingPage.IsPendingImagePreviewCurrent(
            true, false, true, true, originalPath, originalPath));
        Assert.IsFalse(PublishingPage.IsPendingImagePreviewCurrent(
            true, true, false, true, originalPath, originalPath));
        Assert.IsFalse(PublishingPage.IsPendingImagePreviewCurrent(
            true, true, true, false, originalPath, originalPath));
        Assert.IsFalse(PublishingPage.IsPendingImagePreviewCurrent(
            true,
            true,
            true,
            true,
            originalPath,
            @"C:\images\2.jpg"));
    }

    private static string ReadPageSource()
    {
        var testDirectory = Path.GetDirectoryName(CurrentSourceFile())!;
        var pagePath = Path.GetFullPath(Path.Combine(
            testDirectory,
            "..",
            "..",
            "..",
            "src",
            "NovelM.App",
            "Presentation",
            "Publishing",
            "PublishingPage.xaml.cs"));
        return File.ReadAllText(pagePath);
    }

    private static string ExtractSourceRange(
        string source,
        string startMarker,
        string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Missing source marker: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0;;)
        {
            index = source.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            index += value.Length;
        }
    }

    private static string CurrentSourceFile(
        [CallerFilePath] string sourceFile = "") => sourceFile;
}
