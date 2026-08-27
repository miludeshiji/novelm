using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NovelM_App.Presentation.Publishing;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class PublishingPageTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

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
    public void ChapterUploadUi_DeclaresRequiredNamedControls()
    {
        var document = ReadXamlDocument();
        Assert.AreEqual(
            "using:NovelM_App.Presentation.Publishing",
            document.Root!.GetNamespaceOfPrefix("viewModels")?.NamespaceName);

        foreach (var (name, elementType, clickHandler) in new[]
                 {
                     ("PendingChapterImagesGridView", "GridView", (string?)null),
                     ("UploadChapterImagesButton", "Button", "UploadChapterImagesButton_Click"),
                     ("SelectBatchChapterFoldersButton", "Button", "SelectBatchChapterFoldersButton_Click"),
                     ("PendingBatchChaptersList", "ListView", null),
                     ("UploadBatchChaptersButton", "Button", "UploadBatchChaptersButton_Click"),
                     ("SelectChapterImagesButton", "Button", "SelectChapterImagesButton_Click"),
                     ("ClearPendingChapterImagesButton", "Button", "ClearPendingChapterImagesButton_Click"),
                     ("ClearChapterImagesButton", "Button", "ClearChapterImagesButton_Click")
                 })
        {
            var element = FindNamedElement(document, name);
            Assert.AreEqual(
                elementType,
                element.Name.LocalName);
            if (clickHandler is not null)
            {
                AssertAttribute(element, "Click", clickHandler);
            }
        }
    }

    [TestMethod]
    public void PendingChapterImagesGridView_BindsPreviewStateAndItemCommandsWithinItsTemplate()
    {
        var gridView = FindNamedElement(
            ReadXamlDocument(),
            "PendingChapterImagesGridView");
        AssertAttribute(
            gridView,
            "ItemsSource",
            "{x:Bind ViewModel.Editor.PendingChapterImages, Mode=OneWay}");
        var imageTemplate = FindTypedDataTemplate(
            gridView,
            "viewModels:PendingComicImage");

        AssertPendingImagePreview(imageTemplate);
        AssertTemplateTextBinding(imageTemplate, "FileName");
        AssertTemplateTextBinding(imageTemplate, "StatusText");
        AssertTemplateTextBinding(imageTemplate, "ErrorMessage");
        var replaceButton = AssertTemplateButton(
            imageTemplate,
            "ReplacePendingChapterImageButton",
            "ReplacePendingChapterImageButton_Click",
            "CanReplace");
        AssertAttribute(replaceButton, "Content", "重新选择并上传");
        AssertTemplateButton(
            imageTemplate,
            "RemovePendingChapterImageButton",
            "RemovePendingChapterImageButton_Click",
            "CanRemove");
    }

    [TestMethod]
    public void PendingBatchChaptersList_BindsChapterAndNestedImageContractsWithinTheirTemplates()
    {
        var listView = FindNamedElement(
            ReadXamlDocument(),
            "PendingBatchChaptersList");
        AssertAttribute(
            listView,
            "ItemsSource",
            "{x:Bind ViewModel.Editor.PendingBatchChapters, Mode=OneWay}");
        var chapterTemplate = FindTypedDataTemplate(
            listView,
            "viewModels:PendingComicChapter");

        AssertTemplateTextBinding(chapterTemplate, "Title", oneWay: false);
        AssertTemplateTextBinding(chapterTemplate, "StatusText");
        AssertTemplateTextBinding(chapterTemplate, "ErrorMessage");
        AssertTemplateButton(
            chapterTemplate,
            "RetryBatchChapterButton",
            "RetryBatchChapterButton_Click",
            "CanRetryCreate");
        AssertTemplateButton(
            chapterTemplate,
            "RemoveBatchChapterButton",
            "RemoveBatchChapterButton_Click");

        var imageTemplate = FindTypedDataTemplate(
            chapterTemplate,
            "viewModels:PendingComicImage");
        AssertPendingImagePreview(imageTemplate);
        AssertTemplateTextBinding(imageTemplate, "FileName");
        AssertTemplateTextBinding(imageTemplate, "StatusText");
        AssertTemplateTextBinding(imageTemplate, "ErrorMessage");
        var replaceButton = AssertTemplateButton(
            imageTemplate,
            "ReplaceBatchImageButton",
            "ReplaceBatchImageButton_Click",
            "CanReplace");
        AssertAttribute(replaceButton, "Content", "重新选择并上传");
        AssertTemplateButton(
            imageTemplate,
            "RemoveBatchImageButton",
            "RemoveBatchImageButton_Click",
            "CanRemove");
    }

    [TestMethod]
    public void ChapterUploadUi_UpdateViewStateAndSubscriptionsFollowQueues()
    {
        var pageSource = ReadPageSource();
        var subscribe = ExtractSourceRange(
            pageSource,
            "private void Subscribe()",
            "private void Unsubscribe()");
        var unsubscribe = ExtractSourceRange(
            pageSource,
            "private void Unsubscribe()",
            "private void ViewModel_AccountNavigationRequested(");
        var updateViewState = ExtractSourceRange(
            pageSource,
            "private void UpdateViewState()",
            "private string? CurrentNoticeMessage()");

        StringAssert.Contains(
            subscribe,
            "Editor.PendingChapterImages.CollectionChanged += UploadQueues_CollectionChanged;");
        StringAssert.Contains(
            subscribe,
            "Editor.PendingBatchChapters.CollectionChanged += UploadQueues_CollectionChanged;");
        StringAssert.Contains(
            unsubscribe,
            "Editor.PendingChapterImages.CollectionChanged -= UploadQueues_CollectionChanged;");
        StringAssert.Contains(
            unsubscribe,
            "Editor.PendingBatchChapters.CollectionChanged -= UploadQueues_CollectionChanged;");
        StringAssert.Contains(
            updateViewState,
            "PendingChapterImagesPanel.Visibility = Editor.HasPendingChapterImages");
        StringAssert.Contains(
            updateViewState,
            "UploadChapterImagesButton.IsEnabled = Editor.CanUploadPendingChapterImages");
        StringAssert.Contains(
            updateViewState,
            "ClearPendingChapterImagesButton.IsEnabled = Editor.HasPendingChapterImages");
        StringAssert.Contains(
            updateViewState,
            "SelectBatchChapterFoldersButton.IsEnabled = Editor.IsLoaded && !isBusy;");
        StringAssert.Contains(
            updateViewState,
            "UploadBatchChaptersButton.IsEnabled = Editor.CanUploadBatchChapters && !isBusy;");
        StringAssert.Contains(
            updateViewState,
            "BatchUploadProgressText.Text = Editor.BatchProgressText;");
        StringAssert.Contains(
            updateViewState,
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

    private static XDocument ReadXamlDocument()
    {
        var testDirectory = Path.GetDirectoryName(CurrentSourceFile())!;
        var xamlPath = Path.GetFullPath(Path.Combine(
            testDirectory,
            "..",
            "..",
            "..",
            "src",
            "NovelM.App",
            "Presentation",
            "Publishing",
            "PublishingPage.xaml"));
        return XDocument.Load(xamlPath, LoadOptions.PreserveWhitespace);
    }

    private static XElement FindNamedElement(XContainer scope, string name)
    {
        var matches = scope.Descendants()
            .Where(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == name)
            .ToArray();
        Assert.AreEqual(
            1,
            matches.Length,
            $"Expected one named XAML control '{name}'.");
        return matches.Single();
    }

    private static XElement FindTypedDataTemplate(
        XElement scope,
        string dataType)
    {
        var matches = scope.Descendants()
            .Where(element =>
                element.Name.LocalName == "DataTemplate"
                && (string?)element.Attribute(XamlNamespace + "DataType") == dataType)
            .ToArray();
        Assert.AreEqual(
            1,
            matches.Length,
            $"Expected one DataTemplate for '{dataType}' in '{scope.Name.LocalName}'.");
        return matches.Single();
    }

    private static IEnumerable<XElement> OwnTemplateDescendants(
        XElement template) =>
        template.Descendants().Where(element => ReferenceEquals(
            element.Ancestors().FirstOrDefault(ancestor =>
                ancestor.Name.LocalName == "DataTemplate"),
            template));

    private static XElement FindNamedElementInTemplate(
        XElement template,
        string name)
    {
        var matches = OwnTemplateDescendants(template)
            .Where(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == name)
            .ToArray();
        Assert.AreEqual(
            1,
            matches.Length,
            $"Expected one '{name}' control in the '{(string?)template.Attribute(XamlNamespace + "DataType")}' template body.");
        return matches.Single();
    }

    private static void AssertAttribute(
        XElement element,
        string attributeName,
        string expectedValue)
    {
        Assert.AreEqual(
            expectedValue,
            (string?)element.Attribute(attributeName),
            $"Unexpected {attributeName} on '{(string?)element.Attribute(XamlNamespace + "Name") ?? element.Name.LocalName}'.");
    }

    private static void AssertPendingImagePreview(XElement imageTemplate)
    {
        var images = OwnTemplateDescendants(imageTemplate)
            .Where(element => element.Name.LocalName == "Image")
            .ToArray();
        Assert.AreEqual(1, images.Length, "Expected one preview Image in the image template.");
        AssertAttribute(images[0], "Loaded", "PendingImage_Loaded");
        AssertAttribute(
            images[0],
            "DataContextChanged",
            "PendingImage_DataContextChanged");
        AssertAttribute(images[0], "Unloaded", "PendingImage_Unloaded");
    }

    private static void AssertTemplateTextBinding(
        XElement template,
        string propertyName,
        bool oneWay = true)
    {
        var expectedBinding = oneWay
            ? $"{{x:Bind {propertyName}, Mode=OneWay}}"
            : $"{{x:Bind {propertyName}}}";
        Assert.IsTrue(
            OwnTemplateDescendants(template).Any(element =>
                element.Name.LocalName == "TextBlock"
                && (string?)element.Attribute("Text") == expectedBinding),
            $"Missing TextBlock binding '{expectedBinding}' in the '{(string?)template.Attribute(XamlNamespace + "DataType")}' template body.");
    }

    private static XElement AssertTemplateButton(
        XElement template,
        string name,
        string clickHandler,
        string? enabledProperty = null)
    {
        var button = FindNamedElementInTemplate(template, name);
        Assert.AreEqual("Button", button.Name.LocalName);
        AssertAttribute(button, "Click", clickHandler);
        if (enabledProperty is not null)
        {
            AssertAttribute(
                button,
                "IsEnabled",
                $"{{x:Bind {enabledProperty}, Mode=OneWay}}");
        }

        return button;
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
