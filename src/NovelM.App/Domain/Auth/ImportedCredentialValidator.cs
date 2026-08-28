using NovelM_App.Domain.Errors;

namespace NovelM_App.Domain.Auth;

public static class ImportedCredentialValidator
{
    public const int MaximumDeviceIdLength = 256;
    public const int MaximumRefreshTokenLength = 16_384;

    public static string NormalizeDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new AppException(AppErrorKind.Validation, "请输入有效的 x-id。");
        }

        var normalized = deviceId.Trim();
        if (normalized.Length > MaximumDeviceIdLength
            || normalized.Any(char.IsControl))
        {
            throw new AppException(AppErrorKind.Validation, "x-id 格式无效。");
        }

        return normalized;
    }

    public static string NormalizeRefreshToken(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AppException(AppErrorKind.Validation, "请输入 RefreshToken。");
        }

        var normalized = refreshToken.Trim();
        if (normalized.Length > MaximumRefreshTokenLength
            || normalized.Any(char.IsControl))
        {
            throw new AppException(AppErrorKind.Validation, "RefreshToken 格式无效。");
        }

        return normalized;
    }
}
