using ConstructErp.Application.Common;

namespace ConstructErp.Application.Identity;

/// <param name="Login">A username, or an email (anything containing "@").</param>
public sealed record LoginRequest(string Login, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    string Role,
    Guid? OrganizationId,
    LocalizedTextDto? OrganizationName);

public sealed record AuthResultDto(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    CurrentUserDto User);
