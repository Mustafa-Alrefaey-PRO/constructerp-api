namespace ConstructErp.Infrastructure.Identity;

/// <summary>
/// Token settings, bound from the "Jwt" configuration section.
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> has no default on purpose. A hard-coded fallback
/// is the single most common way a signing key reaches production — it works
/// everywhere, so nobody notices it was never replaced, and anyone with the
/// source can mint an admin token. Startup fails loudly instead.
///
/// In development it comes from appsettings.Development.json, which is fine
/// because it only ever signs local tokens. In production it must come from an
/// environment variable or a secret store, and must never be committed.
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "constructerp";

    public string Audience { get; set; } = "constructerp-app";

    /// <summary>At least 32 bytes. Validated at startup.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Deliberately short. An access token cannot be revoked once issued, so
    /// its lifetime IS the revocation window — disabling an account takes
    /// effect within this many minutes, not instantly.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Rotated on every use; see RefreshToken.</summary>
    public int RefreshTokenDays { get; set; } = 14;
}
