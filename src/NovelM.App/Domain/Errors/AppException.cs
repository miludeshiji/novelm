namespace NovelM_App.Domain.Errors;

public enum AppErrorKind
{
    Validation,
    Transport,
    Unauthorized,
    Server,
    Protocol,
    Storage,
    Unexpected
}

public sealed class AppException : Exception
{
    public AppException(
        AppErrorKind kind,
        string message,
        int? status = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Status = status;
    }

    public AppErrorKind Kind { get; }

    public int? Status { get; }
}
