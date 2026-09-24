using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ConstructErp.Application.Common;
using ConstructErp.Domain.Identity;
using Microsoft.AspNetCore.Http;

namespace ConstructErp.Infrastructure.Identity;

/// <summary>
/// Reads the caller's identity from the validated token on the request.
/// </summary>
/// <remarks>
/// Every value here comes from claims the JWT middleware has already verified
/// against the signing key. Nothing is read from a header, a query string or
/// the request body, because those are caller-controlled — a user must not be
/// able to widen their own scope by editing a request.
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Find(JwtRegisteredClaimNames.Sub) ?? Find(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Find(ClaimTypes.Role), out var role) ? role : null;

    public Guid? OrganizationId =>
        Guid.TryParse(Find(ErpClaims.OrganizationId), out var id) ? id : null;

    /// <summary>
    /// Null for an Admin, so their queries are unscoped. Everyone else is
    /// restricted to their own organization — including a user whose token
    /// somehow carries no org claim, who then sees nothing rather than
    /// everything. Failing closed is the only safe default here.
    /// </summary>
    public Guid? ScopeOrganizationId =>
        Role switch
        {
            null => null,
            UserRole.Admin => null,
            _ => OrganizationId ?? Guid.Empty,
        };

    /// <summary>Set for a Driver only: their own assignments, not the carrier's.</summary>
    public Guid? ScopeDriverId =>
        Role == UserRole.Driver ? UserId ?? Guid.Empty : null;

    private string? Find(string claimType) => Principal?.FindFirstValue(claimType);
}
