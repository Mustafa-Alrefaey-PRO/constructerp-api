using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ConstructErp.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ConstructErp.Infrastructure.Identity;

/// <summary>Claim names this app puts on its tokens.</summary>
public static class ErpClaims
{
    public const string OrganizationId = "org";
}

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues access and refresh tokens.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions options = options.Value;

    /// <summary>
    /// A short-lived bearer token carrying the user's identity, role and org.
    /// </summary>
    /// <remarks>
    /// The organization claim is what the request-scoped ICurrentUser reads to
    /// scope queries. It is signed, so a client cannot widen its own scope by
    /// editing it — but note the flip side: the claim is a snapshot. Moving a
    /// user to a different organization takes effect when their access token
    /// expires, not the moment the row changes.
    /// </remarks>
    public IssuedToken CreateAccessToken(AppUser user)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        if (user.OrganizationId is { } organizationId)
        {
            claims.Add(new Claim(ErpClaims.OrganizationId, organizationId.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>
    /// Mints a refresh token, returning the raw value and the row to store.
    /// </summary>
    /// <remarks>
    /// The raw value is returned exactly once, to the caller that asked for it.
    /// Only its hash is persisted, so a stolen database yields no usable
    /// credential — the same reason passwords are hashed.
    /// </remarks>
    public (string Raw, RefreshToken Record) CreateRefreshToken(AppUser user)
    {
        // 32 bytes from a CSPRNG. Guid.NewGuid() is NOT a secure source and
        // must never be used for anything that acts as a credential.
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return (raw, new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(raw),
            ExpiresAt = clock.GetUtcNow().AddDays(options.RefreshTokenDays),
        });
    }

    /// <summary>
    /// SHA-256, unsalted and fast — correct here, unlike for passwords.
    /// </summary>
    /// <remarks>
    /// A refresh token is 32 random bytes, so there is no dictionary to attack
    /// and nothing for a slow KDF to protect against. Salting would also make
    /// the value unlookupable, and this is looked up by hash on every refresh.
    /// </remarks>
    public static string HashRefreshToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
}
