using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Identity;

/// <summary>
/// Someone who can sign in.
/// </summary>
/// <remarks>
/// Two separate questions live on this record and must not be confused:
///
///   Role           — what this person may DO.
///   OrganizationId — whose data they may SEE.
///
/// An Admin has no organization scope. A TruckingCompany user is scoped to
/// their carrier. A Driver is scoped further still, to the moves assigned to
/// them personally.
///
/// The password is never stored, only a PBKDF2 hash produced by ASP.NET Core's
/// <c>PasswordHasher</c> — see PasswordHashing in Infrastructure for why that
/// hasher rather than the full Identity stack.
/// </remarks>
public sealed class AppUser : Entity
{
    /// <summary>Lowercased on save; the login identifier and unique key.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    /// <summary>
    /// Null for an Admin, who is not scoped to anyone's data.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    /// <summary>A disabled account is refused at login without being deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Reset on every success; drives lockout.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>Set when FailedAttempts crosses the threshold.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;
}
