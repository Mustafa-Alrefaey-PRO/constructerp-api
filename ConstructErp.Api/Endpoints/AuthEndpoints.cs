using ConstructErp.Application.Common;
using ConstructErp.Application.Identity;
using ConstructErp.Domain.Identity;
using ConstructErp.Infrastructure.Identity;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

/// <summary>
/// Sign in, stay signed in, sign out.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>After this many failures the account is locked briefly.</summary>
    private const int MaxFailedAttempts = 5;

    private const int LockoutMinutes = 15;

    /// <summary>
    /// A throwaway user used only to burn the same time a real verify costs.
    /// </summary>
    /// <remarks>
    /// Without this, an unknown email returns immediately while a known one
    /// pays for a PBKDF2 verification. That difference is measurable, and it
    /// turns response latency into the account-enumeration oracle the
    /// identical error messages were meant to close.
    /// </remarks>
    private static readonly AppUser TimingDecoy = new()
    {
        Email = "decoy@invalid",
        PasswordHash = PasswordHashing.Hash(new AppUser(), Guid.NewGuid().ToString()),
    };

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login).WithName("Login").AllowAnonymous();
        group.MapPost("/refresh", Refresh).WithName("RefreshToken").AllowAnonymous();
        group.MapPost("/logout", Logout).WithName("Logout").RequireAuthorization();
        group.MapGet("/me", Me).WithName("Me").RequireAuthorization();

        return group;
    }

    /// <summary>
    /// Exchanges an email and password for a token pair.
    /// </summary>
    /// <remarks>
    /// Every failure returns the same message. Saying "no such user" versus
    /// "wrong password" turns the login form into a way to enumerate who has
    /// an account, which is worth more to an attacker than it is to a user who
    /// mistyped their own address.
    ///
    /// Lockout is the other half: without it, a correct-password check is just
    /// a slow oracle to brute force.
    /// </remarks>
    private static async Task<IResult> Login(
        LoginRequest body,
        ErpDbContext db,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var email = body.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var user = await db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !user.IsActive)
        {
            // Verify against the decoy anyway, so this path costs what the
            // real one costs. The result is discarded.
            PasswordHashing.Verify(TimingDecoy, body.Password ?? string.Empty);

            return InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            return Results.Problem(
                title: "Account temporarily locked.",
                detail: "Too many failed attempts. Try again shortly.",
                statusCode: StatusCodes.Status423Locked);
        }

        var verification = PasswordHashing.Verify(user, body.Password ?? string.Empty);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;

            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = now.AddMinutes(LockoutMinutes);
                user.FailedAttempts = 0;
            }

            await db.SaveChangesAsync(ct);

            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The password is right but the hash used older parameters. Upgrade
            // it now, while the plaintext is in hand — there is no other moment
            // when this is possible.
            user.PasswordHash = PasswordHashing.Hash(user, body.Password!);
        }

        user.FailedAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;

        return Results.Ok(await IssueAsync(user, db, tokens, ct));
    }

    /// <summary>
    /// Trades a refresh token for a new pair, revoking the one presented.
    /// </summary>
    /// <remarks>
    /// Rotation on every use. Presenting an already-redeemed token fails,
    /// which is what makes a stolen refresh token stop working as soon as
    /// either party uses it once.
    /// </remarks>
    private static async Task<IResult> Refresh(
        RefreshRequest body,
        ErpDbContext db,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = TokenService.HashRefreshToken(body.RefreshToken ?? string.Empty);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .ThenInclude(u => u!.Organization)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User is null || !stored.IsActive(now) || !stored.User.IsActive)
        {
            return Results.Problem(
                title: "Invalid refresh token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        stored.RevokedAt = now;

        return Results.Ok(await IssueAsync(stored.User, db, tokens, ct));
    }

    /// <summary>
    /// Revokes every refresh token the user holds.
    /// </summary>
    /// <remarks>
    /// Their current access token keeps working until it expires — a JWT
    /// cannot be recalled once signed. That window is why the access token
    /// lifetime is deliberately short.
    /// </remarks>
    private static async Task<IResult> Logout(
        ErpDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var now = clock.GetUtcNow();

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        ErpDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null ? Results.Unauthorized() : Results.Ok(Describe(user));
    }

    private static async Task<AuthResultDto> IssueAsync(
        AppUser user, ErpDbContext db, TokenService tokens, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var (raw, record) = tokens.CreateRefreshToken(user);

        db.RefreshTokens.Add(record);
        await db.SaveChangesAsync(ct);

        return new AuthResultDto(access.Token, access.ExpiresAt, raw, Describe(user));
    }

    private static CurrentUserDto Describe(AppUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.OrganizationId,
        user.Organization is null
            ? null
            : new LocalizedTextDto(user.Organization.Name.En, user.Organization.Name.Ar));

    private static IResult InvalidCredentials() => Results.Problem(
        title: "Invalid email or password.",
        statusCode: StatusCodes.Status401Unauthorized);
}
