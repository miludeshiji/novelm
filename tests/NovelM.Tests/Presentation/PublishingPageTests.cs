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

        StringAssert.Contains(selectionHandler, "Editor.StageChapterImages(");
        Assert.IsFalse(selectionHandler.Contains("ReadAllBytes", StringComparison.Ordinal));
        Assert.IsFalse(selectionHandler.Contains(
            "UploadPendingChapterImagesAsync",
            StringComparison.Ordinal));
        StringAssert.Contains(coverHandler, "new LocalImageSource(");
        StringAssert.Contains(coverHandler, "Editor.UploadCoverAsync(");
        StringAssert.Contains(pageSource, "UploadChapterImagesButton_Click");
        StringAssert.Contains(pageSource, "SelectBatchChapterFoldersButton_Click");
        StringAssert.Contains(pageSource, "UploadBatchChaptersButton_Click");
        StringAssert.Contains(pageSource, "ReplacePendingChapterImageButton_Click");
        StringAssert.Contains(pageSource, "ReplaceBatchImageButton_Click");
        StringAssert.Contains(pageSource, "RetryBatchChapterButton_Click");
    }

    [TestMethod]
    public void PendingImagePreview_UsesStorageFileWithoutChangingUploadState()
    {
        var pageSource = ReadPageSource();
        var previewHelper = ExtractSourceRange(
            pageSource,
            "private static async Task UpdatePendingImageAsync(",
            "private static BitmapImage? CreateHttpImage(");

        StringAssert.Contains(previewHelper, "StorageFile.GetFileFromPathAsync");
        StringAssert.Contains(previewHelper, "bitmap.SetSourceAsync");
        StringAssert.Contains(previewHelper, "ReferenceEquals(image.DataContext, item)");
        Assert.IsFalse(previewHelper.Contains("item.Fail(", StringComparison.Ordinal));
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

    private static string CurrentSourceFile(
        [CallerFilePath] string sourceFile = "") => sourceFile;
}
