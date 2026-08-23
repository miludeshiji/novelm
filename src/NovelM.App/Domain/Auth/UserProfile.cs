namespace NovelM_App.Domain.Auth;

public sealed record UserProfile(
    long Id,
    string UserName,
    string Avatar,
    string RoleName,
    int InteriorLevel = 0);
