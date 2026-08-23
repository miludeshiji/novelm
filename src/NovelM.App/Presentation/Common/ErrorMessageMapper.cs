using NovelM_App.Domain.Errors;

namespace NovelM_App.Presentation.Common;

public sealed class ErrorMessageMapper
{
    private const string TransportMessage = "网络连接失败，请检查网络后重试。";
    private const string UnauthorizedMessage = "登录已失效，请重新登录。";
    private const string ProtocolMessage = "服务器响应格式不兼容。";
    private const string StorageMessage = "本地数据存储失败，请检查应用数据目录权限。";
    private const string UnexpectedMessage = "发生未预期错误，请查看诊断日志。";
    private const string ServerFallbackMessage = "服务器请求失败，请稍后重试。";

    public string Map(Exception exception)
    {
        if (exception is not AppException appException)
        {
            return UnexpectedMessage;
        }

        return appException.Kind switch
        {
            AppErrorKind.Validation => appException.Message,
            AppErrorKind.Transport => TransportMessage,
            AppErrorKind.Unauthorized => UnauthorizedMessage,
            AppErrorKind.Server => SafeServerMessage(appException.Message),
            AppErrorKind.Protocol => ProtocolMessage,
            AppErrorKind.Storage => StorageMessage,
            _ => UnexpectedMessage
        };
    }

    private static string SafeServerMessage(string message)
    {
        var safeMessage = new string(message
                .Where(character => !char.IsControl(character))
                .ToArray())
            .Trim();
        if (safeMessage.Length == 0)
        {
            return ServerFallbackMessage;
        }

        return safeMessage.Length <= 300
            ? safeMessage
            : safeMessage[..300];
    }
}
