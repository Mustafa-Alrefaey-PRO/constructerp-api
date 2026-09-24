using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Identity;

/// <summary>
/// A long-lived credential that buys a new short-lived access token.
/// </summary>
/// <remarks>
/// Only a SHA-256 hash of the token is stored, for the same reason passwords
/// are hashed: a leaked database must not hand over working credentials. The
/// raw value exists once, in the response that issued it.
///
/// Rotated on use — redeeming a token revokes it and issues a replacement. A
/// second attempt to redeem the same token therefore fails, which is what
/// makes a stolen refresh token detectable rather than silently useful.
/// </remarks>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    public AppUser? User { get; set; }

    /// <summary>SHA-256 of the raw token. The raw value is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
