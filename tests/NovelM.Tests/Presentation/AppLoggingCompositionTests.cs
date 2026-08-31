namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class AppLoggingCompositionTests
{
    [TestMethod]
    public async Task App_ComposesPersistentLoggerAndTopLevelDiagnostics()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,
            "TestSources",
            "App.xaml.cs"));

        StringAssert.Contains(source, "new RedactedFileLog(");
        StringAssert.Contains(source, "UnhandledException += OnUnhandledException;");
        StringAssert.Contains(source, "\"application.startup.failed\"");
        StringAssert.Contains(source, "\"application.unhandled\"");
        Assert.IsFalse(source.Contains(
            "args.Handled = true",
            StringComparison.Ordinal));
    }
}
