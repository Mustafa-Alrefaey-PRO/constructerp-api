using ConstructErp.Application.Common;

namespace ConstructErp.Application.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record CurrentUserDto(
    Guid Id,
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
